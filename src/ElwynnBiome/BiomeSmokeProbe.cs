using System.Reflection;
using System.Text.Json;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Assets.Scripts.Inventory__Items__Pickups;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace AzerothMegabonk.ElwynnBiome;

public sealed class BiomeSmokeProbe : MonoBehaviour
{
    public BiomeSmokeProbe(IntPtr pointer) : base(pointer) { }
    internal static BiomeSmokeProbe Instance;
    internal static Vector3 WishDirection;
    internal static bool Drive;
    int state, maximumEnemies, frame, routeIndex = 1;
    float began, movingAt, nextFrame;
    Vector3 start;
    float capsuleHeight, capsuleRadius;
    AsyncOperation unloading;
    string[] spellIds;
    int[] ranks;
    internal static int? RequestedSeed
    {
        get
        {
            var arg = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("-azeroth-elwynn-seed="));
            return arg != null && int.TryParse(arg.Split('=', 2)[1], out int seed) ? seed : null;
        }
    }
    static Type Named(string type) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(type, false)).First(x => x != null);
    static object Call(string type, string method, params object[] args) => Named(type).GetMethod(method, BindingFlags.Static | BindingFlags.Public).Invoke(null, args);
    static object Hud(string property) => Named("AzerothAbilities.HudBridge").GetProperty(property).GetValue(null);
    void Start() { Instance = this; began = Time.realtimeSinceStartup; Application.runInBackground = true; Application.targetFrameRate = 30; QualitySettings.vSyncCount = 0; }
    void Update()
    {
        if (state == 9) return;
        try
        {
            if (Time.realtimeSinceStartup - began > 90) throw new InvalidOperationException("Elwynn probe timed out at state " + state);
            var player = MyPlayer.Instance; var game = GameManager.Instance;
            if (player == null || player.inventory == null || game == null || MapGenerationController.isGenerating) return;
            if (LevelupScreen.isLevelingUp)
            {
                var button = Object.FindObjectsOfType<UpgradeButton>().FirstOrDefault(x => x.gameObject.activeInHierarchy);
                if (button != null) button.SelectUpgrade(); return;
            }
            player.inventory.playerHealth.hp = player.inventory.playerHealth.maxHp;
            maximumEnemies = Math.Max(maximumEnemies, EnemyManager.Instance?.enemies?.Count ?? 0);
            if (state == 0)
            {
                if (!ElwynnBridge.Ready || !player.hasStarted || !game.IsPlaying()) return;
                using var first = JsonDocument.Parse(ElwynnBridge.Snapshot());
                if (first.RootElement.GetProperty("propInstances").GetInt32() < 30 || !Plugin.Assets.AllAssetsValid) throw new InvalidOperationException("Elwynn source objects are absent");
                var native = first.RootElement.GetProperty("native");
                if (!native.GetProperty("nativeStageTimeline").GetBoolean() || !native.GetProperty("nativeSpecialTiles").GetBoolean() || !native.GetProperty("rngIsolationVerified").GetBoolean())
                    throw new InvalidOperationException("Native generation references or decoration RNG isolation failed: " + native);
                var c = player.playerMovement.GetComponent<CapsuleCollider>(); capsuleHeight = c.height; capsuleRadius = c.radius;
                spellIds = (string[])Hud("SpellIds"); ranks = (int[])Hud("SpellRanks");
                start = player.transform.position; movingAt = Time.realtimeSinceStartup; Drive = true; state = 1;
                Plugin.Note("PROBE native movement begins; input only, original MovementTick/AddForce/collisions retained. " + ElwynnBridge.Snapshot());
            }
            if (state == 1)
            {
                float age = Time.realtimeSinceStartup - movingAt;
                Drive = age < 7;
                var route = BiomeClearance.LastReport.Route;
                while (routeIndex < route.Count - 1 && Horizontal(player.transform.position, new Vector3(route[routeIndex][0], route[routeIndex][1], route[routeIndex][2])) < 3) routeIndex++;
                WishDirection = routeIndex < route.Count ? new Vector3(route[routeIndex][0], 0, route[routeIndex][2]) - new Vector3(player.transform.position.x, 0, player.transform.position.z) : MapGenerationController.tileSpawnDir;
                if (WishDirection.sqrMagnitude < .01f) WishDirection = Vector3.forward;
                WishDirection.Normalize();
                if (Environment.GetCommandLineArgs().Contains("-azeroth-elwynn-record") && age >= 4 && age < 12 && (int)Hud("SourceVfxSpawned") > 0 && Time.realtimeSinceStartup >= nextFrame)
                { nextFrame = Time.realtimeSinceStartup + .125f; Capture("elwynn-video-" + (frame++).ToString("D4") + ".ppm"); }
                if (age < 12 || maximumEnemies < 1 && age < 25) return;
                Drive = false;
                var capsule = player.playerMovement.GetComponent<CapsuleCollider>();
                float moved = Horizontal(start, player.transform.position);
                if (moved < 3 || maximumEnemies < 1 || capsule.height != capsuleHeight || capsule.radius != capsuleRadius) throw new InvalidOperationException("Native movement/spawn not observed: moved=" + moved + ", enemies=" + maximumEnemies);
                Capture("elwynn-first-" + MapGenerationController.mapSeed + ".ppm"); CaptureOverview("elwynn-overview-" + MapGenerationController.mapSeed + ".ppm");
                Plugin.Note("PASS native movement distance=" + moved + "; max native enemies=" + maximumEnemies + "; native capsule unchanged; " + ElwynnBridge.Snapshot());
                unloading = Resources.UnloadUnusedAssets(); state = 5; return;
            }
            if (state == 5)
            {
                if (!unloading.isDone) return;
                if (!Plugin.Assets.AllAssetsValid) throw new InvalidOperationException("Elwynn assets lost after native cleanup");
                Plugin.Note("PASS Elwynn source cache after explicit cleanup: " + Plugin.Assets.DiagnosticsJson());
                VerifyRetained();
                if ((int)Hud("SourceVfxSpawned") < 1) throw new InvalidOperationException("Source Mage spell effects were not exercised");
                // Full portal/final-boss regression uses the established
                // AdventureSmokeRunner independently of this first-biome probe.
                Finish();
            }
        }
        catch (Exception error) { Drive = false; state = 9; Plugin.Note("FAIL ELWYNN " + error); Application.Quit(); }
    }
    static float Horizontal(Vector3 a, Vector3 b) { var d = a - b; d.y = 0; return d.magnitude; }
    [HideFromIl2Cpp]
    void VerifyRetained()
    {
        if (!Plugin.Assets.AllAssetsValid || !(bool)Hud("Active") || InventoryUtility.GetNumAvailableTomeSlots() != 4) throw new InvalidOperationException("Source assets/Mage/books lost after travel");
        var ids = (string[])Hud("SpellIds"); var current = (int[])Hud("SpellRanks");
        for (int i = 0; i < spellIds.Length; i++) { int index = Array.IndexOf(ids, spellIds[i]); if (index < 0 || current[index] < ranks[i]) throw new InvalidOperationException("Source spell rank lost after portal: " + spellIds[i]); }
    }
    [HideFromIl2Cpp]
    static void Capture(string name)
    {
        BiomeCapture.Capture(name);
    }
    [HideFromIl2Cpp]
    static void CaptureOverview(string name)
    {
        var camera = Camera.main; var pos = camera.transform.position; var rotation = camera.transform.rotation;
        try { var p = MyPlayer.Instance.transform.position; camera.transform.position = p + Vector3.up * 25 - camera.transform.forward * 28; camera.transform.LookAt(p + Vector3.up * 3); Capture(name); }
        finally { camera.transform.position = pos; camera.transform.rotation = rotation; }
    }
    [HideFromIl2Cpp]
    void Finish() { Drive = false; state = 9; Plugin.Note("PASS ELWYNN native generated forest, movement/spawn/source assets verified; " + ElwynnBridge.Snapshot()); Application.Quit(); }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.CreateInstances))]
