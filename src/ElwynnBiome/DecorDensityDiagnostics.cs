using System.Text.Json;
using Assets.Scripts.Game.MapGeneration;
using Assets.Scripts.Managers;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Observation only: never call GetAmount here, because doing so would consume
// an additional native random draw. Its actual return is observed below.
public static class DecorDensityDiagnostics
{
    static readonly DecorDensitySettings settings = DecorDensityPolicy.Parse(Environment.GetCommandLineArgs());
    static readonly List<GroupSample> groups = new();
    static GroupSample current;
    static int observationFailures;

    internal sealed class GroupSample
    {
        internal IntPtr Pointer;
        internal GroupSample Previous;
        public int Index { get; set; }
        public string[] Prefabs { get; set; }
        public int NativeMinimum { get; set; }
        public int NativeMaximum { get; set; }
        public float NativeMultiplier { get; set; }
        public float EffectiveMultiplier { get; set; }
        public int AmountObservations { get; set; }
        public int? NativeSelectedAmount { get; set; }
        public int? BaselineRequested { get; set; }
        public int? Requested { get; set; }
        public int? Placed { get; set; }
        public int? PlacementRejected { get; set; }
        public bool Completed { get; set; }
        public string Error { get; set; }
        internal int Before;
    }

    internal static void Reset()
    { groups.Clear(); current = null; observationFailures = 0; }

    internal static GroupSample BeginGroup(RandomMapObject group, int index, ref float multiplier)
    {
        var sample = new GroupSample
        {
            Pointer = group.Pointer, Previous = current, Index = index,
            NativeMinimum = group.amount, NativeMaximum = group.maxAmount,
            NativeMultiplier = multiplier, EffectiveMultiplier = multiplier * settings.Multiplier,
            Prefabs = group.prefabs?.Where(x => x != null).Select(x => x.name).ToArray() ?? Array.Empty<string>()
        };
        multiplier = sample.EffectiveMultiplier;
        groups.Add(sample); current = sample;
        try { sample.Before = CountActiveRoots(); }
        catch (Exception error) { observationFailures++; sample.Error = "Before count: " + error.Message; }
        return sample;
    }

    internal static void ObserveAmount(RandomMapObject group, int selected)
    {
        if (current == null || group == null || current.Pointer != group.Pointer) return;
        current.AmountObservations++;
        current.NativeSelectedAmount = selected;
        // Verified native RVA 49C72E..49C745: GetAmount -> multiply ->
        // native RoundToInt. Do not infer a selected value from configured max.
        current.BaselineRequested = Mathf.RoundToInt(selected * current.NativeMultiplier);
        current.Requested = Mathf.RoundToInt(selected * current.EffectiveMultiplier);
    }

    internal static void EndGroup(GroupSample sample, Exception nativeError)
    {
        if (sample == null) return;
        try
        {
            if (nativeError != null) sample.Error = "Native placement: " + nativeError.GetType().Name + ": " + nativeError.Message;
            if (sample.Error == null)
            {
                sample.Placed = CountActiveRoots() - sample.Before;
                if (sample.AmountObservations == 1 && sample.Requested.HasValue && sample.Placed >= 0 && sample.Placed <= sample.Requested)
                    sample.PlacementRejected = sample.Requested.Value - sample.Placed.Value;
                else sample.Error = "Native amount/count observation is incomplete; rejected count is not inferred";
            }
            sample.Completed = nativeError == null;
        }
        catch (Exception error) { observationFailures++; sample.Error = "After count: " + error.Message; }
        finally { current = sample.Previous; }
    }

    static int CountActiveRoots() => Object.FindObjectsOfType<ElwynnPropMarker>().Count(x =>
        x != null && x.gameObject.scene.IsValid() && x.gameObject.scene.handle == SceneManager.GetActiveScene().handle &&
        x.gameObject.activeInHierarchy && x.name.StartsWith("AzerothElwynn_", StringComparison.Ordinal));

    public static string SnapshotJson => JsonSerializer.Serialize(Snapshot());
    internal static object Snapshot()
    {
        int active = CountActiveRoots();
        bool complete = groups.Count > 0 && groups.All(x => x.Completed && x.Error == null && x.PlacementRejected.HasValue);
        int placed = groups.Sum(x => x.Placed ?? 0);
        bool clearanceApplied = BiomeRuntime.ClearanceSucceeded && BiomeRuntime.AppliedScene == SceneManager.GetActiveScene().handle;
        var clearance = clearanceApplied ? BiomeClearance.LastReport : null;
        return new
        {
            settings.Active, settings.TestMode, settings.Multiplier, settings.RequestedValues, settings.Reason,
            Scope = "Source Elwynn decor and obstacle groups only; native ground and interactables excluded",
            NativeMapSeed = MapGenerationController.mapSeed,
            GroupsObserved = groups.Count, CountsComplete = complete, ObservationFailures = observationFailures,
            BaselineRequested = complete ? (int?)groups.Sum(x => x.BaselineRequested ?? 0) : null,
            Requested = complete ? (int?)groups.Sum(x => x.Requested ?? 0) : null,
            NativePlaced = complete ? (int?)placed : null,
            PlacementRejected = complete ? (int?)groups.Sum(x => x.PlacementRejected ?? 0) : null,
            ActiveSceneSourceRoots = active,
            ClearanceApplied = clearanceApplied,
            ClearanceRemoved = complete && clearanceApplied ? (int?)Math.Max(0, placed - active) : null,
            ClearanceRemovedBySource = clearance?.RemovedBySource,
            BuildingRejectedTerrain = clearance?.Buildings.RejectedTerrain,
            BuildingRejectedDoors = clearance?.Buildings.RejectedDoors,
            BuildingRejectedOverlap = clearance?.Buildings.RejectedOverlap,
            SolidDecorCollidersDisabled = clearance?.SolidDecorCollidersDisabled,
            RequestedIncreasePolicy = "Native integer rounding and original placement/clearance rejection remain authoritative; final survivors need not be exactly 20% higher",
            Groups = groups
        };
    }
}

// Unique installed native entry RVA 0x4810F0, verified against script.json.
// The existing source-group scope ensures native terrain/interactables and
// all callers outside that scope are unobserved and unmodified.
[HarmonyPatch(typeof(RandomMapObject), nameof(RandomMapObject.GetAmount))]
static class ElwynnDecorAmountObservationPatch
{
    static void Postfix(RandomMapObject __instance, int __result) => DecorDensityDiagnostics.ObserveAmount(__instance, __result);
}
