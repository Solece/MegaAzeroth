using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Game.Combat.EnemySpecialAttacks;
using Assets.Scripts.Game.Combat.EnemyDebuffs;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Menu.Shop;
using Actors.Enemies;
using HarmonyLib;
using UnityEngine;

namespace AzerothAbilities;

// Source formulas are recovered from the installed Azeroth executable. Unity targeting,
// vertical placement and native combat entry points are the cross-engine adapter.
public static class MageSpellBehaviors
{
    public static Action<string,string,Vector3,float,float> VisualEvent;
    public static Func<LearnedSpell,float> RadiusMultiplier;
    public static Func<LearnedSpell,float> BarrierMultiplier;
    public static float RadiusScale(LearnedSpell spell)=>Mathf.Max(.1f,RadiusMultiplier?.Invoke(spell)??1);
    public static float BarrierRemaining {get;private set;}
    private static float barrierExpires;
    private static float nextPolymorph;
    private static bool popping;
    private static readonly List<GroundArea> Areas=new();
    private static readonly Dictionary<IntPtr,Control> Controls=new();
    private sealed class GroundArea
    {
        internal LearnedSpell Spell;internal Vector3 Center;internal float Radius,Damage,Next,Interval;
        internal int Remaining;
    }
    private sealed class Control
    {
        internal Enemy Enemy;internal uint Id;internal float SlowUntil,RootUntil,StunUntil,SheepUntil,SlowFactor=1;
        internal bool WasSheep;
        internal Vector3 Wander;
        internal float ChangeDirection;
    }
    private static void Fx(string id,string stage,Vector3 pos,float scale=1,float duration=.5f)
    {
        if(Plugin.Current?.SpellVisuals.Value==true && AbilityBehaviour.RenderVisuals)
            VisualEvent?.Invoke(id,stage,pos,scale,duration);
    }
    public static void Clear()
    {
        Controls.Clear();Areas.Clear();BarrierRemaining=0;barrierExpires=nextPolymorph=0;popping=false;
    }
    private static bool Alive(Enemy e)=>e!=null && !e.IsDeadOrDyingNextFrame();
    private static Control Get(Enemy enemy)
    {
        if(!Controls.TryGetValue(enemy.Pointer,out var c) || c.Id!=enemy.id)
            Controls[enemy.Pointer]=c=new Control{Enemy=enemy,Id=enemy.id};
        return c;
    }
    public static bool IsSheep(Enemy enemy)=>enemy!=null && Controls.TryGetValue(enemy.Pointer,out var c)
        && c.Id==enemy.id && c.SheepUntil>Time.time;
    public static bool IsControlled(Enemy enemy)
    {
        if(enemy==null)return false;
        if(Controls.TryGetValue(enemy.Pointer,out var c) && c.Id==enemy.id
            && (c.SlowUntil>Time.time || c.RootUntil>Time.time || c.StunUntil>Time.time || c.SheepUntil>Time.time))return true;
        return enemy.HasDebuff(EDebuff.Stun) || enemy.HasDebuff(EDebuff.Freeze) || enemy.HasDebuff(EDebuff.Charm);
    }
    public static void ApplyRoot(Enemy enemy,float duration,bool stun)
    {
        if(!AbilityRuntime.Active || !Alive(enemy) || duration<=0)return;
        if(enemy.IsBoss())duration*=.25f;
        var c=Get(enemy);
        if(stun)
        {
            c.StunUntil=Mathf.Max(c.StunUntil,Time.time+duration);
            enemy.AddDebuff(EDebuff.Stun,new DamageContainer(0,"Azeroth:MageTalent"),duration,1);
        }
        else c.RootUntil=Mathf.Max(c.RootUntil,Time.time+duration);
    }
    internal static float SpeedFactor(Enemy enemy)
    {
        if(!AbilityRuntime.Active || enemy==null || !Controls.TryGetValue(enemy.Pointer,out var c) || c.Id!=enemy.id)return 1;
        if(c.RootUntil>Time.time || c.StunUntil>Time.time || c.SheepUntil>Time.time)return 0;
        return c.SlowUntil>Time.time?c.SlowFactor:1;
    }
    public static bool TryCast(LearnedSpell spell,MyPlayer player,out bool casted)
    {
        casted=false;var d=spell.Definition;
        switch(d.Kind)
        {
            case "passive":return true;
            case "barrier":
                if(BarrierRemaining>0)return true;
                var health=player.inventory.playerHealth;
                if(health==null)return true;
                // Native1459612A4: maxHP * statusValue *1.4 *sqrt(rankDamageFactor).
                // Native book damage is separate from the original rank factor.
                BarrierRemaining=health.maxHp*d.StatusValue*AbilityRuntime.Catalog.DamageScale*Mathf.Sqrt(spell.DamageFactor);
                BarrierRemaining*=Mathf.Max(0,BarrierMultiplier?.Invoke(spell)??1);
                barrierExpires=Time.time+d.Duration*AbilityRuntime.Catalog.DurationScale*spell.DurationFactor;
                Fx(d.Id,"aura",player.transform.position,1,Mathf.Max(.1f,barrierExpires-Time.time));
                casted=true;return true;
            case "cone":
                var origin=player.transform.position;
                var reach=d.Range*spell.RadiusFactor*RadiusScale(spell)*Mathf.Max(.1f,AbilityRuntime.Stat(EStat.SizeMultiplier));
                var direction=player.transform.forward;direction.y=0;direction.Normalize();
                var hits=AbilityRuntime.Targets(origin,reach,false).Where(e=>
                {var delta=e.transform.position-origin;delta.y=0;return delta.sqrMagnitude>.0001f && Vector3.Dot(delta.normalized,direction)>.5f;}).ToArray();
                if(hits.Length==0)return true;
                foreach(var e in hits)AbilityRuntime.Hit(spell,e,AbilityRuntime.Damage(spell),origin);
                Fx(d.Id,"cast",origin,reach,.7f);casted=true;return true;
            case "groundAoe":
                var candidates=AbilityRuntime.Targets(player.transform.position,d.Range,false);
                if(candidates.Count==0)return true;
                var radius=d.Radius*spell.RadiusFactor*RadiusScale(spell)*Mathf.Max(.1f,AbilityRuntime.Stat(EStat.SizeMultiplier));
                // Original chooses among the first24 nearby targets to maximize coverage.
                // Retain the same bounded search in the Unity arena.
                var center=candidates[0].transform.position;var best=-1;
                foreach(var candidate in candidates.Take(24))
                {
                    var count=0;var at=candidate.transform.position;
                    foreach(var e in candidates)if((e.transform.position-at).sqrMagnitude<=radius*radius)count++;
                    if(count>best){best=count;center=at;}
                }
                var duration=d.Duration*AbilityRuntime.Catalog.DurationScale*spell.DurationFactor;
                var totalTicks=Mathf.Max(1,(int)MathF.Round(duration/Mathf.Max(.01f,d.Duration)*Mathf.Max(1,d.Ticks),MidpointRounding.AwayFromZero));
                Areas.Add(new GroundArea{Spell=spell,Center=center,Radius=radius,Damage=AbilityRuntime.Damage(spell)/Mathf.Max(1,d.Ticks),
                    Remaining=totalTicks,Next=Time.time+.25f,Interval=duration/totalTicks});
                Fx(d.Id,"area",center,radius,duration);casted=true;return true;
            default:return false;
        }
    }
    public static void Tick()
    {
        if(!AbilityRuntime.Active)return;
        if(BarrierRemaining>0 && Time.time>=barrierExpires)
        {BarrierRemaining=0;}
        // Process a snapshot: hit procs may create more statuses and visual callbacks.
        foreach(var pair in Controls.ToArray())
        {
            var c=pair.Value;
            if(!Alive(c.Enemy) || c.Enemy.id!=c.Id){Controls.Remove(pair.Key);continue;}
            if(c.WasSheep && c.SheepUntil<=Time.time)
            {c.WasSheep=false;}
            if(c.SlowUntil<=Time.time && c.RootUntil<=Time.time && c.StunUntil<=Time.time && c.SheepUntil<=Time.time)Controls.Remove(pair.Key);
        }
        for(var i=Areas.Count-1;i>=0;i--)
        {
            var area=Areas[i];
            if(Time.time<area.Next)continue;
            var targets=AbilityRuntime.Targets(area.Center,area.Radius,false);
            foreach(var e in targets)AbilityRuntime.Hit(area.Spell,e,area.Damage,area.Center);
            area.Remaining--;area.Next+=area.Interval;
            if(area.Remaining<=0)Areas.RemoveAt(i);
        }
    }
    public static void BeforeHit(LearnedSpell spell,Enemy target)
    {
        if(popping || !IsSheep(target))return;
        var passive=AbilityRuntime.Spells.FirstOrDefault(s=>s.Definition.Id=="polymorph");
        if(passive==null)return;
        var c=Get(target);c.SheepUntil=0;c.WasSheep=false;
        var center=target.transform.position;
        // Original15% maxHP, capped by the rank-scaled115*1.4 damage budget.
        var damage=Mathf.Min(target.maxHp*.15f,AbilityRuntime.Damage(passive));
        var radius=passive.Definition.Radius*passive.RadiusFactor*RadiusScale(passive)*Mathf.Max(.1f,AbilityRuntime.Stat(EStat.SizeMultiplier));
        popping=true;
        try
        {
            foreach(var enemy in AbilityRuntime.Targets(center,radius,false))AbilityRuntime.Hit(passive,enemy,damage,center);
            Fx("polymorph","impact",center,radius,.7f);
        }
        finally{popping=false;}
    }
    public static void AfterHit(LearnedSpell spell,Enemy target,DamageContainer dc)
    {
        if(!Alive(target))return;
        var d=spell.Definition;
        if(d.StatusKind==1 && d.StatusDuration>0)
        {
            var c=Get(target);var until=Time.time+d.StatusDuration;
            if(c.SlowUntil<=Time.time)c.SlowFactor=1;
            c.SlowFactor=Mathf.Min(c.SlowFactor,Mathf.Max(0,1-d.StatusValue));
            c.SlowUntil=Mathf.Max(c.SlowUntil,until);
        }
        else if(d.StatusKind==2 && d.StatusValue>0)
        {
            // Native root status stores duration in the value field. Source bosses are
            // rooted for a quarter of the ordinary duration, without changing their AI.
            ApplyRoot(target,d.StatusValue,false);
        }
        if(popping || d.Id=="polymorph" || target.IsBoss() || target.IsStageBoss() || target.IsFinalBoss() || IsSheep(target))return;
        var passive=AbilityRuntime.Spells.FirstOrDefault(s=>s.Definition.Id=="polymorph");
        if(passive==null || Time.time<nextPolymorph || Controls.Values.Count(c=>c.SheepUntil>Time.time)>=3 || UnityEngine.Random.value>=.12f)return;
        var sheep=Get(target);sheep.SheepUntil=Time.time+10;sheep.WasSheep=true;
        nextPolymorph=Time.time+Mathf.Max(3,passive.Definition.BaseCooldown*AbilityRuntime.Catalog.CooldownScale*passive.CooldownFactor)
            /Mathf.Max(.1f,AbilityRuntime.Stat(EStat.AttackSpeed));
        Fx("polymorph","aura",target.transform.position,1,10);
        Plugin.Current?.Log.LogInfo($"Azeroth Polymorph proc: enemy={target.id},duration=10,next={nextPolymorph-Time.time:F3}");
    }
    internal static bool Wander(EnemyMovementRb movement)
    {
        var enemy=movement.enemy;if(!AbilityRuntime.Active || !IsSheep(enemy))return false;
        var c=Get(enemy);
        if(Time.time>=c.ChangeDirection)
        {var angle=(enemy.id*.618034f+Mathf.Floor(Time.time*.5f))*Mathf.PI*2;c.Wander=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*.8f;c.ChangeDirection=Time.time+2;}
        if(movement.rb!=null)
        {var velocity=movement.rb.velocity;movement.rb.velocity=new Vector3(c.Wander.x,velocity.y,c.Wander.z);}
        return true;
    }
    internal static void Absorb(DamageContainer dc)
    {
        if(!AbilityRuntime.Active || BarrierRemaining<=0 || dc==null || dc.damage<=0 || Time.time>=barrierExpires)return;
        var absorbed=Mathf.Min(BarrierRemaining,dc.damage);dc.damage-=absorbed;BarrierRemaining-=absorbed;
    }
}

