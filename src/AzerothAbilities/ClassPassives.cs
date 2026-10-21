using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using HarmonyLib;
using UnityEngine;

namespace AzerothAbilities;

internal static class ClassPassives
{
    sealed class Sheep{internal Enemy Enemy;internal uint Id;internal float Until;}
    sealed class Mark{internal Enemy Enemy;internal uint Id;internal float Next;}
    sealed class Well{internal LearnedSpell Spell;internal Vector3 Center;internal float Until,Next;}
    static readonly Dictionary<uint,Sheep> sheep=new();static readonly Dictionary<uint,Mark> faerie=new();
    static readonly List<Well> wells=new();static readonly Queue<(Vector3 Position,int Depth)> redemption=new();
    static readonly HashSet<(IntPtr,uint)> killed=new();
    static Enemy hunter;static uint hunterId;static float nextHunter,nextHunterBurst,redemptionAt,nextPoly,nextSoulShard,nextBloodrage,nextSeal,nextFlame,lastHit,bloodTokens,sealTokens,rage,soulShards,combo,lastTick;
    static int depth,redemptionDepth,procs;
    static LearnedSpell Spell(string id)=>AbilityRuntime.Spells.FirstOrDefault(s=>s.Definition.Id==id);
    static bool Alive(Enemy e)=>ClassSpellBehaviors.Alive(e);
    static float RankScale(LearnedSpell spell)=>Mathf.Sqrt(Mathf.Max(0,spell.DamageFactor*(1+ClassCombat.SpellBonus(spell,0))));
    static void Burst(LearnedSpell spell,Vector3 at,float damage,int chainDepth=0)
    {
        if(spell==null || depth>1)return;
        int saved=redemptionDepth;redemptionDepth=chainDepth;depth++;
        try{foreach(var target in AbilityRuntime.Targets(at,ClassSpellBehaviors.Radius(spell),false))AbilityRuntime.Hit(spell,target,damage,at);ClassSpellBehaviors.Fx(spell,"impact",at,spell.RadiusFactor*TomeCompatibility.Size,.7f);procs++;}
        finally{depth--;redemptionDepth=saved;}
    }
    internal static bool IsSheep(Enemy enemy)=>enemy!=null&&sheep.TryGetValue(enemy.id,out var c)&&c.Enemy.Pointer==enemy.Pointer&&c.Until>CombatSimulation.Time;
    internal static void BeforeHit(LearnedSpell spell,Enemy enemy)
    {
        if(depth>0||!IsSheep(enemy))return;var passive=Spell("polymorph");if(passive==null)return;
        sheep.Remove(enemy.id);Burst(passive,enemy.transform.position,Mathf.Min(enemy.maxHp*.15f,AbilityRuntime.Damage(passive)));
    }
    internal static void AfterHit(LearnedSpell spell,Enemy enemy,DamageContainer dc,float actual)
    {
        if(depth>0 || spell.Definition.SourceKind==2)return;
        float now=CombatSimulation.Time;lastHit=now;
        var blood=Spell("bloodrage");
        if(blood!=null)
        {
            float spent=Mathf.Min(2,bloodTokens);bloodTokens-=spent;rage=Mathf.Min(100,rage+spent);
            if(rage>=100 && now>=nextBloodrage){rage=0;nextBloodrage=now+AbilityRuntime.Cooldown(blood);Burst(blood,MyPlayer.Instance.transform.position,AbilityRuntime.Damage(blood));}
        }
        var seal=Spell("seal_fate");
        if(seal!=null)
        {
            int budget=Math.Min(dc.crit?2:1,(int)sealTokens);sealTokens-=budget;combo=Mathf.Min(5,combo+budget);
            if(combo>=5 && now>=nextSeal)
            {
                var targets=TargetAcquisition.SkillTargets(MyPlayer.Instance.transform.position,seal.Definition.Range,true)
                    .OrderByDescending(e=>e.IsBoss()||e.IsElite()).ThenBy(e=>e.hp<e.maxHp*.3f?e.hp/e.maxHp:1).Take(Math.Min(3,ClassSpellBehaviors.Quantity(seal))).ToArray();
                if(targets.Length>0){combo=0;nextSeal=now+AbilityRuntime.Cooldown(seal);depth++;try{foreach(var target in targets)AbilityRuntime.Hit(seal,target,AbilityRuntime.Damage(seal)*(target.hp<target.maxHp*.3f?2:1),MyPlayer.Instance.transform.position);procs++;}finally{depth--;}}
            }
        }
        var polymorph=Spell("polymorph");
        if(polymorph!=null && spell.Definition.SourceKind==0 && Alive(enemy) && !enemy.IsBoss() && !enemy.IsStageBoss() && !enemy.IsFinalBoss() && !IsSheep(enemy) && now>=nextPoly && sheep.Count<3 && UnityEngine.Random.value<.12f)
        {sheep[enemy.id]=new Sheep{Enemy=enemy,Id=enemy.id,Until=now+10*TomeCompatibility.EffectDuration};nextPoly=now+AbilityRuntime.Cooldown(polymorph);ClassSpellBehaviors.Fx(polymorph,"aura",enemy.transform.position,1,10*TomeCompatibility.EffectDuration);procs++;}
        var fairy=Spell("faerie_fire");
        if(fairy!=null && faerie.TryGetValue(enemy.id,out var mark)&&mark.Enemy.Pointer==enemy.Pointer&&now>=mark.Next)
        {
            mark.Next=now+.25f;
            float damage=Mathf.Min(actual*Mathf.Min(1.2f,.8f*RankScale(fairy)),AbilityRuntime.Damage(fairy));
            depth++;
            try{foreach(var target in AbilityRuntime.Targets(enemy.transform.position,ClassSpellBehaviors.Radius(fairy),false).Where(e=>e.id!=enemy.id).Take(2))AbilityRuntime.Hit(fairy,target,damage,enemy.transform.position);procs++;}
            finally{depth--;}
        }
    }
    internal static void OnKill(Enemy enemy)
    {
        if(!killed.Add((enemy.Pointer,enemy.id)))return;
        float now=CombatSimulation.Time;
        var soul=Spell("soul_fire");
        if(soul!=null && now>=nextSoulShard){soulShards=Mathf.Min(5,soulShards+1);nextSoulShard=now+AbilityRuntime.Cooldown(soul)/5;}
        var holy=Spell("redemption");
        if(holy!=null && redemptionDepth<2 && redemption.Count<4)
        {
            float baseCd=holy.Definition.BaseCooldown*(AbilityRuntime.Catalog?.CooldownScale??1.4f);
            float chance=Mathf.Min(.2f,.1f*baseCd/Mathf.Max(.25f,AbilityRuntime.Cooldown(holy)));
            if(UnityEngine.Random.value<chance)redemption.Enqueue((enemy.transform.position,redemptionDepth+1));
        }
        var mark=Spell("hunters_mark");
        if(mark!=null && hunter!=null && enemy.id==hunterId)
        {if(now>=nextHunterBurst){Burst(mark,enemy.transform.position,AbilityRuntime.Damage(mark));nextHunterBurst=now+AbilityRuntime.Cooldown(mark);}hunter=null;hunterId=0;nextHunter=now+1;}
        if(faerie.TryGetValue(enemy.id,out var previousMark))
        {
            var fairy=Spell("faerie_fire");
            if(fairy!=null){var next=AbilityRuntime.Targets(enemy.transform.position,ClassSpellBehaviors.Radius(fairy),false).FirstOrDefault(e=>e.id!=enemy.id&&!faerie.ContainsKey(e.id));if(next!=null)faerie[next.id]=new Mark{Enemy=next,Id=next.id,Next=previousMark.Next};}
        }
        sheep.Remove(enemy.id);
    }
    internal static float TargetDamageFactor(Enemy enemy)
    {var spell=Spell("hunters_mark");return spell!=null && enemy!=null && hunter!=null && enemy.id==hunterId && enemy.Pointer==hunter.Pointer?1+Mathf.Min(.4f,.2f*RankScale(spell)):1;}
    internal static void TotemExpired(Vector3 at)
    {var spell=Spell("call_of_flame");if(spell==null||CombatSimulation.Time<nextFlame)return;nextFlame=CombatSimulation.Time+AbilityRuntime.Cooldown(spell);Burst(spell,at,AbilityRuntime.Damage(spell));}
    internal static void Tick()
    {
        float now=CombatSimulation.Time,dt=Mathf.Max(0,now-lastTick);lastTick=now;
        var blood=Spell("bloodrage");if(blood!=null){float rate=100/Mathf.Max(.01f,AbilityRuntime.Cooldown(blood));bloodTokens=Mathf.Min(Mathf.Max(2,rate*.5f),bloodTokens+dt*rate);if(now-lastHit>3)rage=Mathf.Max(0,rage-15*dt);}
        var seal=Spell("seal_fate");if(seal!=null){float rate=5/Mathf.Max(.01f,AbilityRuntime.Cooldown(seal));sealTokens=Mathf.Min(Mathf.Max(2,rate*.5f),sealTokens+rate*dt);}
        foreach(var id in sheep.Where(p=>!Alive(p.Value.Enemy)||p.Value.Until<=now).Select(p=>p.Key).ToArray())sheep.Remove(id);
        foreach(var id in faerie.Where(p=>!Alive(p.Value.Enemy)).Select(p=>p.Key).ToArray())faerie.Remove(id);
        var fairy=Spell("faerie_fire");
        if(fairy!=null)
        {
            int wanted=ClassSpellBehaviors.Quantity(fairy);
            foreach(var enemy in TargetAcquisition.SkillTargets(MyPlayer.Instance.transform.position,fairy.Definition.Range,true).Where(e=>!faerie.ContainsKey(e.id)).Take(Math.Max(0,wanted-faerie.Count)))
            {faerie[enemy.id]=new Mark{Enemy=enemy,Id=enemy.id};ClassSpellBehaviors.Fx(fairy,"aura",enemy.transform.position,1,1);}
        }
        var mark=Spell("hunters_mark");
        if(mark!=null && (hunter==null||!Alive(hunter)) && now>=nextHunter)
        {hunter=TargetAcquisition.SkillTargets(MyPlayer.Instance.transform.position,mark.Definition.Range,true).OrderByDescending(e=>e.maxHp).FirstOrDefault();hunterId=hunter?.id??0;if(hunter!=null)ClassSpellBehaviors.Fx(mark,"aura",hunter.transform.position,1,1);}
        var soul=Spell("soul_fire");
        if(soul!=null && soulShards>=5)
        {
            var targets=TargetAcquisition.SkillTargets(MyPlayer.Instance.transform.position,soul.Definition.Range,true).OrderByDescending(e=>e.maxHp).Take(ClassSpellBehaviors.Quantity(soul)).ToArray();
            if(targets.Length>0){soulShards=0;depth++;try{foreach(var enemy in targets)AbilityRuntime.Hit(soul,enemy,AbilityRuntime.Damage(soul),MyPlayer.Instance.transform.position);procs++;}finally{depth--;}}
        }
        var holy=Spell("redemption");
        if(holy!=null && redemption.Count>0 && now>=redemptionAt){var burst=redemption.Dequeue();redemptionAt=now+.5f;Burst(holy,burst.Position,AbilityRuntime.Damage(holy),burst.Depth);}
        var light=Spell("lightwell");
        if(light!=null && now>=light.NextCast && TargetAcquisition.SkillTargets(MyPlayer.Instance.transform.position,light.Definition.Range,false).Count>0)
        {float duration=ClassSpellBehaviors.Duration(light);wells.Add(new Well{Spell=light,Center=MyPlayer.Instance.transform.position,Until=now+duration,Next=now});light.NextCast=now+AbilityRuntime.Cooldown(light);ClassSpellBehaviors.Fx(light,"aura",MyPlayer.Instance.transform.position,1,duration);}
        foreach(var well in wells.ToArray())
        {
            if(now>=well.Until){wells.Remove(well);continue;}if(now<well.Next)continue;well.Next+=1;
            foreach(var enemy in AbilityRuntime.Targets(well.Center,well.Spell.Definition.Range,true).Take(ClassSpellBehaviors.Quantity(well.Spell)))AbilityRuntime.Hit(well.Spell,enemy,AbilityRuntime.Damage(well.Spell),well.Center);
        }
    }
    internal static void Clear(){sheep.Clear();faerie.Clear();wells.Clear();redemption.Clear();killed.Clear();hunter=null;hunterId=0;nextHunter=nextHunterBurst=redemptionAt=nextPoly=nextSoulShard=nextBloodrage=nextSeal=nextFlame=lastHit=0;bloodTokens=sealTokens=rage=soulShards=combo=0;lastTick=CombatSimulation.Time;depth=redemptionDepth=procs=0;}
    internal static float CooldownRemaining(LearnedSpell spell)
    {
        float next=spell.Definition.Id switch{"bloodrage"=>nextBloodrage,"redemption"=>redemption.Count>0?redemptionAt:0,"hunters_mark"=>nextHunterBurst,"seal_fate"=>nextSeal,"call_of_flame"=>nextFlame,"polymorph"=>nextPoly,"soul_fire"=>nextSoulShard,"faerie_fire"=>faerie.Count>0?faerie.Values.Max(m=>m.Next):0,_=>spell.NextCast};
        return Mathf.Max(0,next-CombatSimulation.Time);
    }
    internal static float CooldownTotal(LearnedSpell spell)=>spell.Definition.Id switch{"redemption"=>.5f,"faerie_fire"=>.25f,"soul_fire"=>AbilityRuntime.Cooldown(spell)/5,_=>AbilityRuntime.Cooldown(spell)};
    internal static object Snapshot()=>new{Sheep=sheep.Keys.ToArray(),FaerieMarks=faerie.Keys.ToArray(),HunterMark=hunterId,Lightwells=wells.Count,RedemptionQueue=redemption.Count,Rage=rage,SoulShards=soulShards,Combo=combo,Procs=procs,Cooldowns=AbilityRuntime.Spells.Where(s=>s.Definition.Kind=="passive").Select(s=>new{Spell=s.Definition.Id,Remaining=CooldownRemaining(s),Total=CooldownTotal(s)}).ToArray()};
}
[HarmonyPatch(typeof(Enemy),nameof(Enemy.EnemyDied),new[]{typeof(DamageContainer)})]
internal static class SourcePassiveDeathPatch
{
    static void Prefix(Enemy __instance,DamageContainer __0)
    {if(ClassCombat.GenericActive && __0?.damageSource?.StartsWith("Azeroth",StringComparison.Ordinal)==true)ClassCombat.OnKilled(__instance);}
}
[HarmonyPatch(typeof(Enemy),"Damage",new[]{typeof(DamageContainer)})]
internal static class SourceHuntersMarkDamagePatch
{
    static void Prefix(Enemy __instance,DamageContainer __0)
    {if(ClassCombat.GenericActive && __0!=null)__0.damage*=ClassPassives.TargetDamageFactor(__instance)*ClassSpellBehaviors.Vulnerability(__instance);}
}
