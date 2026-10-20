using System.Reflection;
using System.Text.Json;
using AzerothAbilities;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Camera;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Managers;
using Assets.Scripts.Saves___Serialization.Progression;
using Assets.Scripts.Saves___Serialization.Progression.Achievements;
using Assets.Scripts.Saves___Serialization.Progression.Unlocks;
using Assets.Scripts._Data.MapsAndStages;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.AdventureFlow;

[BepInPlugin(Id,"Azeroth Native Adventure Flow","0.1.0")]
[BepInDependency("local.azerothmegabonk.abilities")]
public sealed class Plugin:BasePlugin
{
    public const string Id="local.azerothmegabonk.adventureflow";
    internal static Plugin Current;
    internal static bool TestBattle,Smoke;
    internal static string Report;
    Harmony harmony;
    public override void Load()
    {
        Current=this;
        var args=Environment.GetCommandLineArgs();TestBattle=args.Contains("-azeroth-test-battle");
        Smoke=args.Contains("-azeroth-adventure-smoke") && Application.isBatchMode && Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1";
        Report=Path.GetFullPath(Path.Combine(Paths.GameRootPath,"../reports",Smoke?"adventure-smoke.log":"adventure-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log"));
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        File.WriteAllText(Report,DateTimeOffset.Now+" Native adventure enabled; map/tier visibility is temporary; no fake completion records.\n");
        harmony=new Harmony(Id);harmony.PatchAll(typeof(Plugin).Assembly);
        AddComponent<NativeMapNavigation>();
        if(Smoke){AddComponent<AdventureSmokeRunner>();Log.LogInfo("Background adventure probe armed.");}
        Log.LogInfo("All native maps and tiers available in lab; test battle uses the full native stage route through FinalBossMap.");
    }
    public override bool Unload(){harmony?.UnpatchSelf();return true;}
    internal static void Note(string text){File.AppendAllText(Report,DateTimeOffset.Now+" "+text+"\n");Current.Log.LogInfo(text);}
    internal static int MaxTier(MapData map)=>Math.Max(0,(map?.stages?.Length??1)-1);
    internal static bool IsMap(UnlockableBase unlockable)=>unlockable!=null && unlockable.TryCast<MapData>()!=null;
}

