using System.Reflection;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using Assets.Scripts.Managers;
using Assets.Scripts.Menu.Shop;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UObject=UnityEngine.Object;

namespace AzerothAbilities;

[BepInPlugin(Id,"Azeroth Class Abilities","0.4.0")]
public sealed class Plugin : BasePlugin
{
    public const string Id="local.azerothmegabonk.abilities";
    internal static Plugin Current;
    internal SpellCatalog Catalog;
    internal SpellCatalog LegacyMage;
    internal ConfigEntry<bool> GenericMage=null!;
    internal ConfigEntry<string> SelectedClass=null!;
    internal ConfigEntry<bool> SpellVisuals=null!;
    private Harmony harmony;
    public override void Load()
    {
        Current=this;
        SelectedClass=Config.Bind("Abilities","SelectedClass","none","Fallback source class when HeroRoster is absent; native HeroRoster selection is authoritative when installed.");
        GenericMage=Config.Bind("Abilities","GenericMageRuntime",false,"Enable the new generic Mage engine only for integration verification; false preserves the validated legacy Mage engine.");
        SpellVisuals=Config.Bind("Abilities","SpellVisuals",true,"Original local Mage spell assets rendered with the Unity adapter.");
        try
        {
            var path=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,"mage.json");
            LegacyMage=Catalog=SpellCatalog.Load(path);
            ClassCatalogs.Load(Path.Combine(Path.GetDirectoryName(path),"catalogs"));
            Log.LogInfo($"Loaded source-backed {Catalog.ClassId} rank-one catalog ({Catalog.Spells.Count} spells), source SHA256 {Catalog.SourceSha256}. Original upgrade modifiers and extended Mage behaviors loaded.");
            AbilityRuntime.Initialize(Catalog);
            SpellVfx.Initialize();MageSpellBehaviors.VisualEvent=SpellVfx.Play;
            harmony=new Harmony(Id);harmony.PatchAll(typeof(Plugin).Assembly);
            AddComponent<AbilityBehaviour>();
            AddComponent<MageSelectionBehaviour>();
        }
        catch(Exception ex){Log.LogError("Azeroth abilities disabled: "+ex);}
    }
    public override bool Unload(){harmony?.UnpatchSelf();AbilityRuntime.Clear();return true;}
}

public static class HudBridge
{
    public static bool Active=>AbilityRuntime.Active;
    public static string ClassId=>Active?AbilityRuntime.Catalog.ClassId:"";
    public static bool GenericRuntime=>ClassCombat.GenericActive;
    public static string ClassCombatSnapshotJson=>ClassCombat.SnapshotJson;
    public static string[] SpellIds=>AbilityRuntime.Spells.Select(s=>s.Definition.Id).ToArray();
    public static int[] SpellRanks=>AbilityRuntime.Spells.Select(s=>s.Rank).ToArray();
    public static string[] SpellNames=>AbilityRuntime.Spells.Select(s=>s.Definition.Name).ToArray();
    public static string[] SpellDescriptions=>AbilityRuntime.Spells.Select(DescribeSpell).ToArray();
    public static string[] SpellIcons=>AbilityRuntime.Spells.Select(s=>s.Definition.Icon).ToArray();
    public static int[] EffectiveQuantities=>AbilityRuntime.Spells.Select(s=>s.Definition.Targets>0?(ClassCombat.GenericActive?ClassSpellBehaviors.Quantity(s):TomeCompatibility.Quantity(s)):1).ToArray();
    public static float[] EffectiveDamages=>AbilityRuntime.Spells.Select(AbilityRuntime.Damage).ToArray();
    public static float[] EffectiveRadii=>AbilityRuntime.Spells.Select(s=>ClassCombat.GenericActive?ClassSpellBehaviors.Radius(s):s.Definition.Radius*s.RadiusFactor*MageSpellBehaviors.RadiusScale(s)*TomeCompatibility.Size).ToArray();
    public static float[] EffectiveDurations=>AbilityRuntime.Spells.Select(TomeCompatibility.SpellDuration).ToArray();
    private static string DescribeSpell(LearnedSpell spell)
    {
        if(ClassCombat.GenericActive)return GenericDescription.Spell(spell);
        var d=spell.Definition;var result=$"{d.Name} · Rank {spell.Rank}\nCooldown: {AbilityRuntime.Cooldown(spell):0.##} sec";
        if(d.Kind=="barrier")result+=$"\nAbsorbs {d.StatusValue*(AbilityRuntime.Catalog?.DamageScale??1)*Mathf.Sqrt(spell.DamageFactor)*Mathf.Max(0,MageSpellBehaviors.BarrierMultiplier?.Invoke(spell)??1)*100:0.#}% max HP";
        else if(d.Kind=="passive")result+=$"\n12% hit chance · 15% max HP explosion, damage cap {AbilityRuntime.Damage(spell):0.##}";
        else result+=$"\nDamage: {AbilityRuntime.Damage(spell):0.##}";
        if(d.Kind is "projectile" or "missiles" or "instant")result+=$" · Targets: {TomeCompatibility.Quantity(spell)}";
        if(d.Radius>0)result+=$"\nRadius: {d.Radius*spell.RadiusFactor*MageSpellBehaviors.RadiusScale(spell)*TomeCompatibility.Size:0.##}";
        if(d.Kind=="cone")result+=$"\nReach: {d.Range*spell.RadiusFactor*MageSpellBehaviors.RadiusScale(spell)*TomeCompatibility.Size:0.##}";
        if(d.Duration>0)result+=$"\nDuration: {TomeCompatibility.SpellDuration(spell):0.##} sec";
        return result;
    }
    public static float[] CooldownsRemaining=>AbilityRuntime.Spells.Select(s=>ClassCombat.GenericActive&&s.Definition.Kind=="passive"?ClassPassives.CooldownRemaining(s):Mathf.Max(0,s.NextCast-CombatSimulation.Time)).ToArray();
    public static float[] CooldownsTotal=>AbilityRuntime.Spells.Select(s=>ClassCombat.GenericActive&&s.Definition.Kind=="passive"?ClassPassives.CooldownTotal(s):AbilityRuntime.Cooldown(s)).ToArray();
        public static float[] SourceDamages=>AbilityRuntime.Spells.Select(s=>s.Definition.BaseDamage*s.Definition.CastDamageScale*AbilityRuntime.Catalog.DamageScale*s.DamageFactor).ToArray();
    public static float[] SourceCooldowns=>AbilityRuntime.Spells.Select(s=>s.Definition.BaseCooldown*AbilityRuntime.Catalog.CooldownScale*s.CooldownFactor).ToArray();
    // Call from the Unity game thread. Rarity is the original Azeroth index0..4.
    public static bool ApplyUpgradeForProbe(string spellId,int kind,int rarity)
    {
        if(!Active || rarity<0 || rarity>4)return false;
        var spell=AbilityRuntime.Spells.FirstOrDefault(s=>s.Definition.Id==spellId);
        if(spell==null || !spell.Definition.AllowedUpgrades.Contains(kind))return false;
        var rules=AbilityRuntime.Catalog.UpgradeRules;
        var delta=rules.Delta(spell,kind,rarity);if(delta<=0)return false;
        AbilityRuntime.Apply(new AbilityChoice(spell.Definition,new UpgradeDefinition{Id=kind.ToString(),Value=delta,SourceEvidence=rules.SourceEvidence,Description=$"source modifier{kind} rarity{rarity}"}));
        return true;
    }
    public static int CastCount=>AbilityRuntime.CastCount;
    public static int HitCount=>AbilityRuntime.HitCount;
    public static float DamageDealt=>AbilityRuntime.DamageDealt;
    public static float BarrierRemaining=>ClassCombat.GenericActive?ClassCombat.ShieldRemaining:MageSpellBehaviors.BarrierRemaining;
    public static string LastCastSpell=>AbilityRuntime.LastCastSpell;
    public static Vector3 LastCastDirection=>AbilityRuntime.LastCastDirection;
    public static int SourceVfxSpawned=>SpellVfx.Spawned;
    public static int SourceVfxLive=>SpellVfx.Live;
    public static bool CanSimulate=>CombatSimulation.CanSimulate;
    public static float CombatTime=>CombatSimulation.Time;
    public static string PauseReason=>CombatSimulation.PauseReason;
    public static bool LearnSpellForProbe(string id)
    {
        if(!Application.isBatchMode || !Active)return false;
        var definition=AbilityRuntime.Catalog.Spells.FirstOrDefault(s=>s.Id==id);if(definition==null)return false;
        if(!AbilityRuntime.Spells.Any(s=>s.Definition.Id==id))AbilityRuntime.Spells.Add(new LearnedSpell(definition));return true;
    }
    public static string[] AvailableChoices=>AbilityRuntime.Choices().Select(c=>c.Label).ToArray();
    public static bool SelectClass(string classId)
    {
        if((classId!="none" && !ClassCatalogs.ClassIds.Contains(classId)) || Plugin.Current?.Catalog==null)return false;
        if(ClassCatalogs.HasHeroRoster)return false;
        Plugin.Current.SelectedClass.Value=classId;
        ClassCatalogs.Refresh();AbilityRuntime.ResetRun();return true;
    }
}

