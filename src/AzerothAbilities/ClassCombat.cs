using System.Reflection;
using Assets.Scripts.Menu.Shop;
using System.Text.Json;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Game.Combat;
using HarmonyLib;
using UnityEngine;

namespace AzerothAbilities;

// Shared ABI: source class/racial/passive/buff stats are separate from talents.
// Every duration uses CombatSimulation, including shields, healing and companions.
public static class ClassCombat
{
    sealed class TimedStat {internal int Stat;internal float Value,Until;}
    sealed class Shield {internal float Remaining,Peak,Until;}
    static readonly Dictionary<string,TimedStat> stats=new();
    static readonly Dictionary<string,Shield> shields=new();
    static Type talent;static Func<int,float> sourceStat;static Func<LearnedSpell,int,float> spellModifier;
    static readonly Dictionary<string,MethodInfo> talentMethods=new();
    static float invulnerableUntil,nextRegen;static LearnedSpell racial;static bool inContact;
    static object lastHeal,lastIncoming;
    public static string ClassId=>AbilityRuntime.Active?AbilityRuntime.Catalog.ClassId:"";
    public static bool GenericActive=>AbilityRuntime.Active && !ReferenceEquals(AbilityRuntime.Catalog,Plugin.Current?.LegacyMage);
    public static float CurrentHealth=>MyPlayer.Instance?.inventory?.playerHealth?.hp??0;
    public static float MaximumHealth=>MyPlayer.Instance?.inventory?.playerHealth?.maxHp??0;
    public static float ShieldRemaining=>shields.Values.Sum(s=>s.Remaining);
    public static string LastHealJson=>JsonSerializer.Serialize(lastHeal);
    public static string LastIncomingJson=>JsonSerializer.Serialize(lastIncoming);
    public static float UncappedDamageReduction=>(AbilityRuntime.Catalog?.SourceClass?.SourceBaseDamageReduction??0)+StatBonus(5)+TalentStat(5);
    public static float EffectiveDamageReduction=>Mathf.Clamp(UncappedDamageReduction,0,Mathf.Clamp01(.60f+StatBonus(27)+TalentStat(27)));
    internal static SourceRace Race=>ClassCatalogs.Races.TryGetValue(ClassCatalogs.ActiveRaceId,out var race)?race:null;
    static void BindTalent()
    {
        if(talent!=null)return;
        talent=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.TalentRuntime",false)).FirstOrDefault(t=>t!=null);
        if(talent==null)return;
        sourceStat=talent.GetMethod("SourceStat",BindingFlags.Public|BindingFlags.Static)?.CreateDelegate<Func<int,float>>();
        spellModifier=talent.GetMethod("SourceSpellModifier",BindingFlags.Public|BindingFlags.Static)?.CreateDelegate<Func<LearnedSpell,int,float>>();
    }
    internal static bool ChainNoFalloff{get{BindTalent();return (bool)(talent?.GetProperty("ChainNoFalloff",BindingFlags.Public|BindingFlags.Static)?.GetValue(null)??false);}}
    internal static float TalentStat(int stat){BindTalent();return sourceStat?.Invoke(stat)??0;}
    internal static float SpellBonus(LearnedSpell spell,int modifier){BindTalent();return spellModifier?.Invoke(spell,modifier)??0;}
    static void Notify(string name,params object[] arguments)
    {
        BindTalent();if(talent==null)return;
        if(!talentMethods.TryGetValue(name,out var method))talentMethods[name]=method=talent.GetMethod(name,BindingFlags.Public|BindingFlags.Static);
        method?.Invoke(null,arguments);
    }
    internal static void Event(int id,LearnedSpell spell,Enemy enemy,float value)=>Notify("NotifySourceEvent",id,spell,enemy,value);
    public static float StatBonus(int sourceStat)
    {
        int racialKind=sourceStat switch{0=>1,1=>2,2=>0,3=>4,5=>5,6=>6,7=>3,12=>10,15=>11,_=>-1};
        float value=racialKind>=0?Race?.Bonus(racialKind)??0:0;
        foreach(var stat in stats.Values)if(stat.Stat==sourceStat && stat.Until>CombatSimulation.Time)value+=stat.Value;
        return value;
    }
    public static void ApplyTimedStat(int sourceStat,float value,float duration,string id)
    {
        if(!GenericActive || !float.IsFinite(value) || duration<=0 || string.IsNullOrEmpty(id))return;
        stats[id]=new TimedStat{Stat=sourceStat,Value=value,Until=CombatSimulation.Time+duration};RefreshHealthCache(sourceStat);
    }
    static void RefreshHealthCache(int sourceStat)
    {
        if(sourceStat is not (2 or 6))return;
        var health=MyPlayer.Instance?.inventory?.playerHealth;if(health==null)return;
        // Native health caches max HP and regeneration. Refresh through its own
        // stat-change method when a source timed bonus starts or expires.
        typeof(PlayerHealth).GetMethod("OnStatUpdate",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(health,new object[]{sourceStat==2?EStat.MaxHealth:EStat.HealthRegen});
    }
    public static void ApplyShield(float amount,float duration,string id)
    {
        if(!GenericActive || amount<=0 || !float.IsFinite(amount) || string.IsNullOrEmpty(id))return;
        shields[id]=new Shield{Remaining=amount,Peak=amount,Until=duration>0?CombatSimulation.Time+duration:float.PositiveInfinity};
    }
    public static void ApplyShieldCapped(float amount,float maxHpFraction,string id)
    {
        var health=MyPlayer.Instance?.inventory?.playerHealth;
        if(!GenericActive || health==null || amount<=0 || maxHpFraction<=0)return;
        float cap=health.maxHp*maxHpFraction;
        float before=shields.TryGetValue(id,out var shield)?shield.Remaining:0;
        ApplyShield(Mathf.Min(cap,before+amount),0,id);
    }
    public static float Heal(float amount,string id)
    {
        var health=MyPlayer.Instance?.inventory?.playerHealth;
        if(!GenericActive || !CombatSimulation.CanSimulate || health==null || amount<=0 || !float.IsFinite(amount))return 0;
        int before=health.hp;float carried=health.leftOverHeal;
        bool blocked=MyPlayer.Instance.inventory.statusEffects?.HasStatusEffect(EStatusEffect.Bleed)==true||before<=0;
        float requested=Mathf.Max(0,amount*AbilityRuntime.Stat(EStat.HealingMultiplier));
        health.Heal(amount,false);float actual=Mathf.Max(0,health.hp-before);
        float credited=0,accepted=0,roundingBound=0;
        if(!blocked)
        {
            // Native Heal stores fractional healing at PlayerHealth+0x50.
            // Integer HP delta alone would mislabel fractional regeneration as
            // overheal, and would miscredit a prior call's carried fraction.
            accepted=Mathf.Max(0,actual+health.leftOverHeal-carried);
            float capacity=Mathf.Max(0,health.maxHp-before-carried);
            credited=Mathf.Min(requested,Mathf.Min(capacity,accepted));
            float Ulp(float v)=>Mathf.Abs(MathF.BitIncrement(v)-v);
            roundingBound=4*(Ulp(actual)+Ulp(carried)+Ulp(health.leftOverHeal)+Ulp(requested));
            // Bound cancellation error in native fractional carry arithmetic.
            // Never erase actual excess when available HP capacity is smaller.
            if(capacity>=requested&&Mathf.Abs(accepted-requested)<=roundingBound)credited=requested;
            Notify("NotifyHeal",requested,credited,id??"");
        }
        lastHeal=new{Origin=id??"",InputAmount=amount,ScaledRequested=requested,BeforeHp=before,AfterHp=health.hp,CarryBefore=carried,CarryAfter=health.leftOverHeal,ActualHpGain=actual,RawAcceptedFraction=accepted,CarryRoundingBound=roundingBound,AcceptedFractionalHealing=credited,Overheal=blocked?0:Mathf.Max(0,requested-credited),NativeBleedOrDead=blocked};
        return actual;
    }
    public static void SetInvulnerable(float duration,string id)
    {
        if(GenericActive && duration>0)invulnerableUntil=Mathf.Max(invulnerableUntil,CombatSimulation.Time+duration);
    }
    public static void SetForm(int spellId)
    {
        if(!GenericActive)return;
        ClassCatalogs.Hero?.GetMethod("SetFormForRuntime",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{spellId});
    }
    public static void SpawnPet(int displayId,float duration,float damageFactor,float interval,int maxAdditional,string id)=>SourcePets.Spawn(displayId,duration,damageFactor,interval,maxAdditional,id);
    public static void ApplyTalentStatus(Enemy enemy,int kind,float value,float duration,string id)=>ClassSpellBehaviors.ApplyTalentStatus(enemy,kind,value,duration,id);
    public static void ApplyTalentDot(Enemy enemy,LearnedSpell spell,float addedTotalDamage,float duration,int ticks,string talentId)=>ClassSpellBehaviors.ApplyTalentDot(enemy,spell,addedTotalDamage,duration,ticks,talentId);
    public static bool HasDot(Enemy enemy,string spellId)=>ClassSpellBehaviors.HasDot(enemy,spellId);
    public static void CopyDots(Enemy from,Enemy to,string[] spellIds)=>ClassSpellBehaviors.CopyDots(from,to,spellIds);
    public static bool HasPet(string id)=>SourcePets.Has(id);
    public static int PetCount(string id)=>SourcePets.Count(id);
    public static void DespawnPet(string id)=>SourcePets.Despawn(id);
    internal static void Reset()
    {
        stats.Clear();shields.Clear();invulnerableUntil=0;nextRegen=CombatSimulation.Time;inContact=false;lastHeal=lastIncoming=null;SourcePets.Clear();ClassSpellBehaviors.Clear();ClassPassives.Clear();
        racial=Race!=null && ClassCatalogs.Auxiliary.TryGetValue(Race.RacialSpell,out var definition)?new LearnedSpell(definition):null;
        if(racial!=null)racial.NextCast=CombatSimulation.Time+AbilityRuntime.Cooldown(racial);
    }
    internal static void ClearScene()
    {
        shields.Clear();SourcePets.Clear();ClassSpellBehaviors.Clear();ClassPassives.Clear();
    }
    internal static void Tick()
    {
        if(!GenericActive || !CombatSimulation.CanSimulate)return;
        foreach(var key in stats.Where(p=>p.Value.Until<=CombatSimulation.Time).Select(p=>p.Key).ToArray()){int stat=stats[key].Stat;stats.Remove(key);RefreshHealthCache(stat);}
        foreach(var key in shields.Where(p=>p.Value.Until<=CombatSimulation.Time).Select(p=>p.Key).ToArray())shields.Remove(key);
        ClassSpellBehaviors.Tick();ClassPassives.Tick();SourcePets.Tick();
        if(racial!=null && CombatSimulation.Time>=racial.NextCast && ClassSpellBehaviors.TryCast(racial,MyPlayer.Instance))
            racial.NextCast=CombatSimulation.Time+AbilityRuntime.Cooldown(racial);
    }
    internal static float ConditionalGlobalDamage=>EffectiveDamageReduction*(StatBonus(25)+TalentStat(25))/10
        +Mathf.Max(0,UncappedDamageReduction-EffectiveDamageReduction)*(StatBonus(26)+TalentStat(26));
    internal static float DamageFactor(LearnedSpell spell,Enemy target)
    {
        float factor=1;
        if(spell.Definition.SourceKind==1)factor*=Mathf.Max(0,1+StatBonus(16)+TalentStat(16));
        if(spell.Definition.SourceKind==2)factor*=Mathf.Max(0,1+StatBonus(17)+TalentStat(17));
        if(MageSpellBehaviors.IsControlled(target)||ClassSpellBehaviors.IsControlled(target))factor*=Mathf.Max(0,1+StatBonus(18)+TalentStat(18));
        // Native Enemy.Damage applies the Megabonk elite stat; this is only the
        // independent original source stat, not another native elite multiplier.
        if(target.IsBoss()||target.IsElite())factor*=Mathf.Max(0,1+StatBonus(19)+TalentStat(19));
        if(target.hp<target.maxHp*.30f)factor*=Mathf.Max(0,1+StatBonus(20)+TalentStat(20));

        if(ShieldRemaining>0 || (MyPlayer.Instance?.inventory?.playerHealth?.shield??0)>0)factor*=Mathf.Max(0,1+StatBonus(28)+TalentStat(28));
        return factor;
    }
    internal static bool Incoming(DamageContainer dc,bool ignoreNativeShield)
    {
        if(!GenericActive || dc==null || dc.damage<=0)return true;
        if(!CombatSimulation.CanSimulate)return false;
        if(CombatSimulation.Time<invulnerableUntil){dc.damage=0;return false;}
        float initial=dc.damage;
        if(dc.enemy!=null)dc.damage*=ClassSpellBehaviors.EnemyDamageFactor(dc.enemy);
        dc.damage*=1-EffectiveDamageReduction;
        float afterReduction=dc.damage,sourceAbsorbed=0;
        foreach(var pair in shields.ToArray())
        {
            var shield=pair.Value;if(shield.Until<=CombatSimulation.Time)continue;
            float absorbed=Mathf.Min(shield.Remaining,dc.damage);shield.Remaining-=absorbed;dc.damage-=absorbed;sourceAbsorbed+=absorbed;
            if(shield.Remaining<=.0001f){shields.Remove(pair.Key);Notify("NotifyShieldBreak",absorbed,shield.Peak,pair.Key);}
            if(dc.damage<=0)break;
        }
        var health=MyPlayer.Instance?.inventory?.playerHealth;
        // Native shield blocks the entire admitted hit while positive, even
        // when damage exceeds its remaining amount (Damage RVA428398..3C2).
        bool nativeBlocks=!ignoreNativeShield&&health!=null&&health.shield>0;
        float covered=health!=null?Mathf.Min(Mathf.Max(0,health.overheal),dc.damage):0;
        bool tried=false,prevented=false;
        if(!nativeBlocks&&dc.damage>covered)
        {
            BindTalent();var method=talent?.GetMethod("TryPreventDeath",BindingFlags.Public|BindingFlags.Static);
            if(method!=null)
            {
                float original=dc.damage;tried=true;
                // Native HP is integer and Damage truncates (hp-damage), so
                // a fractional remainder below1 is lethal. Ceiling is used
                // only for the talent lethality view; rejected calls restore
                // the original damage and native shielding remains untouched.
                dc.damage=Mathf.Ceil(Mathf.Max(0,original-covered));
                try{prevented=method.Invoke(null,new object[]{dc}) is bool result&&result;}
                finally{dc.damage=prevented?Mathf.Max(0,dc.damage)+covered:original;}
            }
        }
        lastIncoming=new{InitialDamage=initial,AfterSourceReduction=afterReduction,SourceShieldAbsorbed=sourceAbsorbed,NativeShieldBlocks=nativeBlocks,NativeOverhealCovered=covered,IgnoreNativeShield=ignoreNativeShield,CheatDeathChecked=tried,CheatDeathPrevented=prevented,FinalDamage=dc.damage};
        return dc.damage>0;
    }
    internal static void Contact(Enemy enemy,float damage)
    {
        if(!GenericActive || !CombatSimulation.CanSimulate || enemy==null || inContact)return;
        inContact=true;
        try
        {
            Event(8,null,enemy,damage);ClassSpellBehaviors.Reflect(enemy);
        }
        finally{inContact=false;}
    }
    internal static void OnKilled(Enemy enemy)
    {
        if(!GenericActive || enemy==null)return;
        if(StatBonus(15)>0)Heal(StatBonus(15),"racial_health_per_kill");ClassPassives.OnKill(enemy);
    }
    internal static string SnapshotJson=>JsonSerializer.Serialize(new{ClassId,GenericActive,RaceId=ClassCatalogs.ActiveRaceId,Race=Race?.Name,ShieldRemaining,EffectiveDamageReduction,UncappedDamageReduction,LastHeal=lastHeal,LastIncoming=lastIncoming,InvulnerableFor=Mathf.Max(0,invulnerableUntil-CombatSimulation.Time),Stats=stats.Select(p=>new{id=p.Key,p.Value.Stat,p.Value.Value,Remaining=p.Value.Until-CombatSimulation.Time}),Shields=shields.Select(p=>new{id=p.Key,p.Value.Remaining,p.Value.Peak,RemainingSeconds=p.Value.Until==float.PositiveInfinity?-1:p.Value.Until-CombatSimulation.Time}),Behaviors=ClassSpellBehaviors.Snapshot(),Passives=ClassPassives.Snapshot(),Pets=SourcePets.Snapshot()});
}

[HarmonyPatch(typeof(PlayerStatsNew),nameof(PlayerStatsNew.GetBaseValue))]
internal static class SourceClassHealthBasePatch
{
    static void Postfix(EStat __0,ref float __result)
    {if(ClassCombat.GenericActive && __0==EStat.MaxHealth)__result=AbilityRuntime.Catalog.SourceClass.SourceBaseHealth;}
}
[HarmonyPatch(typeof(PlayerHealth),"OnLevelUp")]
internal static class SourceLevelHealthPatch
{
    // Native level is zero-based (initial HUD/runtime level0). Source level is
    // nativeLevel+1, so original 9*(sourceLevel-1) is 9*nativeLevel.
    // Native RVA4294C0 uses this argument only for its level-HP flat modifier.
    static void Prefix(ref int __0){if(ClassCombat.GenericActive)__0=9*Math.Max(0,__0);}
}
[HarmonyPatch(typeof(PlayerStats),nameof(PlayerStats.GetStat),new[]{typeof(EStat)})]
internal static class SourceOwnStatsPatch
{
    static void Postfix(EStat __0,ref float __result)
    {
        if(!ClassCombat.GenericActive)return;
        float CombinedFactor(int source)=>(1+ClassCombat.StatBonus(source)+ClassCombat.TalentStat(source))/Mathf.Max(.0001f,1+ClassCombat.TalentStat(source));
        switch(__0)
        {
            case EStat.DamageMultiplier:__result*=(1+ClassCombat.StatBonus(0)+ClassCombat.TalentStat(0)+ClassCombat.ConditionalGlobalDamage)/Mathf.Max(.0001f,1+ClassCombat.TalentStat(0));break;
            case EStat.AttackSpeed:__result*=CombinedFactor(1);break;
            case EStat.MaxHealth:__result*=1+ClassCombat.StatBonus(2)+ClassCombat.TalentStat(2);break;
            case EStat.CritChance:__result+=ClassCombat.StatBonus(3);break;
            case EStat.CritDamage:__result+=ClassCombat.StatBonus(4);break;
            case EStat.HealthRegen:__result+=ClassCombat.StatBonus(6)*60;break;
            case EStat.MoveSpeedMultiplier:__result*=CombinedFactor(7);break;
            case EStat.Luck:__result+=ClassCombat.StatBonus(12);break;
            case EStat.PickupRange:__result*=1+(ClassCombat.Race?.Bonus(7)??0);break;
            case EStat.XpIncreaseMultiplier:__result*=1+(ClassCombat.Race?.Bonus(8)??0);break;
            case EStat.GoldIncreaseMultiplier:__result*=1+(ClassCombat.Race?.Bonus(9)??0);break;
        }
    }
}
[HarmonyPatch(typeof(PlayerHealth),nameof(PlayerHealth.CheckStopDamage))]
internal static class SourceIncomingPatch
{
    // Only consume source shields after the native cooldown/evasion/admission
    // path accepted this damage. A prefix on Damage would absorb rejected hits.
    [HarmonyPriority(Priority.Last)]
    static void Postfix(DamageContainer __0,bool __1,ref bool __result)
    {if(ClassCombat.GenericActive && !__result && !ClassCombat.Incoming(__0,__1))__result=true;}
}
[HarmonyPatch(typeof(PlayerHealth),nameof(PlayerHealth.DamagePlayer))]
internal static class SourceContactDamagePatch
{
    static bool Prefix(PlayerHealth __instance,Enemy __0,out float __state)
    {__state=ClassCombat.GenericActive?__instance.hp+__instance.shield+ClassCombat.ShieldRemaining:-1;return !ClassCombat.GenericActive||!ClassSpellBehaviors.IsHarmless(__0);}
    static void Postfix(PlayerHealth __instance,Enemy __0,float __state)
    {if(__state<0)return;float lost=__state-__instance.hp-__instance.shield-ClassCombat.ShieldRemaining;if(lost>0)ClassCombat.Contact(__0,lost);}
}