// Restrict unlock overrides to maps. Characters, items, achievements and completion
// records are left to their normal native progression logic.
[HarmonyPatch]
static class MapUnlockedPatch
{
    static MethodBase TargetMethod()=>AccessTools.Method(typeof(MyAchievements),nameof(MyAchievements.IsUnlocked),new[]{typeof(UnlockableBase),typeof(string).MakeByRefType()});
    static bool Prefix(UnlockableBase unlockable,ref string requirementsString,ref bool __result)
    {if(!Plugin.IsMap(unlockable))return true;requirementsString="";__result=true;return false;}
}
[HarmonyPatch]
static class MapAvailabilityPatch
{
    static IEnumerable<MethodBase> TargetMethods()=>new[]{"IsPurchased","IsAvailable","IsActivated"}.Select(n=>AccessTools.Method(typeof(MyAchievements),n,new[]{typeof(UnlockableBase)}));
    static bool Prefix(UnlockableBase unlockable,ref bool __result)
    {if(!Plugin.IsMap(unlockable))return true;__result=true;return false;}
}
[HarmonyPatch(typeof(MenuMeta),nameof(MenuMeta.GetHighestCompletedTier))]
static class TierVisibilityPatch
{
    // The menu asks for the greatest completed tier to decide which tier buttons
    // can be selected. Returning a temporary value avoids writing completedTiers.
    static void Postfix(EMap map,ref int __result)
    {var data=DataManager.Instance?.GetMap(map);if(data!=null)__result=Math.Max(__result,Plugin.MaxTier(data));}
}
[HarmonyPatch(typeof(MapSelectionUi),nameof(MapSelectionUi.StartMap))]
static class FullTestRoutePatch
{
    static void Prefix(MapSelectionUi __instance)
    {
        if(!Plugin.TestBattle || __instance.runConfig?.mapData==null)return;
        var config=__instance.runConfig;config.mapTierIndex=Plugin.MaxTier(config.mapData);
        Plugin.Note($"Full test route selected: map={config.mapData.name},nativeTierIndex={config.mapTierIndex},stages={config.mapData.stages.Length}; native stage boss/portal/final gate retained.");
    }
}
[HarmonyPatch(typeof(MapController),nameof(MapController.LoadNextStage))]
static class NextStagePatch
{
    static bool Prefix()
    {
        // A native ordinary portal should not occur on the last stage. Protect
        // an accidental duplicate/stale portal from indexing past stages[].
        if(MapController.IsFinalBossStage())return false;
        Plugin.Note("Native portal transition requested: "+AdventureBridge.Describe());
        if(!MapController.IsLastStage())return true;
        Plugin.Note("Last-stage ordinary portal redirected to native final arena loader.");
        MapController.LoadFinalStage();return false;
    }
}
[HarmonyPatch(typeof(MapController),nameof(MapController.LoadFinalStage))]
static class FinalStagePatch
{
    static bool Prefix(){if(MapController.IsFinalBossStage())return false;Plugin.Note("Native final arena transition requested: "+AdventureBridge.Describe());return true;}
}
[HarmonyPatch(typeof(InteractableBossSpawner),"OnEnemyReleasedFromPool")]
static class BossPortalPatch
{
    static void Prefix(InteractableBossSpawner __instance,Enemy enemy,ref bool __state){__state=__instance.bossEnemies!=null && __instance.bossEnemies.Contains(enemy);}
    static void Postfix(InteractableBossSpawner __instance,bool __state)
    {
        if(!__state || __instance.bossEnemies==null || __instance.bossEnemies.Count!=0 || __instance.numBossesDefeated<=0)return;
        var canAdvance=!MapController.IsLastStage() && MapController.GetStageIndex()<(MapController.runConfig?.mapTierIndex??0);
        if(canAdvance && __instance.portal!=null && !__instance.portal.activeSelf)
        {__instance.portal.SetActive(true);Plugin.Note("Restored native portal after all summoned bosses released.");}
        Plugin.Note($"Stage bosses defeated={__instance.numBossesDefeated}; canAdvance={canAdvance}; portalActive={__instance.portal!=null && __instance.portal.activeInHierarchy}; "+AdventureBridge.Describe());
    }
}