public static class AbilityRuntime
{
    public static Func<string,string> StarterOverride;
    public static Action<LearnedSpell,Enemy,DamageContainer> BeforeHitEvent;
    public static Action<LearnedSpell,Enemy,DamageContainer,float> HitEvent;
    public static Action<LearnedSpell,Vector3> CastEvent;
    private static Vector3? castOrigin;
    static LearnedSpell autoWeapon;static float nextAuto;
    static int castSerial,currentCastSerial;static readonly HashSet<int> criticalCasts=new();
    internal static int CurrentCastSerial=>currentCastSerial;
    internal static Vector3? OverrideCastOrigin=>castOrigin;
    internal static float OverrideCastDamage=>castDamageScale;
    internal static float OverrideCastRange=>castRange;
    private static float castDamageScale=1,castRange;
    public static readonly List<LearnedSpell> Spells=new();
    internal static readonly List<SpellProjectile> Projectiles=new();
    internal static SpellCatalog Catalog;
    private static IntPtr lastInventory;
    private static IntPtr initializedNativeHealth;
    private static bool nativeRunVitalsPending;
    private static float nextSummary;
    private static bool reported;
    internal static readonly Dictionary<string,int> CastsBySpell=new(),HitsBySpell=new();
    internal static readonly Dictionary<string,float> DamageBySpell=new();
    public static int CastCount {get;private set;}
    public static int HitCount {get;private set;}
    public static float DamageDealt {get;private set;}
    public static string LastCastSpell {get;private set;}="";
    public static Vector3 LastCastDirection {get;private set;}=Vector3.forward;
    public static bool Active=>Catalog!=null && ClassCatalogs.ActiveClass==Catalog.ClassId;
    internal static void Initialize(SpellCatalog catalog){Catalog=catalog;}
    internal static void Clear()
    {
        foreach(var p in Projectiles)p.Dispose();Projectiles.Clear();Spells.Clear();lastInventory=IntPtr.Zero;
        MageSpellBehaviors.Clear();SpellVfx.Clear();ClassCombat.ClearScene();
    }
    public static void ResetRun()
    {
        Clear();CombatSimulation.Reset();CastCount=HitCount=0;DamageDealt=0;reported=false;CastsBySpell.Clear();HitsBySpell.Clear();DamageBySpell.Clear();DamageSourceRegistry.Clear();
        TomeCompatibilityDiagnostics.Reset();TargetAcquisition.Reset();ClassCombat.Reset();autoWeapon=null;nextAuto=0;criticalCasts.Clear();castSerial=currentCastSerial=0;
        if(ClassCombat.GenericActive && Catalog.SourceClass.SourceAutoWeapon.Enabled)
        {var a=Catalog.SourceClass.SourceAutoWeapon;autoWeapon=new LearnedSpell(new SpellDefinition{Id="source_auto_weapon",Name=Catalog.SourceClass.Name+" Auto Attack",Icon=Catalog.SourceClass.Icon,SourceKind=1,SourceSpellId=0,Kind="melee",School="Physical",BaseDamage=a.Damage*1.5f,BaseCooldown=a.Cooldown,Range=a.Range,Targets=1,CastKindId=5,CastDamageScale=1});}
        var starter=StarterOverride?.Invoke(Catalog?.StartingSpell??"fireball")??Catalog?.StartingSpell;
        if(Active && Catalog!=null)Spells.Add(new LearnedSpell(Catalog.Spells.First(s=>s.Id==starter)));
        lastInventory=MyPlayer.Instance?.inventory?.Pointer??IntPtr.Zero;
        Plugin.Current?.Log.LogInfo($"Azeroth run reset: selected={ClassCatalogs.ActiveClass}; source-backed rank-one spells={Spells.Count}");
    }
    // A native stage change keeps the same run and its learned source upgrades.
    // Only scene-owned projectiles/statuses are discarded; ResetRun is reserved for a new run.
    public static void RebindNativeStage()
    {
        foreach(var projectile in Projectiles)projectile.Dispose();Projectiles.Clear();
        MageSpellBehaviors.Clear();SpellVfx.Clear();ClassCombat.ClearScene();
        foreach(var spell in Spells)spell.NextCast=CombatSimulation.Time;
        lastInventory=MyPlayer.Instance?.inventory?.Pointer??IntPtr.Zero;
        Plugin.Current?.Log.LogInfo($"Azeroth stage rebound: stage={MapController.GetStageIndex()},final={MapController.IsFinalBossStage()}; retained="+string.Join(",",Spells.Select(s=>$"{s.Definition.Id}:{s.Rank}")));
    }
    internal static void PrepareNativeRunVitals()=>nativeRunVitalsPending=true;
    public static void StartNativePlayer()
    {
        ClassCatalogs.FinishNativeInventory();
        bool continuing=Active && Spells.Count>0 && (MapController.GetStageIndex()>0 || MapController.IsFinalBossStage());
        if(!ClassCatalogs.Refresh())
        {
            if(continuing)RebindNativeStage();else ResetRun();
        }
        RefreshNativeStartupVitals(!continuing);
    }
    private static void RefreshNativeStartupVitals(bool newRun)
    {
        if(!ClassCombat.GenericActive)return;
        var health=MyPlayer.Instance?.inventory?.playerHealth;if(health==null)return;
        bool fill=newRun && (nativeRunVitalsPending || initializedNativeHealth!=health.Pointer);
        int beforeHp=health.hp,beforeMax=health.maxHp;
        float carried=health.leftOverHeal,overheal=health.overheal;
        var update=AccessTools.Method(typeof(PlayerHealth),"OnStatUpdate");
        if(update==null)throw new MissingMethodException(typeof(PlayerHealth).FullName,"OnStatUpdate");
        // The native constructor caches instance stats before the source static
        // adapters are fully bound. ResetRun's talent postfix has now completed.
        // Native UpdateMaxValues also heals by the maximum-HP delta; refreshing
        // a continuing run must preserve its HP and fractional healing instead.
        try
        {
            update.Invoke(health,new object[]{EStat.MaxHealth});
            update.Invoke(health,new object[]{EStat.HealthRegen});
        }
        finally
        {
            health.hp=fill?health.maxHp:Math.Min(beforeHp,health.maxHp);
            health.leftOverHeal=fill?0:carried;
            health.overheal=Mathf.Min(overheal,health.maxOverheal);
        }
        initializedNativeHealth=health.Pointer;nativeRunVitalsPending=false;
        Plugin.Current?.Log.LogInfo($"Azeroth native vitals initialized: class={Catalog.ClassId},stage={MapController.GetStageIndex()},newRunFill={fill},hp={beforeHp}->{health.hp},maxHp={beforeMax}->{health.maxHp},sourceStat={PlayerStats.GetStat(EStat.MaxHealth):F3}");
    }
    internal static float Stat(EStat stat)
    {
        try{return PlayerStats.GetStat(stat);}catch{return stat is EStat.Projectiles or EStat.ProjectileBounces or EStat.CritChance?0:1;}
    }
    public static float Cooldown(LearnedSpell s)
    {
        if(!ClassCombat.GenericActive)return Mathf.Max(Catalog?.CooldownFloor??.25f,s.Definition.BaseCooldown*(Catalog?.CooldownScale??1)*s.CooldownFactor/Mathf.Max(.1f,Stat(EStat.AttackSpeed)));
        float raw=s.Definition.BaseCooldown*(Catalog?.CooldownScale??1.4f)*s.CooldownFactor*Mathf.Max(0,1-ClassCombat.SpellBonus(s,1));
        return Mathf.Max(s.Definition.SourcePassive?.CooldownFloorBeforeHaste??.25f,Mathf.Max(.25f,raw))/Mathf.Max(.1f,Stat(EStat.AttackSpeed))/ClassSpellBehaviors.StatusFactor(s);
    }
    public static float Damage(LearnedSpell s)
    {
        float source=s.Definition.BaseDamage*s.Definition.CastDamageScale*(Catalog?.DamageScale??1)*s.DamageFactor;
        if(ClassCombat.GenericActive)
        {
            source*=Mathf.Max(0,1+ClassCombat.SpellBonus(s,0));
            // Source learned+4C is modifier5: additive own maximum-health damage.
            // The cast-kind .5 scale affects the base part, not this additive part.
            if(s.Definition.BaseDamage>0)source+=ClassCombat.MaximumHealth*ClassCombat.SpellBonus(s,5);
        }
        return source*Mathf.Max(0,Stat(EStat.DamageMultiplier));
    }
    internal static void Tick()
    {
        ClassCatalogs.Refresh();
        var p=MyPlayer.Instance;
        if(!Active || p==null || !p.hasStarted || p.inventory==null)return;
        if(lastInventory!=p.inventory.Pointer){StartNativePlayer();lastInventory=p.inventory.Pointer;}
        if(!CombatSimulation.CanSimulate)return;
        if(ClassCombat.GenericActive)ClassCombat.Tick();else MageSpellBehaviors.Tick();
        if(ClassCombat.GenericActive && autoWeapon!=null && !TomeCompatibilityDiagnostics.SuppressAutomaticCasting && CombatSimulation.Time>=nextAuto)
        {var enemies=Targets(p.transform.position,autoWeapon.Definition.Range,true);if(enemies.Count>0){Hit(autoWeapon,enemies[0],autoWeapon.Definition.BaseDamage*Mathf.Max(0,Stat(EStat.DamageMultiplier)),p.transform.position);nextAuto=CombatSimulation.Time+autoWeapon.Definition.BaseCooldown*1.2f/Mathf.Sqrt(Mathf.Max(.1f,Stat(EStat.AttackSpeed)));}}
        if(!reported){reported=true;Plugin.Current?.Log.LogInfo($"Azeroth {Catalog.ClassId} runtime active; starting {Spells[0].Definition.Id}, source damage={Spells[0].Definition.BaseDamage*Catalog!.DamageScale:F3}, source cooldown={Spells[0].Definition.BaseCooldown*Catalog.CooldownScale:F3}. Megabonk tome/item multipliers applied separately. Native WeaponBase.Use suppressed.");}
        for(int i=Projectiles.Count-1;i>=0;i--)if(!Projectiles[i].Tick(CombatSimulation.DeltaTime)){Projectiles[i].Dispose();Projectiles.RemoveAt(i);}
        foreach(var spell in Spells.ToArray())
        {
            if(TomeCompatibilityDiagnostics.SuppressAutomaticCasting)break;
            if(spell.Definition.Kind=="passive")continue;
            if(CombatSimulation.Time<spell.NextCast)continue;
            if(Cast(spell,p))spell.NextCast=CombatSimulation.Time+Cooldown(spell);
        }
        if(Time.unscaledTime>=nextSummary)
        {
            nextSummary=Time.unscaledTime+15;
            Plugin.Current?.Log.LogInfo($"Azeroth ability telemetry: spells={Spells.Count},casts={CastCount},hits={HitCount},damage={DamageDealt:F2},projectiles={Projectiles.Count}");
        }
    }
    public static List<Enemy> Targets(Vector3 origin,float range,bool priority)
    {
        var result=new List<Enemy>();var em=EnemyManager.Instance;
        if(em==null)return result;
        // Snapshot wrappers before damage may remove dictionary entries.
        foreach(var pair in em.enemies)
        {
            var e=pair.Value;
            if(e!=null && !e.IsDeadOrDyingNextFrame() && (e.transform.position-origin).sqrMagnitude<=range*range)result.Add(e);
        }
        result.Sort((a,b)=>
        {
            if(priority)
            {
                var ap=a.IsBoss()||a.IsElite();var bp=b.IsBoss()||b.IsElite();
                if(ap!=bp)return ap?-1:1;
            }
            return (a.transform.position-origin).sqrMagnitude.CompareTo((b.transform.position-origin).sqrMagnitude);
        });
        return result;
    }
    private static bool Cast(LearnedSpell spell,MyPlayer player)
    {
        if(!CombatSimulation.CanSimulate)return false;
        if(ClassCombat.GenericActive)
        {
            int saved=currentCastSerial;currentCastSerial=++castSerial;
            try
            {
                bool casted=ClassSpellBehaviors.TryCast(spell,player);
                if(casted){RecordCast(spell,player);ClassCombat.Event(7,spell,null,0);}return casted;
            }
            finally{currentCastSerial=saved;}
        }
        if(MageSpellBehaviors.TryCast(spell,player,out var specialCasted))
        {if(specialCasted)RecordCast(spell,player);return specialCasted;}
        var d=spell.Definition;var origin=castOrigin??(player.transform.position+Vector3.up*0.8f);
        float activationRange=d.Kind=="selfAoe"?d.Radius*spell.RadiusFactor*MageSpellBehaviors.RadiusScale(spell)*Mathf.Max(0.1f,Stat(EStat.SizeMultiplier)):(castRange>0?castRange:d.Range);
        var targets=TargetAcquisition.SkillTargets(castOrigin??player.transform.position,activationRange,d.Kind is not ("projectile" or "missiles"));
        if(targets.Count==0)return false;
        var damage=Damage(spell)*castDamageScale;
        if(d.Kind=="selfAoe")
        {
            foreach(var target in targets.Where(e=>(e.transform.position-(castOrigin??player.transform.position)).sqrMagnitude<=activationRange*activationRange))Hit(spell,target,damage,origin);
            SpellVfx.Play(d.Id,"cast",player.transform.position,spell.RadiusFactor*MageSpellBehaviors.RadiusScale(spell)*TomeCompatibility.Size,.7f);
        }
        else
        {
            var count=TomeCompatibility.Quantity(spell);
            var volley=d.Kind=="instant"?null:TargetAcquisition.Prepare(spell,castOrigin??player.transform.position,activationRange,targets,count);
            for(int i=0;i<count;i++)
            {
                var target=volley!=null?volley.Assigned[i]:d.Targeting=="random"?targets[UnityEngine.Random.Range(0,targets.Count)]:targets[i%targets.Count];
                if(d.Kind=="instant")Hit(spell,target,damage,origin);
                else Projectiles.Add(new SpellProjectile(spell,target,TomeCompatibility.LaunchOffset(origin,target.transform.position,i,count),damage,rangeOverride:activationRange,launchDelay:i*.06f,volley:volley,projectileIndex:i));
            }
        }
        RecordCast(spell,player,targets[0].transform.position);
        return true;
    }
    private static void RecordCast(LearnedSpell spell,MyPlayer player,Vector3? target=null)
    {
        if(castOrigin.HasValue){SpellVfx.Play(spell.Definition.Id,"cast",castOrigin.Value,1,.25f);return;}
        CastCount++;CastsBySpell[spell.Definition.Id]=CastsBySpell.TryGetValue(spell.Definition.Id,out var casted)?casted+1:1;LastCastSpell=spell.Definition.Id;
        LastCastDirection=target.HasValue?(target.Value-player.transform.position).normalized:player.transform.forward;
        if(spell.Definition.Kind is "projectile" or "missiles" or "instant")
        {
            var hands=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothMegabonk.MageVisuals.MageVisualBridge",false)).FirstOrDefault(t=>t!=null)?.GetProperty("HandWorldPositions")?.GetValue(null) as Vector3[];
            if(hands!=null && hands.Length==2){SpellVfx.Play(spell.Definition.Id,"cast",hands[0],1,.25f);SpellVfx.Play(spell.Definition.Id,"cast",hands[1],1,.25f);}
            else SpellVfx.Play(spell.Definition.Id,"cast",player.transform.position+Vector3.up,1,.25f);
        }
        CastEvent?.Invoke(spell,player.transform.position);
    }
    public static bool CastFreeSpell(string id)
    {
        var player=MyPlayer.Instance;if(!CombatSimulation.CanSimulate || player?.inventory==null || !player.hasStarted)return false;
        var spell=Spells.FirstOrDefault(s=>s.Definition.Id==id);
        var definition=ClassCatalogs.Definition(id);if(definition==null)return false;
        return Cast(spell??new LearnedSpell(definition),player);
    }
    public static bool LearnStartingSpell(string id)
    {
        if(!Active)return false;var definition=Catalog.Spells.FirstOrDefault(s=>s.Id==id);if(definition==null)return false;
        if(!Spells.Any(s=>s.Definition.Id==id))Spells.Add(new LearnedSpell(definition));return true;
    }
    public static bool LaunchPetProjectile(Enemy target,Vector3 origin,float damage)
    {
        if(!CombatSimulation.CanSimulate || target==null || target.IsDeadOrDyingNextFrame())return false;
        var definition=new SpellDefinition{Id="water_elemental",Name="Water Elemental",Icon="Spell_Frost_SummonWaterElemental",School="Frost",Kind="projectile",BaseDamage=50,ProjectileSpeed=28,Range=28,Targets=1,StatusKind=1,StatusValue=.3f,StatusDuration=2};
        Projectiles.Add(new SpellProjectile(new LearnedSpell(definition),target,origin,damage,28,28));return true;
    }
    public static bool CastFreeSpellAt(string id,Vector3 origin,float damageMultiplier=1,float rangeOverride=0)
    {
        var oldOrigin=castOrigin;var oldScale=castDamageScale;var oldRange=castRange;
        try{castOrigin=origin;castDamageScale=Mathf.Max(0,damageMultiplier);castRange=rangeOverride;return CastFreeSpell(id);}
        finally{castOrigin=oldOrigin;castDamageScale=oldScale;castRange=oldRange;}
    }
    public static void Hit(LearnedSpell spell,Enemy target,float damage,Vector3 from,int castToken=0)
        =>HitMeasured(spell,target,damage,from,castToken);
    internal static float HitMeasured(LearnedSpell spell,Enemy target,float damage,Vector3 from,int castToken=0,bool allowCritical=true)
    {
        if(!CombatSimulation.CanSimulate || target==null || target.IsDeadOrDyingNextFrame())return 0;
        if(ClassCombat.GenericActive)ClassPassives.BeforeHit(spell,target);else MageSpellBehaviors.BeforeHit(spell,target);if(target.IsDeadOrDyingNextFrame())return 0;
        if(ClassCombat.GenericActive)damage*=ClassCombat.DamageFactor(spell,target);
        DamageSourceRegistry.Register("Azeroth:"+spell.Definition.Id,spell);
        var dc=new DamageContainer(0,"Azeroth:"+spell.Definition.Id)
        {
            damage=damage,direction=(target.transform.position-from).normalized,knockback=0,
            element=spell.Definition.School=="Fire"?EElement.Fire:spell.Definition.School=="Frost"?EElement.Ice:EElement.Neutral,
            crit=false,isExecute=false,enemy=target
        };
        var hp=target.hp;float actual=0;
        if(allowCritical)TomeCompatibility.ApplyCritical(dc);
        try
        {
            BeforeHitEvent?.Invoke(spell,target,dc);
            TomeCompatibilityDiagnostics.RecordHit(spell,target,dc);
            target.DamageFromPlayerOther(dc);
            actual=Mathf.Max(0,hp-target.hp);
            if(ClassCombat.GenericActive)
            {
                ClassSpellBehaviors.AfterHit(spell,target,dc,actual);ClassPassives.AfterHit(spell,target,dc,actual);
                ClassCombat.Event(1,spell,target,actual);
                if(dc.crit)
                {
                    ClassCombat.Event(2,spell,target,actual);int token=castToken!=0?castToken:currentCastSerial;
                    if(token!=0 && criticalCasts.Add(token))ClassCombat.Event(3,spell,target,actual);
                }
            }
            else MageSpellBehaviors.AfterHit(spell,target,dc);
        }
        finally{HitEvent?.Invoke(spell,target,dc,hp);}
        SpellVfx.Play(spell.Definition.Id,"impact",target.transform.position+Vector3.up*.8f,1,.2f);
        HitCount++;DamageDealt+=actual;
        var id=spell.Definition.Id;HitsBySpell[id]=HitsBySpell.TryGetValue(id,out var count)?count+1:1;DamageBySpell[id]=(DamageBySpell.TryGetValue(id,out var dealt)?dealt:0)+actual;
        if(HitCount<=8)Plugin.Current?.Log.LogInfo($"Azeroth hit {spell.Definition.Id}: enemy={target.id},requested={damage:F3},hp={hp:F3}->{target.hp:F3}");
        return actual;
    }
    internal static float HitTalentDot(LearnedSpell spell,Enemy target,float damage,string id)
    {
        if(!CombatSimulation.CanSimulate || target==null || target.IsDeadOrDyingNextFrame())return 0;
        DamageSourceRegistry.Register("AzerothTalent:"+id,spell);
        var dc=new DamageContainer(0,"AzerothTalent:"+id){damage=damage,crit=false,knockback=0,enemy=target,direction=Vector3.zero,element=spell.Definition.School=="Fire"?EElement.Fire:spell.Definition.School=="Frost"?EElement.Ice:EElement.Neutral};
        float before=target.hp;
        try{BeforeHitEvent?.Invoke(spell,target,dc);TomeCompatibilityDiagnostics.RecordHit(spell,target,dc);target.DamageFromPlayerOther(dc);}
        finally{HitEvent?.Invoke(spell,target,dc,before);}
        float actual=Mathf.Max(0,before-target.hp);HitCount++;DamageDealt+=actual;
        string key="talent:"+id;HitsBySpell[key]=HitsBySpell.TryGetValue(key,out var count)?count+1:1;DamageBySpell[key]=(DamageBySpell.TryGetValue(key,out var dealt)?dealt:0)+actual;
        return actual;
    }
    internal static void AddProjectile(LearnedSpell spell,Enemy target,Vector3 origin,float damage,float delay=0)=>Projectiles.Add(new SpellProjectile(spell,target,origin,damage,launchDelay:delay,castToken:currentCastSerial));
    internal static void AddVolleyProjectile(LearnedSpell spell,Enemy target,Vector3 origin,float damage,float delay,VolleyPlan volley,int index)=>Projectiles.Add(new SpellProjectile(spell,target,origin,damage,rangeOverride:volley.SourceRange,launchDelay:delay,castToken:currentCastSerial,volley:volley,projectileIndex:index));
    internal static List<AbilityChoice> Choices(int rarity=0)
    {
        var result=new List<AbilityChoice>();
        if(!Active || Catalog==null)return result;
        foreach(var definition in Catalog.Spells)
        {
            var learned=Spells.FirstOrDefault(s=>s.Definition.Id==definition.Id);
            if(learned==null)result.Add(new AbilityChoice(definition,null));
                        else foreach(var kind in definition.AllowedUpgrades)
            {
                var delta=Catalog.UpgradeRules.Delta(learned,kind,rarity);
                if(delta<=0.00001f)continue;
                var modifier=kind switch{0=>"damage",1=>"cooldown",2=>"targets",3=>"radius",4=>"duration",_=>"unsupported"};
                var description=kind switch{0=>$"+{delta*100:0.##}% damage",1=>$"-{delta*100:0.##}% base cooldown",2=>$"+{delta:0} target(s)",3=>$"+{delta*100:0.##}% base radius",4=>$"+{delta*100:0.##}% base duration",_=>""};
                result.Add(new AbilityChoice(definition,new UpgradeDefinition{Id=kind.ToString(),Modifier=modifier,Value=delta,SourceEvidence=Catalog.UpgradeRules.SourceEvidence,Description=description}));
            }
        }
        return result;
    }
    internal static void Apply(AbilityChoice choice)
    {
        var learned=Spells.FirstOrDefault(s=>s.Definition.Id==choice.Spell.Id);
        if(learned==null)Spells.Add(new LearnedSpell(choice.Spell));
        else if(choice.Upgrade!=null)
        {
            Catalog.UpgradeRules.Apply(learned,int.Parse(choice.Upgrade.Id),choice.Upgrade.Value);
        }
        Plugin.Current?.Log.LogInfo($"Azeroth choice applied: {choice.Label}; source={choice.Upgrade?.SourceEvidence??choice.Spell.SourceOffset}");
    }
}