[HarmonyPatch(typeof(EnemyMovementRb),"GetSpeed")]
internal static class AzerothSlowPatch
{
    private static void Postfix(EnemyMovementRb __instance,ref float __result)=>__result*=MageSpellBehaviors.SpeedFactor(__instance.enemy);
}
[HarmonyPatch(typeof(EnemyMovementRb),nameof(EnemyMovementRb.MyFixedUpdate))]
internal static class AzerothSheepMovementPatch
{
    private static bool Prefix(EnemyMovementRb __instance)=>!MageSpellBehaviors.Wander(__instance);
}
[HarmonyPatch(typeof(PlayerHealth),nameof(PlayerHealth.DamagePlayer))]
internal static class AzerothSheepContactPatch
{
    private static bool Prefix(Enemy enemy)=>!AbilityRuntime.Active || !MageSpellBehaviors.IsSheep(enemy);
}
[HarmonyPatch(typeof(SpecialAttackController),"UseSpecialAttack")]
internal static class AzerothSheepSpecialAttackPatch
{
    private static bool Prefix(SpecialAttackController __instance)=>!AbilityRuntime.Active || !MageSpellBehaviors.IsSheep(__instance.enemy);
}
[HarmonyPatch(typeof(PlayerHealth),"Damage")]
internal static class AzerothBarrierPatch
{
    private static void Prefix(DamageContainer dc)=>MageSpellBehaviors.Absorb(dc);
}
