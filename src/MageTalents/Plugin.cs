using System.Reflection;
using System.Text.Json;
using AzerothAbilities;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Menu.Shop;
using Assets.Scripts.Inventory__Items__Pickups;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace MageTalents;

[BepInPlugin(Id,"Azeroth Class Talents","0.2.2")]
[BepInDependency("local.azerothmegabonk.abilities")]
public sealed class Plugin:BasePlugin
{
    public const string Id="local.azerothmegabonk.magetalents";
    internal static Plugin Current;
    internal static TalentCatalog Catalog;
    internal static TalentState Saved=new(),Run=new();
    internal static readonly Dictionary<string,TalentCatalog> Catalogs=new(StringComparer.Ordinal);
    static readonly Dictionary<string,TalentState> Profiles=new(StringComparer.Ordinal);
    static readonly string[] ClassIds={"warrior","paladin","hunter","rogue","priest","shaman","mage","warlock","druid"};
    static string selectedClass="mage";
    static bool maxTest;
    static string statePath;
    Harmony harmony;
    public override void Load()
    {
        Current=this;
        try
        {
            var folder=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            foreach(var id in ClassIds)
            {
                var file=Path.Combine(folder,"catalogs",id+".json");
                if(!File.Exists(file)&&id=="mage")file=Path.Combine(folder,"mage-talents.json");
                if(File.Exists(file)){var c=TalentCatalog.Load(file);if(c.ClassId!=id)throw new InvalidDataException("Class catalog mismatch:"+id);Catalogs.Add(id,c);}
            }
            if(!Catalogs.TryGetValue("mage",out Catalog))throw new InvalidDataException("Original Mage fallback catalog missing");
            var launch=Environment.GetCommandLineArgs();
            if(launch.Contains("-azeroth-test-battle")||launch.Contains("-azeroth-test-menu"))
            {
                var argument=launch.FirstOrDefault(s=>s.StartsWith("-azeroth-talent-points=",StringComparison.Ordinal));
                if(argument!=null && int.TryParse(argument.Substring("-azeroth-talent-points=".Length),out var budget) && budget>0)
                    TalentState.TemporaryPointBudget=budget;
            }
            statePath=Path.Combine(Paths.ConfigPath,Id+".json");
            if(File.Exists(statePath))
            {
                var json=File.ReadAllText(statePath);using var doc=JsonDocument.Parse(json);
                if(doc.RootElement.EnumerateObject().Any(p=>p.Name.Equals("profiles",StringComparison.OrdinalIgnoreCase)))
                {var bank=JsonSerializer.Deserialize<TalentProfileBank>(json,TalentCatalog.Json);if(bank?.Profiles!=null)foreach(var p in bank.Profiles)if(ClassIds.Contains(p.Key))Profiles[p.Key]=p.Value;}
                else Profiles["mage"]=JsonSerializer.Deserialize<TalentState>(json,TalentCatalog.Json)??new();
            }
            if(!Profiles.TryGetValue("mage",out Saved))Profiles["mage"]=Saved=new();
            if(!Saved.Valid(Catalog)){Log.LogWarning("Invalid laboratory Mage allocation reset; earned experience retained.");Saved.Ranks.Clear();Saved.TalentXp=Math.Max(0,Saved.TalentXp);}
            maxTest=(launch.Contains("-azeroth-test-battle")||launch.Contains("-azeroth-test-menu")) && launch.Contains("-azeroth-talents-max");
            if(maxTest && TalentState.TemporaryPointBudget>=Catalog.Talents.Sum(t=>t.MaxRank))
            {
                Saved.Ranks=new Dictionary<string,int>();
                foreach(var talent in Catalog.Talents)Saved.Ranks[talent.Id]=talent.MaxRank;
                if(!Saved.Valid(Catalog))throw new InvalidDataException("Invalid maxed original Mage talent allocation");
                Log.LogInfo($"Mage test startup maxed BEFORE native scene/inventory initialization: nodes={Saved.Ranks.Count}; spent={Saved.Spent}; available={Saved.Available(Catalog)}; budget={TalentState.TemporaryPointBudget}; no save writes.");
            }
            Run=Saved.Clone();
            AbilityRuntime.StarterOverride=TalentRuntime.Starter;
            AbilityRuntime.BeforeHitEvent+=TalentRuntime.BeforeHit;
            AbilityRuntime.HitEvent+=TalentRuntime.AfterHit;
            MageSpellBehaviors.RadiusMultiplier=TalentRuntime.Radius;
            MageSpellBehaviors.BarrierMultiplier=TalentRuntime.Barrier;
            harmony=new Harmony(Id);harmony.PatchAll(typeof(Plugin).Assembly);
            AddComponent<TalentBehaviour>();AddComponent<TalentPanel>();
            Log.LogInfo($"Original class talents loaded: classes={Catalogs.Count}; nodes={Catalogs.Values.Sum(c=>c.Talents.Count)}; active={Catalog.ClassId}; testBudget={TalentState.TemporaryPointBudget}; normal cap51; costs80+15*n. Source event runtime available; Mage legacy retained until GenericMageEnabled is explicitly enabled.");
        }
        catch(Exception ex){Log.LogError("Mage talent module disabled: "+ex);}
    }
    internal static bool TestSession=>Application.isBatchMode || Environment.GetCommandLineArgs().Any(s=>s is "-azeroth-test-battle" or "-azeroth-test-menu") || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1";
    internal static bool RefreshClass(string requested=null)
    {
        if(requested==null)
        {
            var heroes=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHeroes.HeroBridge")).FirstOrDefault(t=>t!=null);
            requested=heroes?.GetProperty("SelectedClassId")?.GetValue(null) as string;
            if(string.IsNullOrEmpty(requested))requested=heroes?.GetProperty("ActiveClassId")?.GetValue(null) as string;
            if(string.IsNullOrEmpty(requested))requested=(typeof(AbilityRuntime).GetField("Catalog",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(null) as SpellCatalog)?.ClassId;
        }
        if(string.IsNullOrEmpty(requested)||requested==selectedClass)return true;
        if(!Catalogs.TryGetValue(requested,out var catalog))return false;
        Profiles[selectedClass]=Saved;selectedClass=requested;Catalog=catalog;
        if(!Profiles.TryGetValue(requested,out Saved))Profiles[requested]=Saved=new();
        if(!Saved.Valid(Catalog)){Saved.Ranks.Clear();Saved.TalentXp=Math.Max(0,Saved.TalentXp);}
        if(maxTest&&TalentState.TemporaryPointBudget>=Catalog.Talents.Sum(t=>t.MaxRank))
            foreach(var t in Catalog.Talents)Saved.Ranks[t.Id]=t.MaxRank;
        Run=Saved.Clone();TalentRuntime.Clear();
        Current?.Log.LogInfo($"Source talent profile selected:{requested}; nodes={Catalog.Talents.Count}; XP={Saved.TalentXp}; spent={Saved.Spent}; test={TestSession}");
        return true;
    }
    internal static void Save()
    {
        if(TestSession || Catalog==null)return;
        Profiles[selectedClass]=Saved;
        var bank=new TalentProfileBank{Profiles=new(Profiles)};
        var temp=statePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(bank,TalentCatalog.Json));File.Move(temp,statePath,true);
    }
    public override bool Unload()
    {
        typeof(TalentPanel).GetMethod("Shutdown",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(null,Array.Empty<object>());
        harmony?.UnpatchSelf();AbilityRuntime.BeforeHitEvent-=TalentRuntime.BeforeHit;AbilityRuntime.HitEvent-=TalentRuntime.AfterHit;
        AbilityRuntime.StarterOverride=null;MageSpellBehaviors.RadiusMultiplier=null;MageSpellBehaviors.BarrierMultiplier=null;
        TalentRuntime.Clear();return true;
    }
}
public sealed class TalentProfileBank
{
    public int Version {get;set;}=2;
    public Dictionary<string,TalentState> Profiles {get;set;}=new();
}
public static class TalentBridge
{
    public static string ClassId{get{Plugin.RefreshClass();return Plugin.Catalog?.ClassId??"mage";}}
    public static bool SelectClass(string id)=>Plugin.RefreshClass(id);
    public static int AvailablePoints{get{Plugin.RefreshClass();return Plugin.Catalog==null?0:Plugin.Saved.Available(Plugin.Catalog);}}
    public static long TalentXp=>Plugin.Saved.TalentXp;
    public static int EarnedPoints=>Plugin.Catalog==null?0:Plugin.Saved.Earned(Plugin.Catalog);
    public static int TemporaryPointBudget=>TalentState.TemporaryPointBudget;
    public static int SpentPoints=>Plugin.Saved.Spent;
    public static string StateJson{get{Plugin.RefreshClass();return JsonSerializer.Serialize(new{ClassId=Plugin.Catalog?.ClassId,Plugin.Saved.TalentXp,Plugin.Saved.Ranks,Plugin.Saved.Spent},TalentCatalog.Json);}}
    public static string CatalogJson{get{Plugin.RefreshClass();return JsonSerializer.Serialize(Plugin.Catalog,TalentCatalog.Json);}}
    public static string CatalogDiagnosticsJson=>JsonSerializer.Serialize(new
    {
        Passed=Plugin.Catalogs.Count==9&&Plugin.Catalogs.Values.Sum(c=>c.Talents.Count)==354,
        ActiveClassId=Plugin.Catalog?.ClassId,
        Classes=Plugin.Catalogs.Values.Select(c=>new{c.ClassId,Nodes=c.Talents.Count,Trees=c.Trees.Select(t=>new{t.Id,t.Name,t.Icon}).ToArray()}),
        NativeGameplayVerification=false
    },TalentCatalog.Json);
    public static bool PendingNextRun=>TalentState.TemporaryPointBudget<=0 && MyPlayer.Instance!=null && MyPlayer.Instance.hasStarted;
    public static int Rank(string id){Plugin.RefreshClass();return Plugin.Saved.Rank(id);}
    public static bool CanSpend(string id){Plugin.RefreshClass();return Plugin.Catalog!=null&&Plugin.Saved.Clone().Spend(Plugin.Catalog,id);}
    public static bool CanRefund(string id){Plugin.RefreshClass();return Plugin.Catalog!=null&&Plugin.Saved.Clone().Refund(Plugin.Catalog,id);}
    public static bool Spend(string id){Plugin.RefreshClass();if(Plugin.Catalog==null||!Plugin.Saved.Spend(Plugin.Catalog,id))return false;ApplyDebugNow();Plugin.Save();return true;}
    public static bool Refund(string id){Plugin.RefreshClass();if(Plugin.Catalog==null||!Plugin.Saved.Refund(Plugin.Catalog,id))return false;ApplyDebugNow();Plugin.Save();return true;}
    public static void Reset(){Plugin.RefreshClass();Plugin.Saved.Ranks.Clear();ApplyDebugNow();Plugin.Save();}
    public static bool ApplyDebugNow()
    {
        if(TalentState.TemporaryPointBudget<=0 || !Plugin.TestSession)return false;
        Plugin.Run=Plugin.Saved.Clone();TalentRuntime.Clear();TalentRuntime.AddStartingSpells();
        // The explicit debug budget permits multiple capstones. Grant each selected
        // original starter without replacing learned spell instances or their ranks.
        AddDebugCapstoneStarters();
        var health=MyPlayer.Instance?.inventory?.playerHealth;
        if(health!=null)
        {
            AccessTools.Method(typeof(PlayerHealth),"UpdateMaxValues")?.Invoke(health,Array.Empty<object>());
            AccessTools.Method(typeof(PlayerHealth),"UpdateRegenValues")?.Invoke(health,new object[]{0f});
        }
        Plugin.Current.Log.LogInfo($"Mage debug talents applied immediately: spent={Plugin.Run.Spent},remaining={AvailablePoints}; learned spell ranks retained; no progression saves.");return true;
    }
    internal static void AddDebugCapstoneStarters()
    {
        if(TalentState.TemporaryPointBudget<=0)return;
        if(TalentRuntime.UsesGenericRuntime){TalentRuntime.AddDebugStartingSpells();return;}
        if(Plugin.Run.Rank("arcane_power")>0)AbilityRuntime.LearnStartingSpell("arcane_missiles");
        if(Plugin.Run.Rank("water_elemental")>0)AbilityRuntime.LearnStartingSpell("frostbolt");
    }
    // Only an offscreen laboratory probe may receive a temporary budget. It is never saved.
    public static bool SetProbeState(long xp,string ranksJson)
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1" || Plugin.Catalog==null)return false;
        var s=new TalentState{TalentXp=xp,Ranks=JsonSerializer.Deserialize<Dictionary<string,int>>(ranksJson)??new()};
        if(!s.Valid(Plugin.Catalog))return false;Plugin.Saved=s;Plugin.Run=s.Clone();TalentRuntime.Clear();return true;
    }
    public static long PreviewReward(float seconds,int kills,int bosses,bool won)=>TalentCatalog.BaselineRunXp(seconds,kills,bosses,won);
    public static bool SetPanelVisibleForProbe(bool visible)=>Application.isBatchMode && TalentPanel.Current!=null && TalentPanel.Current.SetProbeVisible(visible);
}
public sealed class TalentBehaviour:MonoBehaviour
{
    public TalentBehaviour(IntPtr ptr):base(ptr){}
    void Update(){try{Plugin.RefreshClass();TalentRuntime.Tick();}catch(Exception ex){Plugin.Current.Log.LogError("Source talent tick failed: "+ex);enabled=false;}}
}
[HarmonyPatch(typeof(AbilityRuntime),nameof(AbilityRuntime.ResetRun))]
static class ResetPatch
{
    static void Prefix(){Plugin.RefreshClass();Plugin.Run=Plugin.Saved.Clone();TalentRuntime.Clear();}
    static void Postfix(){TalentRuntime.AddStartingSpells();TalentBridge.AddDebugCapstoneStarters();}
}
[HarmonyPatch(typeof(MapSelectionUi),nameof(MapSelectionUi.StartMap))]
static class MapSnapshotPatch
{
    // Runs before native inventory/health construction, so maximum health and regeneration
    // are calculated from the newly selected allocation rather than the previous run.
    static void Prefix(){Plugin.RefreshClass();Plugin.Run=Plugin.Saved.Clone();TalentRuntime.Clear();}
}
[HarmonyPatch(typeof(AbilityRuntime),"Damage")]
static class DamagePatch
{
    static void Postfix(LearnedSpell s,ref float __result)
    {if(AbilityRuntime.Active&&!TalentRuntime.UsesGenericRuntime)__result*=1+Plugin.Run.SpellModifier(Plugin.Catalog,s.Definition.Id,s.Definition.School,0);}
}
[HarmonyPatch(typeof(AbilityRuntime),nameof(AbilityRuntime.Cooldown))]
static class CooldownPatch
{
    static void Postfix(LearnedSpell s,ref float __result)
    {if(AbilityRuntime.Active&&!TalentRuntime.UsesGenericRuntime)__result=Mathf.Max(.25f,__result*Mathf.Max(0,1-Plugin.Run.SpellModifier(Plugin.Catalog,s.Definition.Id,s.Definition.School,1)));}
}
[HarmonyPatch(typeof(AbilityRuntime),"Cast")]
static class CastPatch
{
    // Only the argument is replaced. The original spell's rank and upgrade state stay immutable.
    static void Prefix(ref LearnedSpell spell)
    {
        if(!AbilityRuntime.Active||TalentRuntime.UsesGenericRuntime)return;
        var extra=(int)Plugin.Run.SpellModifier(Plugin.Catalog,spell.Definition.Id,spell.Definition.School,2);if(extra==0)return;
        spell=new LearnedSpell(spell.Definition){Rank=spell.Rank,DamageFactor=spell.DamageFactor,CooldownFactor=spell.CooldownFactor,RadiusFactor=spell.RadiusFactor,DurationFactor=spell.DurationFactor,UpgradePicks=spell.UpgradePicks,ExtraTargets=spell.ExtraTargets+extra,NextCast=spell.NextCast};
    }
    static void Postfix(LearnedSpell spell,bool __result){if(__result && AbilityRuntime.Active)TalentRuntime.AfterCast(spell);}
}
[HarmonyPatch(typeof(PlayerStats),nameof(PlayerStats.GetStat),new[]{typeof(EStat)})]
static class StatPatch
{
    static void Postfix(EStat stat,ref float __result)
    {
        if(!AbilityRuntime.Active || Plugin.Catalog==null)return;
        var s=Plugin.Run;var c=Plugin.Catalog;
        switch(stat)
        {
            case EStat.DamageMultiplier:__result*=1+s.Stat(c,0);break;
            case EStat.AttackSpeed:__result*=1+s.Stat(c,1);break;
            case EStat.MaxHealth:if(!TalentRuntime.UsesGenericRuntime)__result*=1+s.Stat(c,2);break;
            case EStat.CritChance:__result+=s.Stat(c,3);break;
            case EStat.CritDamage:__result+=s.Stat(c,4);break;
            // Megabonk's native regeneration value is health per minute.
            case EStat.HealthRegen:__result+=s.Stat(c,6)*60;break;
            case EStat.MoveSpeedMultiplier:__result*=1+s.Stat(c,7);break;
            // SourceStatsRebuild adds talent Luck to its zero-based Luck bonus (VA14590B7B0).
            case EStat.Luck:__result+=s.Stat(c,12);break;
        }
    }
}
[HarmonyPatch(typeof(PlayerHealth),"Damage")]
static class IncomingDamagePatch
{
    [HarmonyPriority(Priority.First)]
    static void Prefix(DamageContainer dc){if(AbilityRuntime.Active&&!TalentRuntime.UsesGenericRuntime&&Plugin.Catalog!=null&&dc!=null)dc.damage*=Mathf.Max(0,1-Plugin.Run.Stat(Plugin.Catalog,5));}
}
[HarmonyPatch(typeof(Enemy),nameof(Enemy.EnemyDied),new[]{typeof(DamageContainer)})]
static class KillPatch
{
    [HarmonyPriority(Priority.First)]
    static void Prefix(Enemy __instance,DamageContainer dc)=>TalentRuntime.OnDeath(__instance,dc);
}
[HarmonyPatch(typeof(GameManager),"OnDied")]
static class EndLostPatch {static void Prefix()=>TalentRuntime.EndRun(false);}
[HarmonyPatch(typeof(GameManager),nameof(GameManager.OnTeleportAway))]
static class EndWinPatch {static void Prefix()=>TalentRuntime.EndRun(true);}