internal sealed class AbilityChoice
{
    internal SpellDefinition Spell; internal UpgradeDefinition Upgrade; internal AbilityChoice(SpellDefinition spell,UpgradeDefinition upgrade){Spell=spell;Upgrade=upgrade;}
    internal string Label=>Upgrade==null?"Learn "+Spell.Name:Spell.Name+": "+Upgrade.Description;
    internal string Description=>Upgrade?.Description ?? (ClassCombat.GenericActive?GenericDescription.Spell(new LearnedSpell(Spell)):(Spell.Kind switch
    {
        "barrier"=>$"Absorbs {Spell.StatusValue*AbilityRuntime.Catalog.DamageScale*100:0.#}% max HP for {Spell.Duration*AbilityRuntime.Catalog.DurationScale:0.#} sec; refreshes when broken.",
        "passive"=>$"12% spell-hit chance to polymorph for 10 sec; up to 3 sheep. Next hit explodes for 15% max HP, capped at {Spell.BaseDamage*AbilityRuntime.Catalog.DamageScale:0.#}; radius {Spell.Radius:0.#}.",
        "groundAoe"=>$"{Spell.BaseDamage*AbilityRuntime.Catalog.DamageScale/Math.Max(1,Spell.Ticks):0.##} {Spell.School} damage per tick for {Spell.Duration*AbilityRuntime.Catalog.DurationScale:0.#} sec; radius {Spell.Radius:0.#}.",
        _=>$"Rank 1 · {Spell.BaseDamage*Spell.CastDamageScale*AbilityRuntime.Catalog.DamageScale:0.##} {Spell.School} damage · {Spell.BaseCooldown*AbilityRuntime.Catalog.CooldownScale:0.##} sec cooldown."
    }));
}