// Native interaction can be raised before the minimap has had its first Update
// after scene generation. Its callback dereferences bossSpawner unconditionally.
[HarmonyPatch(typeof(MinimapCamera),"OnBossSpawnerInteract")]
static class MinimapBossReferencePatch
{
    static bool Prefix(MinimapCamera __instance)
    {
        if(__instance.bossSpawner==null)__instance.TryFindBossSpawner();
        return __instance.bossSpawner!=null;
    }
}
public static class AdventureBridge
{
    static readonly HashSet<(int Stage,IntPtr Pointer,uint Id)> probeKilled=new();
    static bool ProbeAllowed=>Application.isBatchMode && Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1";
    public static string Describe()
    {
        var map=MapController.currentMap;var p=MyPlayer.Instance;
        return JsonSerializer.Serialize(new{scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,map=map?.name,stage=MapController.currentStage?.name,stageIndex=MapController.GetStageIndex(),tierIndex=MapController.runConfig?.mapTierIndex,stages=map?.stages?.Length,finalArena=MapController.IsFinalBossStage(),inventory=p?.inventory?.Pointer.ToInt64(),mage=HudBridge.Active,classId=HudBridge.ClassId,spells=HudBridge.SpellIds,ranks=HudBridge.SpellRanks,books=InventoryUtility.GetNumAvailableTomeSlots(),portals=Object.FindObjectsOfType<InteractablePortal>().Count(x=>x.gameObject.activeInHierarchy),finalGates=Object.FindObjectsOfType<InteractableBossSpawnerFinal>().Count(x=>x.gameObject.activeInHierarchy),finalBosses=Object.FindObjectsOfType<Enemy>().Count(x=>x.IsFinalBoss() && !x.IsDeadOrDyingNextFrame())});
    }
    public static string DescribeMaps()
    {
        var data=DataManager.Instance;if(data==null)return "[]";
        var maps=new List<object>();foreach(var m in data.maps)maps.Add(new{name=m.name,id=m.eMap.ToString(),stages=m.stages.Length,maxTierIndex=Plugin.MaxTier(m),unlocked=MyAchievements.IsUnlocked(m,out var _),available=MyAchievements.IsAvailable(m)});return JsonSerializer.Serialize(maps);
    }
    public static bool SpawnStageBossForProbe()
    {
        if(!ProbeAllowed)return false;
        var spawner=Object.FindObjectsOfType<InteractableBossSpawner>().FirstOrDefault(x=>x.gameObject.activeInHierarchy && (x.bossEnemies==null || x.bossEnemies.Count==0) && x.numBossesDefeated==0);
        if(spawner==null)return false;
        MyPlayer.Instance?.minimapCameraScript?.TryFindBossSpawner();
        var ok=spawner.Interact();Plugin.Note("Probe native boss spawner Interact="+ok);return ok;
    }
    public static int DefeatStageBossForProbe()
    {
        if(!ProbeAllowed)return 0;
        var bosses=Object.FindObjectsOfType<Enemy>().Where(e=>e.IsStageBoss() && !e.IsDeadOrDyingNextFrame() && probeKilled.Add((MapController.GetStageIndex(),e.Pointer,e.id))).ToArray();
        foreach(var e in bosses){Plugin.Note($"Probe native Kill: {e.enemyData?.name}; flagsStage={e.IsStageBoss()},flagsFinal={e.IsFinalBoss()},hp={e.hp}/{e.maxHp},height={e.GetHeight()}");e.MakeInvulnerable(false);e.Kill("AzerothLabAdventureProbe");}
        return bosses.Length;
    }
    public static bool EnterPortalForProbe()
    {
        if(!ProbeAllowed)return false;
        var p=Object.FindObjectsOfType<InteractablePortal>().FirstOrDefault(x=>x.gameObject.activeInHierarchy && !x.done);
        if(p!=null){Plugin.Note("Probe Interact native stage portal.");return p.Interact();}
        var gate=Object.FindObjectsOfType<InteractableBossSpawnerFinal>().FirstOrDefault(x=>x.gameObject.activeInHierarchy && !x.done);
        if(gate==null)return false;
        Plugin.Note("Probe Interact native final arena gate.");return gate.Interact();
    }
}