static class BatchElwynnSeedPatch
{
    static bool Prepare() => Plugin.FirstSmoke || Plugin.FeatureSmoke;
    static void Postfix()
    {
        var seed = BiomeSmokeProbe.RequestedSeed;
        if (!seed.HasValue || !MapGenerationController.isGenerating || !NativeBiomeIntegration.IsElwynnStage) return;
        MapGenerationController.mapSeed = seed.Value; Random.InitState(seed.Value);
        Plugin.Note("PROBE fixed native ProceduralMesh seed=" + seed.Value);
    }
}
[HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.MovementTick))]
static class BatchNativeMovementInputPatch
{
    static bool Prepare() => Plugin.FirstSmoke || Plugin.FeatureSmoke;
    static void Prefix(PlayerMovement __instance)
    {
        if (BiomeSmokeProbe.Instance == null) return;
        var input = __instance.inputState;
        input.moveHorizontal = 0; input.moveVertical = BiomeSmokeProbe.Drive ? 1 : 0;
        input.jumping = input.crouching = input.holdingJump = input.holdingWallrun = false;
        if (BiomeSmokeProbe.Drive) input.rotationHorizontal = Mathf.Atan2(BiomeSmokeProbe.WishDirection.x, BiomeSmokeProbe.WishDirection.z) * Mathf.Rad2Deg;
        __instance.SetBetterInput(input);
    }
}