internal sealed class SpellProjectile
{
    private readonly LearnedSpell spell;private Enemy target;private uint enemyId;
    private readonly float damage,speed,range,size,launchAt,createdAt;private readonly int castToken;
    private readonly bool appliesDot;
    private readonly VolleyPlan volley;private readonly int projectileIndex,projectileId;private bool ownsClaim;
    private Vector3 dotLegOrigin;private float dotLegStarted,dotLegDue,dotLegDistance;
    private readonly HashSet<uint> hitEnemies=new();
    private int bounces;
    private Vector3 pos,destination;private float distance;private GameObject visual;
    private bool destinationOnly,finished,disposed;private string finishReason;
    internal SpellProjectile(LearnedSpell spell,Enemy target,Vector3 position,float damage,float speedOverride=0,float rangeOverride=0,float launchDelay=0,int castToken=0,VolleyPlan volley=null,int projectileIndex=-1)
    {
        this.castToken=castToken;this.spell=spell;this.target=target;enemyId=target.id;pos=position;destination=position;this.damage=damage;createdAt=CombatSimulation.Time;
        this.volley=volley;this.projectileIndex=projectileIndex;projectileId=TargetAcquisition.NewProjectile();
        if(volley!=null){volley.Claim(enemyId);ownsClaim=true;}
        float probeSpeed=TomeCompatibilityDiagnostics.CastProjectileSpeedOverride;
        speed=(probeSpeed>0?probeSpeed:speedOverride>0?speedOverride:spell.Definition.ProjectileSpeed)*TomeCompatibility.ProjectileSpeed;
        float sourceRange=rangeOverride>0?rangeOverride:AbilityRuntime.OverrideCastRange>0?AbilityRuntime.OverrideCastRange:spell.Definition.Range;
        range=TargetAcquisition.DetectionRange(sourceRange)*TomeCompatibility.Duration;
        size=TomeCompatibility.Size;bounces=TomeCompatibility.ExtraBounces;launchAt=CombatSimulation.Time+Mathf.Max(0,launchDelay);
        appliesDot=ClassCombat.GenericActive&&spell.Definition.Kind=="damageOverTime";
        RefreshDestination();if(appliesDot)BeginDotLeg(launchAt);
        TomeCompatibilityDiagnostics.ProjectileCreated();
        Event("created",0,enemyId);
        if(launchDelay<=0)EnsureVisual();
    }
    private void EnsureVisual()
    {
        if(visual==null && Plugin.Current!.SpellVisuals.Value && AbilityBehaviour.RenderVisuals)
        {visual=SpellVfx.Projectile(spell.Definition.Id,pos,size);if(visual!=null)TomeCompatibilityDiagnostics.ProjectileVisualCreated();}
    }
    private void BeginDotLeg(float started)
    {
        dotLegOrigin=pos;dotLegStarted=started;
        var delta=destination-pos;delta.y=0;dotLegDistance=delta.magnitude;
        dotLegDue=started+dotLegDistance/Mathf.Max(.0001f,speed);
    }
    internal object Snapshot()=>new{Spell=spell.Definition.Id,Enemy=enemyId,ProjectileId=projectileId,ProjectileIndex=projectileIndex,VolleyId=volley?.VolleyId??0,CastToken=castToken,SourceRange=volley?.SourceRange,DetectionRange=volley!=null?TargetAcquisition.DetectionRange(volley.SourceRange):(float?)null,TargetDistance=Vector3.Distance(pos,destination),X=pos.x,Y=pos.y,Z=pos.z,Distance=distance,Range=range,RemainingRange=Mathf.Max(0,range-distance),Speed=speed,Size=size,Bounces=bounces,LaunchRemaining=Mathf.Max(0,launchAt-CombatSimulation.Time),AppliesDamageOverTime=appliesDot,DotDeliveryRemaining=appliesDot&&!destinationOnly?Mathf.Max(0,dotLegDue-CombatSimulation.Time):0,HasVisual=visual!=null,VisualActive=VisualActive(),VisualPosition=VisualPosition(),Destination=new[]{destination.x,destination.y,destination.z},LastValidDestination=new[]{destination.x,destination.y,destination.z},TargetAlive=ValidTarget(),DestinationOnly=destinationOnly,Age=Mathf.Max(0,CombatSimulation.Time-createdAt),Finished=finished,FinishReason=finishReason};
    private bool ValidTarget()
    {try{return target!=null&&target.id==enemyId&&!target.IsDeadOrDyingNextFrame();}catch{return false;}}
    private bool RefreshDestination()
    {
        try
        {
            var point=target.collider!=null?target.collider.bounds.center:target.transform.position+Vector3.up;
            if(!float.IsFinite(point.x)||!float.IsFinite(point.y)||!float.IsFinite(point.z))return false;
            destination=point;return true;
        }
        catch{return false;}
    }
    private bool VisualActive(){try{return visual!=null&&visual.activeInHierarchy;}catch{return false;}}
    private float[] VisualPosition(){try{if(visual!=null){var p=visual.transform.position;return new[]{p.x,p.y,p.z};}}catch{}return null;}
    private void SyncVisual()
    {
        if(visual==null)return;visual.transform.position=pos;
        var direction=destination-pos;if(direction.sqrMagnitude>.001f)visual.transform.rotation=Quaternion.LookRotation(direction);
    }
    private void Event(string kind,uint oldEnemy,uint newEnemy,IEnumerable<Enemy> eligible=null,bool appliesDamage=false,string outcome=null)
        =>TargetAcquisition.ProjectileEvent(kind,projectileId,projectileIndex,volley,spell.Definition.Id,castToken,oldEnemy,newEnemy,pos,range,Mathf.Max(0,range-distance),eligible,Vector3.Distance(pos,destination),destination,Mathf.Max(0,CombatSimulation.Time-createdAt),ValidTarget(),appliesDamage,outcome,VisualActive(),VisualPosition());
    private bool Finish(string reason)
    {finished=true;finishReason=reason;Event(reason=="range-expired"?"expired-range":"finished",enemyId,enemyId,outcome:reason);return false;}
    private bool MoveTowards(Vector3 endpoint,float step)
    {
        float remaining=Mathf.Max(0,range-distance),travel=Mathf.Min(Mathf.Max(0,step),remaining);
        var previous=pos;pos=Vector3.MoveTowards(pos,endpoint,travel);
        distance=Mathf.Min(range,distance+Vector3.Distance(previous,pos));
        bool arrived=pos.x==endpoint.x&&pos.y==endpoint.y&&pos.z==endpoint.z;
        if(!arrived&&travel>0&&travel>=remaining)distance=range;
        SyncVisual();return arrived;
    }
    private void ReplaceTarget(Enemy replacement)
    {
        if(ownsClaim){volley.Release(enemyId);ownsClaim=false;}
        target=replacement;enemyId=replacement.id;
        if(volley!=null){volley.Claim(enemyId);ownsClaim=true;}RefreshDestination();
    }
    private bool RetargetDead()
    {
        uint old=enemyId;if(ownsClaim){volley.Release(old);ownsClaim=false;}
        var candidates=TargetAcquisition.DistinctNearest(AbilityRuntime.Targets(pos,Mathf.Max(0,range-distance),false).Where(e=>!hitEnemies.Contains(e.id)&&(!appliesDot||!ClassSpellBehaviors.HasDot(e,spell.Definition.Id))),pos);
        var replacement=volley?.Replacement(candidates)??candidates.FirstOrDefault();
        if(replacement==null)
        {Event("dead-target-no-replacement",old,0,candidates);return false;}
        ReplaceTarget(replacement);if(appliesDot)BeginDotLeg(CombatSimulation.Time);
        Event("retarget-dead",old,enemyId,candidates);return true;
    }
    internal bool Tick(float dt)
    {
        if(finished)return false;
        if(!CombatSimulation.CanSimulate)return true;
        if(CombatSimulation.Time<launchAt)return true;
        if(distance>range)return Finish("range-expired");
        if(!destinationOnly&&(!ValidTarget()||!RefreshDestination()))
        {
            Event("target-lost",enemyId,0);
            if(!RetargetDead())
            {
                // Keep a value-type world point: a pooled/dead Enemy transform
                // can move or be rebound before this projectile reaches it.
                destinationOnly=true;target=null;Event("ballistic-destination",enemyId,0);
            }
        }
        EnsureVisual();
        var endpoint=destination;
        if(destinationOnly)
        {
            if(MoveTowards(endpoint,speed*dt))
            {
                Event("arrival-impact",enemyId,0,outcome:"last-destination-no-damage");
                SpellVfx.Play(spell.Definition.Id,"impact",pos,1,.2f);return Finish("last-destination-no-damage");
            }
            return distance>=range?Finish("range-expired"):true;
        }
        if(appliesDot)
        {
            float progress=Mathf.Clamp01((CombatSimulation.Time-dotLegStarted)/Mathf.Max(.0001f,dotLegDue-dotLegStarted));
            var nextPosition=Vector3.Lerp(dotLegOrigin,endpoint,progress);MoveTowards(nextPosition,Vector3.Distance(pos,nextPosition));
            if((pos-endpoint).sqrMagnitude>0||CombatSimulation.Time<dotLegDue)return distance>=range?Finish("range-expired"):true;
            hitEnemies.Add(enemyId);pos=endpoint;
            Event("arrival-impact",enemyId,enemyId,appliesDamage:true,outcome:"apply-dot");
            ClassSpellBehaviors.ApplySpellDot(spell,target,damage,castToken);TomeCompatibilityDiagnostics.ProjectileHit();
            Event("hit-dot",enemyId,enemyId);
            if(bounces<=0)return Finish("hit-dot");
            var nextDot=AbilityRuntime.Targets(endpoint,Mathf.Max(0,range-distance),false).FirstOrDefault(e=>!hitEnemies.Contains(e.id));
            if(nextDot==null)return Finish("hit-dot-no-bounce");
            uint oldDot=enemyId;bounces--;ReplaceTarget(nextDot);BeginDotLeg(CombatSimulation.Time);TomeCompatibilityDiagnostics.ProjectileBounced();Event("bounce",oldDot,enemyId);return true;
        }
        var from=pos;
        // A large Size tome must not turn the arrival threshold into an early
        // damage radius. Move the real visual first, then resolve the arrival.
        if(MoveTowards(endpoint,speed*dt))
        {
            hitEnemies.Add(enemyId);Event("arrival-impact",enemyId,enemyId,appliesDamage:true,outcome:"direct-hit");
            AbilityRuntime.Hit(spell,target,damage,from,castToken);TomeCompatibilityDiagnostics.ProjectileHit();
            Event("hit",enemyId,enemyId);
            if(bounces<=0)return Finish("hit");
            var next=AbilityRuntime.Targets(endpoint,Mathf.Max(0,range-distance),false).FirstOrDefault(e=>!hitEnemies.Contains(e.id));
            if(next==null)return Finish("hit-no-bounce");
            uint old=enemyId;bounces--;ReplaceTarget(next);TomeCompatibilityDiagnostics.ProjectileBounced();Event("bounce",old,enemyId);
            return true;
        }
        return distance>=range?Finish("range-expired"):true;
    }
    internal void Dispose(){if(disposed)return;disposed=true;if(ownsClaim){volley.Release(enemyId);ownsClaim=false;}Event("disposed",enemyId,enemyId,outcome:finishReason??"external-clear");SpellVfx.Release(visual);visual=null;}
}

