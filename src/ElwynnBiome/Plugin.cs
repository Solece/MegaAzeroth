using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

[BepInPlugin("local.azerothmegabonk.elwynn", "Azeroth Elwynn Procedural Biome", "0.2.0")]
public sealed class Plugin : BasePlugin
{
    internal static Plugin Current;
    internal static BiomeAssets Assets;
    internal static string Report;
    internal static bool Enabled => Environment.GetCommandLineArgs().Contains("-azeroth-elwynn");
    internal static bool ProbeAllowed => Application.isBatchMode && Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST") == "1";
    internal static bool FirstSmoke => ProbeAllowed && Environment.GetCommandLineArgs().Contains("-azeroth-elwynn-smoke");
    internal static bool FeatureSmoke => ProbeAllowed && Environment.GetCommandLineArgs().Contains("-azeroth-feature-smoke");
    internal static bool TreeSmoke => ProbeAllowed && Environment.GetCommandLineArgs().Contains("-azeroth-talent-tree-smoke");
    internal static bool HeroMenuSmoke => ProbeAllowed && Environment.GetCommandLineArgs().Contains("-azeroth-hero-menu-smoke");
    internal static bool HeroClassSmoke => HeroClassProbe.Armed;
    internal static bool VitalsSmoke => VitalsProbe.Armed;
    internal static bool VfxFidelitySmoke => VfxFidelityProbe.Armed;
    internal static bool TargetingSmoke => TargetingProbe.Armed;
    internal static bool GhostSmoke => GhostProbe.Armed;
    internal static bool Smoke => FirstSmoke || FeatureSmoke || TreeSmoke || HeroMenuSmoke || HeroClassSmoke || VitalsSmoke || VfxFidelitySmoke || TargetingSmoke || GhostSmoke;
    Harmony harmony;
    public override void Load()
    {
        Current = this;
        if (!Enabled) return;
        var folder = Path.GetFullPath(Path.Combine(Paths.GameRootPath, "../reports/elwynn"));
        Directory.CreateDirectory(folder);
        Report = Path.Combine(folder, "biome-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Environment.ProcessId + ".log");
        ClassInjector.RegisterTypeInIl2Cpp<ElwynnPropMarker>();
        harmony = new Harmony("local.azerothmegabonk.elwynn");
        harmony.PatchAll(typeof(Plugin).Assembly);
        AddComponent<BiomeRuntime>();
        if (FirstSmoke) AddComponent<BiomeSmokeProbe>();
        if (FeatureSmoke) AddComponent<FeatureProbe>();
        if (TreeSmoke) AddComponent<TalentTreeProbe>();
        if (HeroMenuSmoke) AddComponent<HeroMenuProbe>();
        if (HeroClassSmoke) AddComponent<HeroClassProbe>();
        if (VitalsSmoke) AddComponent<VitalsProbe>();
        if (VfxFidelitySmoke) AddComponent<VfxFidelityProbe>();
        if (TargetingSmoke) AddComponent<TargetingProbe>();
        if (GhostSmoke) AddComponent<GhostProbe>();
        Note("Elwynn v2 all-stage palettes and original buildings armed; native generation, movement, spawn and progression retained. Probe=" + Smoke);
    }
    internal static BiomeAssets EnsureAssets()
    {
        if (Assets != null && Assets.AllAssetsValid) return Assets;
        Assets?.Release();
        Assets = BiomeAssets.Load(Path.Combine(Paths.GameRootPath, "AzerothElwynnAssets"));
        Note("Original Elwynn assets loaded: " + Assets.DiagnosticsJson());
        return Assets;
    }
    internal static void Note(string message)
    {
        Current?.Log.LogInfo(message);
        if (Report != null) File.AppendAllText(Report, DateTimeOffset.Now + " " + message + Environment.NewLine);
    }
    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        NativeBiomeIntegration.ReleaseVariants();
        Assets?.Release(); Assets = null;
        return true;
    }
}

