using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Inventory__Items__Pickups;
using HarmonyLib;
using UnityEngine;

namespace AzerothAbilities;

// Source casts/statuses; enemy selection/3D floor placement is an explicit Unity adapter.
// Unknown source targeting words are retained in the catalog and never advertised as proven.
internal static class ClassSpellBehaviors
{
    sealed class Dot
    {internal LearnedSpell Spell;internal Enemy Enemy;internal uint Id;internal float Damage,Next,Interval;internal int Remaining,CastToken;internal string TalentId;}
    sealed class Area
    {internal LearnedSpell Spell;internal Vector3 Center;internal float Radius,Range,Damage,Next,Interval;internal int Remaining,Targets;internal bool Totem;}
    sealed class Hot
    {internal string Id;internal float Remaining,Rate,Last;}
    sealed class Control
    {internal Enemy Enemy;internal uint Id;internal float SlowUntil,SlowFactor=1,RootUntil,StunUntil,FearUntil,DebuffUntil,DamageReduction,VulnerabilityUntil,Vulnerability;internal Vector3 Wander;internal float ChangeDirection;}
    static readonly List<Dot> dots=new();static readonly List<Area> areas=new();static readonly List<Hot> hots=new();
    static readonly Dictionary<IntPtr,Control> controls=new();
    static readonly Dictionary<string,(float Amount,float Until)> thorns=new();
    static bool reflecting;
    internal static bool Alive(Enemy enemy)=>enemy!=null && !enemy.IsDeadOrDyingNextFrame();
    internal static float SoftCapRadius(float value)=>value<=70?value:70+10*(float)Math.Log(1+(double)((value-70)/10));
    internal static float Radius(LearnedSpell spell)
    {
        float radius=spell.Definition.Radius*spell.RadiusFactor*Mathf.Max(.1f,1+ClassCombat.SpellBonus(spell,3))*TomeCompatibility.Size;
        // Source cast branches2/3/4 call log1pf above70. Passives and totems
        // have separate native paths and are not assumed to share this cap.
        return spell.Definition.CastKindId is 2 or 3 or 4?SoftCapRadius(radius):radius;
    }
    internal static float ConeReach(LearnedSpell spell,float range)=>SoftCapRadius(range*spell.RadiusFactor*Mathf.Max(.1f,1+ClassCombat.SpellBonus(spell,3))*TomeCompatibility.Size);
    internal static float Duration(LearnedSpell spell)=>spell.Definition.Duration*(AbilityRuntime.Catalog?.DurationScale??1.4f)*spell.DurationFactor*Mathf.Max(.1f,1+ClassCombat.SpellBonus(spell,4))*TomeCompatibility.Duration;
    internal static float StatusFactor(LearnedSpell spell)=>Mathf.Max(.1f,1+ClassCombat.SpellBonus(spell,6));
    internal static int Quantity(LearnedSpell spell)=>Math.Max(1,spell.Definition.Targets+spell.ExtraTargets+(int)ClassCombat.SpellBonus(spell,2)+TomeCompatibility.ExtraProjectiles);
    internal static void Fx(LearnedSpell spell,string stage,Vector3 position,float scale=1,float duration=.5f)=>SpellVfx.Play(spell.Definition.Id,stage,position,scale,duration);
    static Vector3 Origin(MyPlayer player)=>AbilityRuntime.OverrideCastOrigin??player.transform.position;
    static Control Get(Enemy enemy)
    {if(!controls.TryGetValue(enemy.Pointer,out var c)||c.Id!=enemy.id)controls[enemy.Pointer]=c=new Control{Enemy=enemy,Id=enemy.id};return c;}
    internal static bool TryCast(LearnedSpell spell,MyPlayer player)
    {
        if(!ClassCombat.GenericActive || !CombatSimulation.CanSimulate || player?.inventory==null)return false;
        var d=spell.Definition;var origin=Origin(player);float damage=AbilityRuntime.Damage(spell)*AbilityRuntime.OverrideCastDamage;
        if(d.Kind=="passive")return false;
        if(d.Kind is "selfBuff" or "barrier")return Self(spell,player);
        float range=AbilityRuntime.OverrideCastRange>0?AbilityRuntime.OverrideCastRange:d.Range;
        if(d.Kind=="selfAoe")
        {
            if(TargetAcquisition.SkillTargets(origin,Radius(spell),false).Count==0)return false;
            var enemies=AbilityRuntime.Targets(origin,Radius(spell),false);
            foreach(var enemy in enemies)AbilityRuntime.Hit(spell,enemy,damage,origin);
            Fx(spell,"cast",origin,spell.RadiusFactor*TomeCompatibility.Size,.7f);return true;
        }
        if(d.Kind=="totem")
        {
            // Both original totems are stationary: Magma damages in its radius,
            // Searing selects finite targets in range. They retain their source tick budget.
            var enemies=TargetAcquisition.SkillTargets(origin,Mathf.Max(d.Range,Radius(spell)),false);if(enemies.Count==0)return false;
            AddArea(spell,origin,true,damage);return true;
        }
        if(d.Kind=="groundAoe")
        {
            Vector3 center=origin;
            if(range>0)
            {
                var candidates=TargetAcquisition.SkillTargets(origin,range,false);if(candidates.Count==0)return false;
                float radius=Radius(spell);int best=-1;
                foreach(var candidate in candidates.Take(24))
                {int count=candidates.Count(e=>(e.transform.position-candidate.transform.position).sqrMagnitude<=radius*radius);if(count>best){best=count;center=candidate.transform.position;}}
            }
            else if(TargetAcquisition.SkillTargets(origin,Radius(spell),false).Count==0)return false;
            AddArea(spell,center,false,damage);return true;
        }
        if(d.Kind=="cone")
        {
            float reach=ConeReach(spell,range);
            Vector3 forward=player.transform.forward;forward.y=0;forward.Normalize();
            bool InCone(Enemy e){var v=e.transform.position-origin;v.y=0;return v.sqrMagnitude>.0001f&&Vector3.Dot(v.normalized,forward)>.5f;}
            if(!TargetAcquisition.SkillTargets(origin,reach,false).Any(InCone))return false;
            var enemies=AbilityRuntime.Targets(origin,reach,false).Where(e=>{var v=e.transform.position-origin;v.y=0;return v.sqrMagnitude>.0001f&&Vector3.Dot(v.normalized,forward)>.5f;}).ToArray();
            foreach(var e in enemies)AbilityRuntime.Hit(spell,e,damage,origin);Fx(spell,"cast",origin,spell.RadiusFactor*TomeCompatibility.Size,.7f);return true;
        }
        var targets=TargetAcquisition.SkillTargets(origin,range,d.Kind is not ("projectile" or "missiles" or "damageOverTime"));if(targets.Count==0)return false;
        if(d.Kind=="damageOverTime"){targets=targets.Where(e=>!HasDot(e,d.Id)).ToList();if(targets.Count==0)return false;}
        int quantity=Quantity(spell);
        if(d.Kind=="chain")
        {
            var selected=new HashSet<uint>();Enemy target=targets[0];Vector3 at=origin;
            for(int i=0;i<quantity && target!=null;i++)
            {
                selected.Add(target.id);AbilityRuntime.Hit(spell,target,damage*(ClassCombat.ChainNoFalloff?1:Mathf.Pow(.92f,i)),at);Fx(spell,"impact",target.transform.position+Vector3.up,1,.35f);
                at=target.transform.position;target=AbilityRuntime.Targets(at,Radius(spell),false).FirstOrDefault(e=>!selected.Contains(e.id));
            }
            return true;
        }
        // Source DoTs keep one delivery per eligible un-afflicted target;
        // their moving deliveries still share the volley reservation policy.
        var dotVolley=d.Kind=="damageOverTime"&&d.ProjectileSpeed>0?TargetAcquisition.Prepare(spell,origin,range,targets,Math.Min(quantity,targets.Count)):null;
        int deliveryIndex=0;
        foreach(var target in targets.Take(Math.Min(quantity,targets.Count)))
        {
            if(d.Kind=="damageOverTime")
            {
                if(d.ProjectileSpeed>0)AbilityRuntime.AddVolleyProjectile(spell,target,origin+Vector3.up*.8f,damage,0,dotVolley,deliveryIndex++);
                else ApplySpellDot(spell,target,damage,AbilityRuntime.CurrentCastSerial);
            }
            else if(d.Kind is "melee" or "finisher" or "instant" || d.ProjectileSpeed<=0)
            {
                float hit=d.Kind=="finisher" && target.hp<target.maxHp*.30f?damage*2:damage;
                AbilityRuntime.Hit(spell,target,hit,origin);
            }
        }
        if(d.Kind is "projectile" or "missiles" && d.ProjectileSpeed>0)
        {
            // User policy: claim each nearest distinct target before reusing
            // one. Reservation lifetime includes delayed launches and retargets.
            var volley=TargetAcquisition.Prepare(spell,origin,range,targets,quantity);
            for(int i=0;i<volley.Assigned.Count;i++)
            {
                var target=volley.Assigned[i];var launch=TomeCompatibility.LaunchOffset(origin+Vector3.up*.8f,target.transform.position,i,quantity);
                AbilityRuntime.AddVolleyProjectile(spell,target,launch,damage,i*.06f,volley,i);
            }
        }
        Fx(spell,"cast",origin+Vector3.up,1,.3f);return true;
    }
    internal static void ApplySpellDot(LearnedSpell spell,Enemy target,float damage,int castToken)
    {
        if(!ClassCombat.GenericActive||!CombatSimulation.CanSimulate||!Alive(target))return;
        var d=spell.Definition;float duration=Duration(spell),interval=d.Duration/Math.Max(1,d.Ticks);
        int ticks=TickCount(spell,duration);float perTick=damage*(duration/Mathf.Max(.01f,d.Duration))/ticks;
        dots.RemoveAll(dot=>dot.Id==target.id&&dot.Enemy.Pointer==target.Pointer&&dot.Spell.Definition.Id==d.Id);
        // Original queued-hit delivery writes current source time to status+1C
        // (1459197ED), then the status consumer ticks when now>=nextTick.
        dots.Add(new Dot{Spell=spell,Enemy=target,Id=target.id,Damage=perTick,Remaining=ticks,Next=CombatSimulation.Time,Interval=interval,CastToken=castToken});
        Fx(spell,"aura",target.transform.position,1,duration);
    }
    static int TickCount(LearnedSpell spell,float duration)=>Math.Max(1,(int)MathF.Round(duration/Mathf.Max(.01f,spell.Definition.Duration)*Math.Max(1,spell.Definition.Ticks),MidpointRounding.AwayFromZero));
    static void AddArea(LearnedSpell spell,Vector3 center,bool totem,float damage)
    {
        float duration=Duration(spell),radius=Radius(spell);int count=TickCount(spell,duration);
        areas.Add(new Area{Spell=spell,Center=center,Radius=radius,Range=spell.Definition.Range,Damage=damage/Math.Max(1,spell.Definition.Ticks),Remaining=count,Next=CombatSimulation.Time+.25f,Interval=duration/count,Targets=spell.Definition.Targets>0?Quantity(spell):0,Totem=totem});
        bool consecration=!totem&&spell.Definition.Id=="consecration";
        // Original ground cast sends its initial visual before the persistent
        // object (14596100E/1459610F8). Keep each cast asset's original clip life.
        if(consecration)SpellVfx.PlaySourceOneShot(spell.Definition.Id,"cast",center);
        // Source spell26573 base radius8: dynamicobject scale=effectiveRadius/8
        // (145964D30..D46, lookup145966A29). Share the actual gameplay radius.
        Fx(spell,totem?"aura":"area",center,consecration?radius/8:spell.RadiusFactor*TomeCompatibility.Size,duration);
    }
    static bool Self(LearnedSpell spell,MyPlayer player)
    {
        var d=spell.Definition;var hp=player.inventory.playerHealth;if(hp==null)return false;
        float duration=Duration(spell);
        float damageFactor=Mathf.Max(0,spell.DamageFactor*(1+ClassCombat.SpellBonus(spell,0)));
        float scale=(1+ClassCombat.StatBonus(23)+ClassCombat.TalentStat(23))*(AbilityRuntime.Catalog?.DamageScale??1.4f)*Mathf.Sqrt(damageFactor);
        string id="spell:"+d.Id;
        switch(d.StatusKind)
        {
            case 6:
                if(hp.hp>hp.maxHp*.85f)return false;
                float healing=hp.maxHp*d.StatusValue*scale;
                if(duration>0){hots.RemoveAll(h=>h.Id==id);hots.Add(new Hot{Id=id,Remaining=duration,Rate=healing/duration,Last=CombatSimulation.Time});}
                else ClassCombat.Heal(healing,id);
                break;
            case 7:
                if(ClassCombat.ShieldRemaining>0)return false;
                ClassCombat.ApplyShield(hp.maxHp*d.StatusValue*scale,duration,id);break;
            case 8:ClassCombat.ApplyTimedStat(0,d.StatusValue*StatusFactor(spell),duration,id);break;
            case 9:ClassCombat.ApplyTimedStat(1,d.StatusValue*StatusFactor(spell),duration,id);break;
            case 11:
                if(hp.hp>hp.maxHp*.35f)return false;
                ClassCombat.SetInvulnerable(duration,id);break;
            case 12:thorns[id]=(d.StatusValue*damageFactor,CombatSimulation.Time+duration);break;
            default:return false;
        }
        Fx(spell,"aura",player.transform.position,1,Mathf.Max(.3f,duration));return true;
    }
    internal static void AfterHit(LearnedSpell spell,Enemy target,DamageContainer dc,float actualDamage)
    {
        var d=spell.Definition;float duration=TomeCompatibility.EffectDuration;float scale=StatusFactor(spell);
        switch(d.StatusKind)
        {
            case 1:
                if(Alive(target)){var c=Get(target);if(c.SlowUntil<=CombatSimulation.Time)c.SlowFactor=1;c.SlowFactor=Mathf.Min(c.SlowFactor,1-Mathf.Min(.9f,d.StatusValue*scale));c.SlowUntil=Mathf.Max(c.SlowUntil,CombatSimulation.Time+d.StatusDuration*duration);}break;
            case 2:ApplyControl(target,d.StatusValue*duration,2);break;
            case 3:ApplyControl(target,d.StatusValue*duration,3);break;
            case 4:ApplyControl(target,d.StatusValue*duration,4);break;
            case 5:ClassCombat.Heal(Mathf.Max(0,actualDamage)*d.StatusValue*scale,"lifesteal:"+d.Id);break;
            case 6:ClassCombat.Heal(ClassCombat.MaximumHealth*d.StatusValue*scale,"heal:"+d.Id);break;
            case 7:ClassCombat.ApplyShield(ClassCombat.MaximumHealth*d.StatusValue*scale,0,"shield:"+d.Id);break;
            case 10:
                if(Alive(target)){var c=Get(target);c.DamageReduction=Mathf.Max(c.DebuffUntil>CombatSimulation.Time?c.DamageReduction:0,Mathf.Min(.9f,d.StatusValue*scale));c.DebuffUntil=Mathf.Max(c.DebuffUntil,CombatSimulation.Time+d.StatusDuration*duration);}break;
        }
    }
    internal static void ApplyControl(Enemy enemy,float duration,int kind)
    {
        if(!Alive(enemy)||duration<=0)return;if(enemy.IsBoss())duration*=.25f;
        var c=Get(enemy);float until=CombatSimulation.Time+duration;
        if(kind==2)c.RootUntil=Mathf.Max(c.RootUntil,until);else if(kind==3)c.StunUntil=Mathf.Max(c.StunUntil,until);else c.FearUntil=Mathf.Max(c.FearUntil,until);
    }
    internal static void ApplyTalentStatus(Enemy enemy,int kind,float value,float duration,string id)
    {
        if(!Alive(enemy))return;var c=Get(enemy);float until=CombatSimulation.Time+Mathf.Max(0,duration);
        if(kind==-1){c.Vulnerability=Mathf.Max(c.VulnerabilityUntil>CombatSimulation.Time?c.Vulnerability:0,Mathf.Max(0,value));c.VulnerabilityUntil=Mathf.Max(c.VulnerabilityUntil,until);}
        else if(kind==1){c.SlowFactor=Mathf.Min(c.SlowUntil>CombatSimulation.Time?c.SlowFactor:1,1-Mathf.Clamp(value,0,.9f));c.SlowUntil=Mathf.Max(c.SlowUntil,until);}
        else if(kind is 2 or 3 or 4)ApplyControl(enemy,value>0?value:duration,kind);
        else if(kind==10){c.DamageReduction=Mathf.Max(c.DebuffUntil>CombatSimulation.Time?c.DamageReduction:0,Mathf.Clamp(value,0,.9f));c.DebuffUntil=Mathf.Max(c.DebuffUntil,until);}
    }
    internal static float Vulnerability(Enemy enemy)=>enemy!=null&&controls.TryGetValue(enemy.Pointer,out var c)&&c.Id==enemy.id&&c.VulnerabilityUntil>CombatSimulation.Time?1+c.Vulnerability:1;
    internal static bool IsControlled(Enemy enemy)
    {if(enemy==null || !controls.TryGetValue(enemy.Pointer,out var c)||c.Id!=enemy.id)return false;float now=CombatSimulation.Time;return c.RootUntil>now||c.StunUntil>now||c.FearUntil>now||c.SlowUntil>now||ClassPassives.IsSheep(enemy);}
    internal static bool IsHarmless(Enemy enemy)=>ClassPassives.IsSheep(enemy)||(enemy!=null && controls.TryGetValue(enemy.Pointer,out var c)&&c.Id==enemy.id&&(c.FearUntil>CombatSimulation.Time||c.StunUntil>CombatSimulation.Time));
    internal static float SpeedFactor(Enemy enemy)
    {if(!ClassCombat.GenericActive || enemy==null || !controls.TryGetValue(enemy.Pointer,out var c)||c.Id!=enemy.id)return 1;if(c.RootUntil>CombatSimulation.Time||c.StunUntil>CombatSimulation.Time)return 0;return c.SlowUntil>CombatSimulation.Time?c.SlowFactor:1;}
    internal static float EnemyDamageFactor(Enemy enemy)=>controls.TryGetValue(enemy.Pointer,out var c)&&c.Id==enemy.id&&c.DebuffUntil>CombatSimulation.Time?1-c.DamageReduction:1;
    internal static bool Wander(Actors.Enemies.EnemyMovementRb movement)
    {
        if(!ClassCombat.GenericActive || !CombatSimulation.CanSimulate)return false;
        var enemy=movement.enemy;if(!Alive(enemy))return false;
        bool sheep=ClassPassives.IsSheep(enemy);var c=Get(enemy);
        if(!sheep && c.FearUntil<=CombatSimulation.Time)return false;
        if(CombatSimulation.Time>=c.ChangeDirection){float angle=(enemy.id*.618034f+Mathf.Floor(CombatSimulation.Time*.5f))*Mathf.PI*2;c.Wander=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(sheep?.8f:2);c.ChangeDirection=CombatSimulation.Time+2;}
        if(movement.rb!=null){var velocity=movement.rb.velocity;movement.rb.velocity=new Vector3(c.Wander.x,velocity.y,c.Wander.z);}return true;
    }
    internal static void Reflect(Enemy enemy)
    {
        if(reflecting || !Alive(enemy))return;
        float amount=thorns.Values.Where(t=>t.Until>CombatSimulation.Time).Sum(t=>t.Amount);if(amount<=0)return;
        var spell=AbilityRuntime.Spells.FirstOrDefault(s=>s.Definition.StatusKind==12);if(spell==null)return;
        reflecting=true;try{AbilityRuntime.Hit(spell,enemy,amount,MyPlayer.Instance.transform.position);}finally{reflecting=false;}
    }
    internal static void Tick()
    {
        if(!CombatSimulation.CanSimulate)return;
        foreach(var dot in dots.ToArray())
        {
            if(!Alive(dot.Enemy)||dot.Enemy.id!=dot.Id){dots.Remove(dot);continue;}
            if(CombatSimulation.Time<dot.Next)continue;
            dot.Remaining--;dot.Next+=dot.Interval;
            float actual=dot.TalentId!=null?AbilityRuntime.HitTalentDot(dot.Spell,dot.Enemy,dot.Damage,dot.TalentId)
                :AbilityRuntime.HitMeasured(dot.Spell,dot.Enemy,dot.Damage,dot.Enemy.transform.position,dot.CastToken,false);
            ClassCombat.Event(4,dot.Spell,dot.Enemy,actual);
            if(dot.Remaining<=0)dots.Remove(dot);
        }
        foreach(var area in areas.ToArray())
        {
            if(CombatSimulation.Time<area.Next)continue;
            var targets=AbilityRuntime.Targets(area.Center,area.Targets>0?area.Range:area.Radius,area.Targets>0);
            foreach(var enemy in area.Targets>0?targets.Take(area.Targets):targets){float actual=AbilityRuntime.HitMeasured(area.Spell,enemy,area.Damage,area.Center);ClassCombat.Event(4,area.Spell,enemy,actual);}
            area.Remaining--;area.Next+=area.Interval;
            if(area.Remaining<=0){areas.Remove(area);if(area.Totem)ClassPassives.TotemExpired(area.Center);}
        }
        foreach(var hot in hots.ToArray())
        {
            float dt=Mathf.Min(hot.Remaining,CombatSimulation.Time-hot.Last);if(dt<=0)continue;
            ClassCombat.Heal(hot.Rate*dt,hot.Id);hot.Remaining-=dt;hot.Last=CombatSimulation.Time;if(hot.Remaining<=0)hots.Remove(hot);
        }
        foreach(var pair in controls.ToArray())
        {var c=pair.Value;float now=CombatSimulation.Time;if(!Alive(c.Enemy)||c.Enemy.id!=c.Id||Mathf.Max(c.SlowUntil,Mathf.Max(c.RootUntil,Mathf.Max(c.StunUntil,Mathf.Max(c.FearUntil,Mathf.Max(c.DebuffUntil,c.VulnerabilityUntil)))))<=now)controls.Remove(pair.Key);}
        foreach(var id in thorns.Where(p=>p.Value.Until<=CombatSimulation.Time).Select(p=>p.Key).ToArray())thorns.Remove(id);
    }
    internal static void ApplyTalentDot(Enemy enemy,LearnedSpell spell,float added,float duration,int ticks,string id)
    {
        if(!ClassCombat.GenericActive || !CombatSimulation.CanSimulate || !Alive(enemy) || spell==null || added<=0 || duration<=0 || string.IsNullOrEmpty(id))return;
        var existing=dots.FirstOrDefault(d=>d.Id==enemy.id && d.TalentId==id && d.Spell.Definition.SourceSpellId==spell.Definition.SourceSpellId);
        float total=added+(existing!=null?existing.Damage*existing.Remaining:0);if(existing!=null)dots.Remove(existing);
        int count=Math.Max(1,ticks);
        // Original allocator writes current sourceTime to +1C; the consumer
        // processes it when now>=nextTick, then adds interval (+18).
        dots.Add(new Dot{Spell=spell,Enemy=enemy,Id=enemy.id,Damage=total/count,Remaining=count,Interval=duration/count,Next=CombatSimulation.Time,TalentId=id});
    }
    internal static bool HasDot(Enemy enemy,string spellId)=>enemy!=null&&dots.Any(d=>d.Id==enemy.id&&d.Enemy.Pointer==enemy.Pointer&&(d.Spell.Definition.Id==spellId||d.TalentId==spellId));
    internal static void CopyDots(Enemy from,Enemy to,string[] spellIds)
    {
        if(from==null||!Alive(to))return;
        foreach(var dot in dots.Where(d=>d.Remaining>0&&d.Id==from.id&&(spellIds==null||spellIds.Length==0||spellIds.Contains(d.Spell.Definition.Id))).ToArray())
        {dots.RemoveAll(d=>d.Id==to.id&&d.Spell.Definition.Id==dot.Spell.Definition.Id);dots.Add(new Dot{Spell=dot.Spell,Enemy=to,Id=to.id,Damage=dot.Damage,Remaining=dot.Remaining,Interval=dot.Interval,Next=dot.Next,CastToken=dot.CastToken,TalentId=dot.TalentId});}
    }
    internal static void Clear(){dots.Clear();areas.Clear();hots.Clear();controls.Clear();thorns.Clear();}
    internal static object Snapshot()=>new{Dots=dots.Select(d=>new{Spell=d.Spell.Definition.Id,d.TalentId,Enemy=d.Id,d.Damage,d.Remaining,d.Interval,NextIn=Mathf.Max(0,d.Next-CombatSimulation.Time)}).ToArray(),Areas=areas.Select(a=>new{Spell=a.Spell.Definition.Id,Center=new[]{a.Center.x,a.Center.y,a.Center.z},a.Radius,a.Range,a.Damage,a.Interval,a.Remaining,a.Targets,a.Totem,NextIn=Mathf.Max(0,a.Next-CombatSimulation.Time)}).ToArray(),Controls=controls.Values.Select(c=>new{Enemy=c.Id,c.SlowFactor,c.DamageReduction,c.Vulnerability,VulnerabilityFor=Mathf.Max(0,c.VulnerabilityUntil-CombatSimulation.Time),SlowFor=Mathf.Max(0,c.SlowUntil-CombatSimulation.Time),RootFor=Mathf.Max(0,c.RootUntil-CombatSimulation.Time),StunFor=Mathf.Max(0,c.StunUntil-CombatSimulation.Time),FearFor=Mathf.Max(0,c.FearUntil-CombatSimulation.Time),DebuffFor=Mathf.Max(0,c.DebuffUntil-CombatSimulation.Time)}).ToArray()};
}
[HarmonyPatch(typeof(Actors.Enemies.EnemyMovementRb),"GetSpeed")]
internal static class SourceControlSpeedPatch
{static void Postfix(Actors.Enemies.EnemyMovementRb __instance,ref float __result)=>__result*=ClassSpellBehaviors.SpeedFactor(__instance.enemy);}
[HarmonyPatch(typeof(Actors.Enemies.EnemyMovementRb),nameof(Actors.Enemies.EnemyMovementRb.MyFixedUpdate))]
internal static class SourceFearMovementPatch
{static bool Prefix(Actors.Enemies.EnemyMovementRb __instance)=>!ClassSpellBehaviors.Wander(__instance);}

[HarmonyPatch(typeof(Assets.Scripts.Game.Combat.EnemySpecialAttacks.SpecialAttackController),"UseSpecialAttack")]
internal static class SourceControlSpecialAttackPatch
{static bool Prefix(Assets.Scripts.Game.Combat.EnemySpecialAttacks.SpecialAttackController __instance)=>!ClassCombat.GenericActive||!ClassSpellBehaviors.IsHarmless(__instance.enemy);}
