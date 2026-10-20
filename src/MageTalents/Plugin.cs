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

[BepInPlugin(Id,"Azeroth Mage Talents","0.1.0")]
[BepInDependency("local.azerothmegabonk.abilities")]
public sealed class Plugin:BasePlugin
{
    public const string Id="local.azerothmegabonk.magetalents";
    internal static Plugin Current;
    internal static TalentCatalog Catalog;
    internal static TalentState Saved=new(),Run=new();
    static string statePath;
    Harmony harmony;
    public override void Load()
    {
        Current=this;
        try
        {
            Catalog=TalentCatalog.Load(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"mage-talents.json"));
            var launch=Environment.GetCommandLineArgs();
            if(launch.Contains("-azeroth-test-battle"))
            {
                var argument=launch.FirstOrDefault(s=>s.StartsWith("-azeroth-talent-points=",StringComparison.Ordinal));
                if(argument!=null && int.TryParse(argument.Substring("-azeroth-talent-points=".Length),out var budget) && budget>0)
                    TalentState.TemporaryPointBudget=budget;
            }
            statePath=Path.Combine(Paths.ConfigPath,Id+".json");
            if(File.Exists(statePath))Saved=JsonSerializer.Deserialize<TalentState>(File.ReadAllText(statePath),TalentCatalog.Json)??new();
            if(!Saved.Valid(Catalog)){Log.LogWarning("Invalid laboratory Mage allocation reset; earned experience retained.");Saved.Ranks.Clear();Saved.TalentXp=Math.Max(0,Saved.TalentXp);}
            if(launch.Contains("-azeroth-test-battle") && launch.Contains("-azeroth-talents-max") && TalentState.TemporaryPointBudget>=Catalog.Talents.Sum(t=>t.MaxRank))
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
            Log.LogInfo($"Mage source talents loaded:39 nodes/3 trees; XP={Saved.TalentXp}; earned={Saved.Earned(Catalog)}; testBudget={TalentState.TemporaryPointBudget}; F6 opens talents. Original normal progression capped51; costs80+15*n; baseline first-zone rewards; native crit/movement adapter.");
        }
        catch(Exception ex){Log.LogError("Mage talent module disabled: "+ex);}
    }
    internal static bool TestSession=>Application.isBatchMode || Environment.GetCommandLineArgs().Any(s=>s=="-azeroth-test-battle") || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1";
    internal static void Save()
    {
        if(TestSession || Catalog==null)return;
        var temp=statePath+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(Saved,TalentCatalog.Json));File.Move(temp,statePath,true);
    }
    public override bool Unload()
    {
        harmony?.UnpatchSelf();AbilityRuntime.BeforeHitEvent-=TalentRuntime.BeforeHit;AbilityRuntime.HitEvent-=TalentRuntime.AfterHit;
        AbilityRuntime.StarterOverride=null;MageSpellBehaviors.RadiusMultiplier=null;MageSpellBehaviors.BarrierMultiplier=null;
        TalentRuntime.Clear();return true;
    }
}
public static class TalentBridge
{
    public static int AvailablePoints=>Plugin.Catalog==null?0:Plugin.Saved.Available(Plugin.Catalog);
    public static long TalentXp=>Plugin.Saved.TalentXp;
    public static int EarnedPoints=>Plugin.Catalog==null?0:Plugin.Saved.Earned(Plugin.Catalog);
    public static int TemporaryPointBudget=>TalentState.TemporaryPointBudget;
    public static int SpentPoints=>Plugin.Saved.Spent;
    public static string StateJson=>JsonSerializer.Serialize(Plugin.Saved,TalentCatalog.Json);
    public static string CatalogJson=>JsonSerializer.Serialize(Plugin.Catalog,TalentCatalog.Json);
    public static bool PendingNextRun=>TalentState.TemporaryPointBudget<=0 && MyPlayer.Instance!=null && MyPlayer.Instance.hasStarted;
    public static int Rank(string id)=>Plugin.Saved.Rank(id);
    public static bool Spend(string id){if(Plugin.Catalog==null||!Plugin.Saved.Spend(Plugin.Catalog,id))return false;ApplyDebugNow();Plugin.Save();return true;}
    public static bool Refund(string id){if(Plugin.Catalog==null||!Plugin.Saved.Refund(Plugin.Catalog,id))return false;ApplyDebugNow();Plugin.Save();return true;}
    public static void Reset(){Plugin.Saved.Ranks.Clear();ApplyDebugNow();Plugin.Save();}
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
    void Update(){try{TalentRuntime.Tick();}catch(Exception ex){Plugin.Current.Log.LogError("Mage talent tick failed: "+ex);enabled=false;}}
}
[HarmonyPatch(typeof(AbilityRuntime),nameof(AbilityRuntime.ResetRun))]
static class ResetPatch
{
    static void Prefix(){Plugin.Run=Plugin.Saved.Clone();TalentRuntime.Clear();}
    static void Postfix(){TalentRuntime.AddStartingSpells();TalentBridge.AddDebugCapstoneStarters();}
}
[HarmonyPatch(typeof(MapSelectionUi),nameof(MapSelectionUi.StartMap))]
static class MapSnapshotPatch
{
    // Runs before native inventory/health construction, so maximum health and regeneration
    // are calculated from the newly selected allocation rather than the previous run.
    static void Prefix(){Plugin.Run=Plugin.Saved.Clone();TalentRuntime.Clear();}
}
[HarmonyPatch(typeof(AbilityRuntime),"Damage")]
static class DamagePatch
{
    static void Postfix(LearnedSpell s,ref float __result)
    {if(AbilityRuntime.Active)__result*=1+Plugin.Run.SpellModifier(Plugin.Catalog,s.Definition.Id,s.Definition.School,0);}
}
[HarmonyPatch(typeof(AbilityRuntime),nameof(AbilityRuntime.Cooldown))]
static class CooldownPatch
{
    static void Postfix(LearnedSpell s,ref float __result)
    {if(AbilityRuntime.Active)__result=Mathf.Max(.25f,__result*Mathf.Max(0,1-Plugin.Run.SpellModifier(Plugin.Catalog,s.Definition.Id,s.Definition.School,1)));}
}
[HarmonyPatch(typeof(AbilityRuntime),"Cast")]
static class CastPatch
{
    // Only the argument is replaced. The original spell's rank and upgrade state stay immutable.
    static void Prefix(ref LearnedSpell spell)
    {
        if(!AbilityRuntime.Active)return;
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
            case EStat.MaxHealth:__result*=1+s.Stat(c,2);break;
            case EStat.CritChance:__result+=s.Stat(c,3);break;
            case EStat.CritDamage:__result+=s.Stat(c,4);break;
            // Megabonk's native regeneration value is health per minute.
            case EStat.HealthRegen:__result+=s.Stat(c,6)*60;break;
            case EStat.MoveSpeedMultiplier:__result*=1+s.Stat(c,7);break;
            case EStat.Luck:__result*=1+s.Stat(c,12);break;
        }
    }
}
[HarmonyPatch(typeof(PlayerHealth),"Damage")]
static class IncomingDamagePatch
{
    [HarmonyPriority(Priority.First)]
    static void Prefix(DamageContainer dc){if(AbilityRuntime.Active&&Plugin.Catalog!=null&&dc!=null)dc.damage*=Mathf.Max(0,1-Plugin.Run.Stat(Plugin.Catalog,5));}
}
[HarmonyPatch(typeof(Enemy),nameof(Enemy.EnemyDied),new[]{typeof(DamageContainer)})]
static class KillPatch
{
    static void Prefix(Enemy __instance,DamageContainer dc)=>TalentRuntime.OnDeath(__instance,dc);
}
[HarmonyPatch(typeof(GameManager),"OnDied")]
static class EndLostPatch {static void Prefix()=>TalentRuntime.EndRun(false);}
[HarmonyPatch(typeof(GameManager),nameof(GameManager.OnTeleportAway))]
static class EndWinPatch {static void Prefix()=>TalentRuntime.EndRun(true);}