public sealed class BiomeRuntime : MonoBehaviour
{
    public BiomeRuntime(IntPtr pointer) : base(pointer) { }
    internal static int AppliedScene = -1;
    internal static bool ClearanceSucceeded;
    float readyAt;
    int observedScene = -1;
    void Update()
    {
        if (!NativeBiomeIntegration.IsElwynnStage || MapGenerationController.isGenerating) return;
        var player = MyPlayer.Instance;
        if (player == null || !player.hasStarted || player.inventory == null) return;
        int scene = SceneManager.GetActiveScene().handle;
        if (AppliedScene == scene) return;
        if (observedScene != scene) { observedScene = scene; readyAt = Time.realtimeSinceStartup + .25f; return; }
        if (Time.realtimeSinceStartup < readyAt) return;
        try
        {
            var generator = Object.FindObjectOfType<MapGenerationController>();
            if (generator == null) return;
            NativeBiomeIntegration.ApplyFinalTerrainBindings(generator);
            var result = BiomeClearance.Apply(generator);
            if (result.ReservedViolationsRemaining != 0 || result.NativeInteractablesBefore != result.NativeInteractablesAfter || !result.AddedCollisionsCannotBlockNativeRoute || result.Buildings.DoorViolationsRemaining != 0)
                throw new InvalidOperationException("Elwynn decoration clearance failed: " + BiomeClearance.DiagnosticsJson);
            AppliedScene = scene; ClearanceSucceeded = true;
            Plugin.Note("Elwynn native generation completed: " + ElwynnBridge.Snapshot());
        }
        catch (Exception error)
        {
            AppliedScene = scene; ClearanceSucceeded = false;
            foreach (var marker in Object.FindObjectsOfType<ElwynnPropMarker>())
                if (marker.gameObject.activeInHierarchy)
                { var collider = marker.GetComponent<Collider>(); if (collider != null) collider.enabled = false; }
            Plugin.Note("ERROR Elwynn post-generation clearance: " + error);
        }
    }
}

public static class ElwynnBridge
{
    public static bool Ready => BiomeRuntime.ClearanceSucceeded && NativeBiomeIntegration.IsElwynnStage && BiomeRuntime.AppliedScene == SceneManager.GetActiveScene().handle;
    public static string Snapshot()
    {
        var generator = Object.FindObjectOfType<MapGenerationController>();
        var roots = Object.FindObjectsOfType<ElwynnPropMarker>().Where(x => x != null && x.gameObject.activeInHierarchy && x.gameObject.scene.IsValid()).ToArray();
        var rows = roots.Select(x => x.name + "@" + Vec(x.transform.position) + "/" + Vec(x.transform.localScale)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Mesh mesh = null;
        if (generator?.proceduralMeshRenderer != null) mesh = generator.proceduralMeshRenderer.GetComponent<MeshFilter>()?.sharedMesh;
        string terrain = "";
        if (mesh != null)
        {
            var vertices = mesh.vertices;
            terrain = mesh.vertexCount + ":" + string.Join(";", Enumerable.Range(0, Math.Min(vertices.Length, 96)).Select(i => Vec(vertices[i * Math.Max(1, (vertices.Length - 1) / 96)])));
        }
        using var integration = JsonDocument.Parse(NativeBiomeIntegration.Describe());
        using var assets = JsonDocument.Parse(Plugin.Assets?.DiagnosticsJson() ?? "{}");
        using var clearance = JsonDocument.Parse(BiomeClearance.DiagnosticsJson);
        return JsonSerializer.Serialize(new { ready = Ready, scene = SceneManager.GetActiveScene().name, stage = MapController.GetStageIndex(), seed = MapGenerationController.mapSeed,
            native = integration.RootElement.Clone(), assets = assets.RootElement.Clone(), clearance = clearance.RootElement.Clone(),
            propInstances = roots.Length, layoutHash = Hash(string.Join("\n", rows)), terrainHash = Hash(terrain), terrainVertexCount = mesh?.vertexCount ?? 0, terrainShader = GroundShaderSettings() });
    }
    static object GroundShaderSettings()
    {
        try
        {
            var material = MapController.currentStage?.triplanarMaterial;
            if (material == null || material.shader == null) return new { ready = false };
            var shader = material.shader;
            var values = new List<object>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i), type = shader.GetPropertyType(i).ToString();
                if (type is "Float" or "Range") values.Add(new { name, type, value = material.GetFloat(name) });
                else if (type == "Vector") { var v = material.GetVector(name); values.Add(new { name, type, value = new[] { v.x, v.y, v.z, v.w } }); }
                else if (type == "Color") { var c = material.GetColor(name); values.Add(new { name, type, value = new[] { c.r, c.g, c.b, c.a } }); }
            }
            return new { ready = true, shader = shader.name, values };
        }
        catch (Exception error) { return new { ready = false, error = error.Message }; }
    }
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static string Vec(Vector3 value) => value.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + value.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + value.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