public sealed class AdventureSmokeRunner:MonoBehaviour
{
    public AdventureSmokeRunner(IntPtr pointer):base(pointer){}
    float began,next,deadline;int state,fromStage=-1,stagesVerified,bossesKilled,errors,upgradeProbeAttempts;bool seeded;
    string[] expectedIds;int[] expectedRanks;long expectedInventory;string sourceClassId;int expectedTalentSpent;
    void Start(){began=Time.realtimeSinceStartup;next=began+1;Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;}
    void Update()
    {
        if(state==9 || Time.realtimeSinceStartup<next)return;next=Time.realtimeSinceStartup+1;
        try
        {
            if(Time.realtimeSinceStartup-began>240)throw new InvalidOperationException("Adventure probe timed out at state="+state+" "+AdventureBridge.Describe());
            var player=MyPlayer.Instance;var gm=GameManager.Instance;
            if(player==null || player.inventory==null || gm==null || MapGenerationController.isGenerating)return;
            if(!player.hasStarted){if(state==0)return;player.hasStarted=true;Plugin.Note("Probe native player start resumed after stage generation.");}
            if(gm.isGameOver)throw new InvalidOperationException("Native game ended before final arena");
            if(LevelupScreen.isLevelingUp)
            {
                var choice=Object.FindObjectsOfType<UpgradeButton>().Where(b=>b.gameObject.activeInHierarchy)
                    .OrderByDescending(b=>!b.isItem && b.upgradable!=null && b.upgradable.TryCast<WeaponData>()!=null).FirstOrDefault();
                if(choice!=null)choice.SelectUpgrade();return;
            }
            if(!gm.IsPlaying()){if(state==0)return;gm.StartPlaying();next=Time.realtimeSinceStartup+2;return;}
            // Test-only survivability; native boss spawning, kill events, portal
            // activation and scene generation still execute unmodified.
            var health=player.inventory.playerHealth;health.hp=health.maxHp;

            if(!VisualTransitionProbe.StageReady())return;
            if(state==0)
            {
                if(!seeded)
                {
                    sourceClassId=HudBridge.ClassId;
                    if(!ClassAbilityDiagnostics.LearnAllForProbe())throw new InvalidOperationException("Cannot learn original current-class catalog for transition probe");
                    var sourceCatalog=VisualTransitionProbe.ActiveCatalog;
                    var seed=sourceCatalog.Spells.FirstOrDefault(s=>s.AllowedUpgrades.Contains(0))??sourceCatalog.Spells.FirstOrDefault(s=>s.AllowedUpgrades.Length>0);
                    if(seed==null || !HudBridge.ApplyUpgradeForProbe(seed.Id,seed.AllowedUpgrades.Contains(0)?0:seed.AllowedUpgrades[0],2))throw new InvalidOperationException("Cannot seed original current-class learned-rank transition probe");
                    expectedIds=HudBridge.SpellIds;expectedRanks=HudBridge.SpellRanks;expectedInventory=player.inventory.Pointer.ToInt64();seeded=true;
                    var talentType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.TalentBridge",false)).FirstOrDefault(x=>x!=null);
                    if(talentType==null)throw new InvalidOperationException("Original class talent bridge absent");
                    expectedTalentSpent=(int)talentType.GetProperty("SpentPoints").GetValue(null);
                    Plugin.Note("Probe source class="+sourceClassId+"; catalogSpells="+sourceCatalog.Spells.Count+"; learned="+expectedIds.Length+"; upgraded="+seed.Id+"; talentSpent="+expectedTalentSpent);
                    Plugin.Note("Probe map availability: "+AdventureBridge.DescribeMaps());
                }
                if(VisualTransitionProbe.Enabled)
                {
                    using var weaponPolicy=JsonDocument.Parse(NativeWeaponDiagnostics.SnapshotJson);
                    if(weaponPolicy.RootElement.GetProperty("SourceCardsSelected").GetInt32()==0)
                    {
                        if(++upgradeProbeAttempts>8)throw new InvalidOperationException("Native levelup did not offer/select an Azeroth source spell");
                        player.inventory.AddLevel();Plugin.Note("Probe real native levelup requested; prefer a source-spell card; attempt="+upgradeProbeAttempts);return;
                    }
                }
                Verify();fromStage=MapController.GetStageIndex();state=1;deadline=Time.realtimeSinceStartup+15;
            }
            if(MapController.IsFinalBossStage())
            {
                Verify();var final=Object.FindObjectsOfType<Enemy>().FirstOrDefault(e=>e.IsFinalBoss() && !e.IsDeadOrDyingNextFrame());
                if(final==null){if(deadline==0)deadline=Time.realtimeSinceStartup+45;if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Final arena loaded but no native final boss spawned");return;}
                if(!VisualTransitionProbe.BossReady(final))return;
                Plugin.Note($"PASS: native stages verified={stagesVerified}; stage bosses killed={bossesKilled}; FinalBossMap loaded, live final boss={final.enemyData?.name} hp={final.hp}/{final.maxHp}; {sourceClassId} learned={expectedIds.Length} spell ranks/talents/books retained; no OS input.");
                state=9;Application.Quit();return;
            }
            if(state==1)
            {
                // The last tier stage contains the native final-arena gate.
                if(MapController.IsTierFinalStage() || MapController.IsLastStage())
                {if(!AdventureBridge.EnterPortalForProbe()){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Native final gate not available");return;}state=4;deadline=Time.realtimeSinceStartup+45;return;}
                if(!AdventureBridge.SpawnStageBossForProbe()){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Native stage boss spawner unavailable");return;}state=2;deadline=Time.realtimeSinceStartup+15;return;
            }
            if(state==2)
            {
                var visualBoss=Object.FindObjectsOfType<Enemy>().FirstOrDefault(e=>e.IsStageBoss()&&!e.IsDeadOrDyingNextFrame());
                if(VisualTransitionProbe.Enabled && visualBoss!=null && !VisualTransitionProbe.BossReady(visualBoss))return;
                int killed=AdventureBridge.DefeatStageBossForProbe();if(killed>0)bossesKilled+=killed;
                var ready=Object.FindObjectsOfType<InteractablePortal>().Any(x=>x.gameObject.activeInHierarchy && !x.done);
                if(!ready){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Boss defeat did not activate native stage portal");return;}
                if(!AdventureBridge.EnterPortalForProbe())throw new InvalidOperationException("Native stage portal refused interaction");state=4;deadline=Time.realtimeSinceStartup+45;return;
            }
            if(state==4)
            {
                if(MapController.GetStageIndex()==fromStage){if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Portal did not advance to next native stage");return;}
                if(VisualTransitionProbe.Enabled)
                {
                    using var weaponPolicy=JsonDocument.Parse(NativeWeaponDiagnostics.SnapshotJson);
                    if(weaponPolicy.RootElement.GetProperty("SourceCardsSelected").GetInt32()==0)
                    {
                        if(++upgradeProbeAttempts>8)throw new InvalidOperationException("Native levelup did not offer/select an Azeroth source spell");
                        player.inventory.AddLevel();Plugin.Note("Probe real native levelup requested; prefer a source-spell card; attempt="+upgradeProbeAttempts);return;
                    }
                }
                Verify();fromStage=MapController.GetStageIndex();state=1;deadline=Time.realtimeSinceStartup+15;
            }
        }
        catch(Exception ex){Plugin.Note("ERROR adventure state="+state+" "+ex);if(++errors>=1){Plugin.Note("FAIL adventure progression");state=9;Application.Quit();}}
    }
    [HideFromIl2Cpp]
    void Verify()
    {
        if(!HudBridge.Active || HudBridge.ClassId!=sourceClassId || InventoryUtility.GetNumAvailableTomeSlots()!=4)throw new InvalidOperationException("Original source class/four tome slots lost across native stage");
        if(HudBridge.SpellIds.Length<expectedIds.Length)throw new InvalidOperationException("Original class learned-spell count decreased across native stage");
        for(int i=0;i<expectedIds.Length;i++)
        {int j=Array.IndexOf(HudBridge.SpellIds,expectedIds[i]);if(j<0 || HudBridge.SpellRanks[j]<expectedRanks[i])throw new InvalidOperationException("Learned spell/rank lost: "+expectedIds[i]);}
        var talent=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.TalentBridge",false)).FirstOrDefault(x=>x!=null);
        int spent=talent==null?0:(int)talent.GetProperty("SpentPoints").GetValue(null);
        if(Environment.GetCommandLineArgs().Contains("-azeroth-talents-max") && spent!=expectedTalentSpent)throw new InvalidOperationException("Maxed original class talent allocation lost across stage: "+spent+" vs "+expectedTalentSpent);
        stagesVerified++;Plugin.Note("PASS native stage ready: "+AdventureBridge.Describe()+$"; originalInventory={expectedInventory}; talentSpent={spent}");
    }
}