public sealed class AbilityBehaviour : MonoBehaviour
{
    public AbilityBehaviour(IntPtr pointer):base(pointer){}
    internal static bool RenderVisuals=>!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_RENDER_PROBE")=="1";
    private static Material fire,arcane;
    private bool failed;
    private void Update()
    {
        try{AbilityRuntime.Tick();}catch(Exception ex){if(!failed){failed=true;Plugin.Current?.Log.LogError("Azeroth spell tick failed; class selection, learned ranks and native weapon suppression retained: "+ex);}}
    }
    internal static GameObject Orb(Vector3 position,string school)
    {
        var orb=GameObject.CreatePrimitive(PrimitiveType.Sphere);orb.name="AzerothSpellVisual";orb.transform.position=position;orb.transform.localScale=Vector3.one*0.28f;
        var collider=orb.GetComponent<Collider>();if(collider!=null)UObject.Destroy(collider);
        var renderer=orb.GetComponent<Renderer>();var material=school=="Fire"?fire:arcane;
        if(material==null)
        {
            var shader=Shader.Find("Unlit/Color")??Shader.Find("Sprites/Default");
            material=new Material(shader);material.color=school=="Fire"?new Color(1,0.28f,0.04f):new Color(0.65f,0.25f,1);
            if(school=="Fire")fire=material;else arcane=material;
        }
        renderer.sharedMaterial=material;return orb;
    }
    internal static void Pulse(Vector3 position,float radius,string school)
    {
        var orb=Orb(position,school);orb.transform.localScale=new Vector3(radius*2,0.12f,radius*2);UObject.Destroy(orb,0.12f);
    }
}

[HarmonyPatch(typeof(WeaponBase),nameof(WeaponBase.Use))]
internal static class NativeWeaponsPatch
{
    private static bool Prefix()=>NativeWeaponPolicy.AllowNativeUse();
}
[HarmonyPatch(typeof(MyPlayer),nameof(MyPlayer.StartPlayer))]
internal static class StartRunPatch
{
    private static void Postfix(){AbilityRuntime.StartNativePlayer();}
}







[HarmonyPatch(typeof(MapSelectionUi),nameof(MapSelectionUi.StartMap))]
internal static class SourcePrepareInventoryPatch
{
    // Source class/race bases and selected talents must be bound before the
    // native inventory constructor caches its first PlayerStatsNew values.
    [HarmonyPriority(Priority.First)]
    static void Prefix(){AbilityRuntime.PrepareNativeRunVitals();ClassCatalogs.PrepareNativeInventory();}
}
