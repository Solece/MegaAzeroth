using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// The component identifies source decoration only. Unity does not serialize a
// managed string field on an injected component when a native prefab is cloned,
// so discovery always verifies the root name as well as the scene instance.
public sealed class ElwynnPropMarker : MonoBehaviour
{
    public ElwynnPropMarker(IntPtr pointer) : base(pointer) { }
    public string SourceId;
}

public sealed class ClearanceReport
{
    public string Scene { get; set; }
    public int NativeMapSeed { get; set; }
    public int GroundMask { get; set; }
    public int ObstacleMask { get; set; }
    public int NativeWhatIsGroundMask { get; set; }
    public int NativeGroundAndObjectsMask { get; set; }
    public int NativeMovementGroundOnlyMask { get; set; }
    public string GroundMaskSource { get; set; }
    public string NativeColliderType { get; set; }
    public float NativeCapsuleLocalRadius { get; set; }
    public float NativeCapsuleLocalHeight { get; set; }
    public int NativeCapsuleDirection { get; set; }
    public float[] NativeCapsuleAxis { get; set; }
    public float[] NativeCapsuleScale { get; set; }
    public float[] NativeColliderBoundsSize { get; set; }
    public float[] NativeColliderBoundsFeet { get; set; }
    public float NativePlayerRadius { get; set; }
    public float ProbeCapsuleRadius { get; set; }
    public float ProbeCapsuleHeight { get; set; }
    public float GridSpacing { get; set; }
    public int GridNodes { get; set; }
    public int WalkableNodes { get; set; }
    public int NodesVisited { get; set; }
    public bool RouteBudgetExceeded { get; set; }
    public int PropsExamined { get; set; }
    public int PropsRemaining { get; set; }
    public int RemovedReserved { get; set; }
    public int RemovedRail { get; set; }
    public int RemovedCorridor { get; set; }
    public int SolidDecorCollidersDisabled { get; set; }
    public int ReservedZones { get; set; }
    public int ReservedViolationsRemaining { get; set; }
    public int NativeInteractablesBefore { get; set; }
    public int NativeInteractablesAfter { get; set; }
    public int RailCount { get; set; }
    public string Target { get; set; }
    public float[] Spawn { get; set; }
    public float[] UnprojectedSpawn { get; set; }
    public bool SpawnProjectedOntoNativeFloor { get; set; }
    public float SpawnFloorProjectionDistance { get; set; }
    public float[] NativeTileSpawn { get; set; }
    public bool NativeTileSpawnApplicable { get; set; }
    public float[] Gate { get; set; }
    public float[] RouteEnd { get; set; }
    public float EndDistanceFromGate { get; set; }
    public float RouteLength { get; set; }
    public int RoutePoints { get; set; }
    public bool NativeRouteVerified { get; set; }
    public bool DecoratedRouteClear { get; set; }
    public bool AddedCollisionsCannotBlockNativeRoute { get; set; }
    public bool NativeGeometryUntouched { get; set; } = true;
    public string Reason { get; set; }
    public string FailureDetails { get; set; }
    public string CapsuleCastApi { get; set; } = "PhysicsScene.Internal_CapsuleCastNonAlloc_Injected: pinned concrete RaycastHit buffer";
    public long ElapsedMilliseconds { get; set; }
    public List<float[]> Route { get; set; } = new();
    public Dictionary<string, int> RemovedBySource { get; set; } = new();
    public BuildingSafetyReport Buildings { get; set; } = new();
}

// Runs once after native generation and interactable placement. Its only writes
// affect new AzerothElwynn_ decoration instances and their own colliders.
public static class BiomeClearance
{
    const string Prefix = "AzerothElwynn_";
    const float WalkableNormalY = .6427876f; // cos(50 degrees), conservative native surface probe.
    const float SlopeTangent = 1.191754f;
    const float GroundSkin = .16f;
    static ClearanceReport last = new() { Reason = "Not applied" };
    static CapsuleQueryBuffer activeCastQueries;
    static BuildingSafety activeBuildings;
    internal static string LastObstacleDescription { get; private set; }
    public static string DiagnosticsJson => JsonSerializer.Serialize(last);
    public static ClearanceReport LastReport => last;

    sealed class Prop
    {
        internal GameObject Root;
        internal List<Collider> Colliders = new();
        internal string Source;
        internal bool Removed;
    }
    readonly record struct Zone(Vector3 Center, float Radius, Bounds Bounds, bool IsBounds, bool Rail);
    readonly record struct Node(Vector3 Feet, bool Valid);
    internal readonly record struct Body(float Radius, float Height);

    [StructLayout(LayoutKind.Sequential)]
    struct QuerySpan
    {
        internal IntPtr Begin;
        internal int Length;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int CapsuleCastNonAllocNative(ref PhysicsScene scene, ref Vector3 lower, ref Vector3 upper, float radius,
        ref Vector3 direction, ref QuerySpan hits, float distance, int mask, QueryTriggerInteraction triggerInteraction);
    sealed class CapsuleQueryBuffer : IDisposable
    {
        static readonly CapsuleCastNonAllocNative NativeCast = IL2CPP.ResolveICall<CapsuleCastNonAllocNative>(
            "UnityEngine.PhysicsScene::Internal_CapsuleCastNonAlloc_Injected");
        RaycastHit[] hits;
        GCHandle pin;
        internal CapsuleQueryBuffer()
        {
            if (Marshal.SizeOf<QuerySpan>() != 16 || Marshal.SizeOf<RaycastHit>() != 44)
                throw new InvalidOperationException("Native capsule-query buffer layout differs from the installed verified Unity build");
            Resize(128);
        }
        void Resize(int length)
        {
            if (pin.IsAllocated) pin.Free();
            hits = new RaycastHit[length];
            pin = GCHandle.Alloc(hits, GCHandleType.Pinned);
        }
        internal ReadOnlySpan<RaycastHit> Cast(Vector3 lower, Vector3 upper, float radius, Vector3 direction, float distance, int mask)
        {
            while (true)
            {
                var scene = Physics.defaultPhysicsScene;
                // ManagedSpanWrapper has verified explicit native fields
                // begin@0 and length@8. Fill that blittable value directly: the
                // generated top-level NonAlloc helper creates an IL2CPP Span<T>,
                // while all-hit helpers try to instantiate abstract array bases.
                var rawSpan = new QuerySpan { Begin = pin.AddrOfPinnedObject(), Length = hits.Length };
                int count = NativeCast(ref scene, ref lower, ref upper, radius,
                    ref direction, ref rawSpan, distance, mask, QueryTriggerInteraction.Ignore);
                if (count < 0 || count > hits.Length) throw new InvalidOperationException("Native capsule query returned invalid hit count " + count);
                if (count < hits.Length) return hits.AsSpan(0, count);
                if (hits.Length >= 8192) throw new InvalidOperationException("Native capsule query saturated its 8192-hit buffer; no route pass can be inferred");
                Resize(hits.Length * 2);
            }
        }
        public void Dispose() { if (pin.IsAllocated) pin.Free(); hits = null; }
    }

    public static ClearanceReport Apply(MapGenerationController generator)
    {
        var timer = Stopwatch.StartNew();
        var report = new ClearanceReport
        {
            Scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            NativeMapSeed = MapGenerationController.mapSeed
        };
        last = report;
        var props = new List<Prop>();
        var zones = new List<Zone>();
        var savedColliders = new List<Collider>();
        CapsuleQueryBuffer queryBuffer = null;
        bool safeFallback = false;
        try
        {
            var gm = GameManager.Instance;
            var player = MyPlayer.Instance;
            var movement = player?.playerMovement;
            if (generator == null || gm == null || movement == null || MapGenerationController.isGenerating)
            {
                report.Reason = "Native generator/player not ready; added solid decoration disabled";
                safeFallback = true;
                return report;
            }
            Physics.SyncTransforms();
            queryBuffer = new CapsuleQueryBuffer();
            activeCastQueries = queryBuffer;
            ResolveMasks(generator, movement, gm, report);
            report.NativePlayerRadius = movement.GetPlayerRadius();
            var nativeCollider = movement.GetPlayerCollider();
            var body = MeasureBody(nativeCollider, report, out var spawn);
            report.ProbeCapsuleRadius = body.Radius;
            report.ProbeCapsuleHeight = body.Height;
            if (nativeCollider == null) spawn = player.transform.position;
            report.UnprojectedSpawn = Array(spawn);
            if (Ground(spawn, spawn.y + Math.Max(8, body.Height * 2), spawn.y - 64, report.GroundMask, out var projectedSpawn))
            {
                report.SpawnProjectedOntoNativeFloor = true;
                report.SpawnFloorProjectionDistance = projectedSpawn.y - spawn.y;
                spawn = projectedSpawn;
            }
            var nativeSpawn = MapGenerationController.tileSpawnPosition;
            report.Spawn = Array(spawn);
            report.NativeTileSpawn = Array(nativeSpawn);
            report.NativeTileSpawnApplicable = MapController.currentMap != null && (int)MapController.currentMap.mapType == 0;

            var interactables = Object.FindObjectsOfType<BaseInteractable>()
                .Where(x => x != null && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy).ToArray();
            report.NativeInteractablesBefore = interactables.Length;
            zones.Add(new(spawn, Math.Max(7, body.Radius * 6), default, false, false));
            if (report.NativeTileSpawnApplicable) zones.Add(new(nativeSpawn, Math.Max(7, body.Radius * 6), default, false, false));
            BaseInteractable target = null;
            foreach (var item in interactables)
            {
                bool gate = item.TryCast<InteractableBossSpawner>() != null ||
                    item.TryCast<InteractableBossSpawnerFinal>() != null ||
                    item.TryCast<InteractablePortal>() != null || item.TryCast<InteractablePortalFinal>() != null;
                zones.Add(new(item.transform.position, gate ? Math.Max(11, body.Radius * 9) : Math.Max(3.5f, body.Radius * 3.5f), default, false, false));
                if (target == null && (item.TryCast<InteractableBossSpawner>() != null || item.TryCast<InteractableBossSpawnerFinal>() != null))
                    target = item;
            }
            var rails = Object.FindObjectsOfType<Rail>()
                .Where(x => x != null && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy).ToArray();
            report.RailCount = rails.Length;
            foreach (var rail in rails)
            {
                if (RailBounds(rail, out var bounds))
                {
                    bounds.Expand(new Vector3(body.Radius * 4 + 2, 2, body.Radius * 4 + 2));
                    zones.Add(new(default, 0, bounds, true, true));
                }
                else zones.Add(new(rail.transform.position, Math.Max(5, body.Radius * 4), default, false, true));
            }
            report.ReservedZones = zones.Count;
            // Fit/reject complete source WMO instances before discovering the
            // final decoration set. Their bounds come from the source data and
            // actual visual transform, not possibly empty startup renderer bounds.
            activeBuildings = new BuildingSafety(Plugin.Assets, body, report.GroundMask, report.ObstacleMask);
            report.Buildings = activeBuildings.Report;
            activeBuildings.Prepare();
            props = FindProps();
            report.PropsExamined = props.Count;
            foreach (var prop in props)
            {
                foreach (var zone in zones)
                {
                    if (!IntersectsZone(prop, zone)) continue;
                    Remove(prop, report, "Reserved");
                    if (zone.Rail) report.RemovedRail++; else report.RemovedReserved++;
                    break;
                }
            }
            var propsByPointer = props.ToDictionary(x => x.Root.Pointer, x => x);
            activeBuildings.ClearInteriorAndApproaches(props.Select(x => x.Root), root =>
            { if (propsByPointer.TryGetValue(root.Pointer, out var prop)) Remove(prop, report, "Building approach"); });
            activeBuildings.ValidateDoors();

            if (target == null)
            {
                report.Reason = "No native boss gate after generation; added solid decoration disabled";
                safeFallback = true;
                return report;
            }
            report.Target = target.GetIl2CppType().Name + ":" + target.name;
            var gatePosition = target.transform.position;
            report.Gate = Array(gatePosition);
            // The baseline must describe the native map. It must not accidentally
            // route over a rock we have just added, or consider a tree native wall.
            foreach (var prop in props)
                foreach (var collider in prop.Colliders)
                    if (collider != null && collider.enabled && prop.Root.activeInHierarchy)
                    { savedColliders.Add(collider); collider.enabled = false; }
            Physics.SyncTransforms();
            if (!TryBounds(generator, out var worldBounds))
            {
                report.Reason = "Native generated world bounds unavailable; added solid decoration disabled";
                safeFallback = true;
                return report;
            }
            if (report.GroundMask == 0)
            {
                report.Reason = "Native ground mask is empty; added solid decoration disabled";
                safeFallback = true;
                return report;
            }
            if (!report.SpawnProjectedOntoNativeFloor)
            {
                report.Reason = "Actual player feet cannot be projected onto native terrain; added solid decoration disabled";
                safeFallback = true;
                return report;
            }
            if (Ground(gatePosition, worldBounds.max.y + 64, worldBounds.min.y - 64, report.GroundMask, out var gateGround))
                gatePosition = gateGround;
            var route = FindRoute(worldBounds, spawn, gatePosition, body, report);
            report.NativeRouteVerified = route.Count > 1;
            foreach (var collider in savedColliders) if (collider != null) collider.enabled = true;
            savedColliders.Clear();
            Physics.SyncTransforms();
            if (!report.NativeRouteVerified)
            {
                safeFallback = true;
                if (string.IsNullOrEmpty(report.Reason)) report.Reason = "No conservative walking route proven on the native collision surface; added solid decoration disabled";
                return report;
            }
            report.RoutePoints = route.Count;
            report.RouteEnd = Array(route[^1]);
            report.EndDistanceFromGate = HorizontalDistance(route[^1], gatePosition);
            for (int i = 1; i < route.Count; i++) report.RouteLength += Vector3.Distance(route[i - 1], route[i]);
            report.Route = route.Select(Array).ToList();
            var roots = props.ToDictionary(x => x.Root.Pointer, x => x);
            foreach (var point in route)
            {
                Capsule(point, body, out var lower, out var upper);
                foreach (var hit in Physics.OverlapCapsule(lower, upper, body.Radius, -1, QueryTriggerInteraction.Ignore))
                {
                    var prop = OwningProp(hit, roots);
                    if (prop != null && !prop.Removed) { Remove(prop, report, "Corridor"); report.RemovedCorridor++; }
                }
            }
            for (int i = 1; i < route.Count; i++)
            {
                var delta = route[i] - route[i - 1];
                if (delta.sqrMagnitude < .0001f) continue;
                Capsule(route[i - 1], body, out var lower, out var upper);
                foreach (var hit in activeCastQueries.Cast(lower, upper, body.Radius, delta.normalized, delta.magnitude, -1))
                {
                    var prop = OwningProp(hit.collider, roots);
                    if (prop != null && !prop.Removed) { Remove(prop, report, "Corridor"); report.RemovedCorridor++; }
                }
            }
            Physics.SyncTransforms();
            activeBuildings.ValidateDoors();
            report.DecoratedRouteClear = true;
            for (int i = 1; i < route.Count; i++)
                if (HasObstacle(route[i - 1], route[i], body, report.ObstacleMask))
                { report.DecoratedRouteClear = false; break; }
            if (!report.DecoratedRouteClear)
            {
                safeFallback = true;
                report.Reason = "The verified baseline acquired an obstacle; added solid decoration disabled";
            }
            else
            {
                report.AddedCollisionsCannotBlockNativeRoute = true;
                report.Reason = "Native spawn-to-gate surface route verified with the measured player capsule; added decoration cleared from the route";
            }
            return report;
        }
        catch (Exception ex)
        {
            safeFallback = true;
            report.FailureDetails = ex.ToString();
            report.Reason = "Clearance probe failed; added solid decoration disabled: " + ex.GetType().Name + ": " + ex.Message;
            return report;
        }
        finally
        {
            activeCastQueries = null;
            queryBuffer?.Dispose();
            foreach (var collider in savedColliders) if (collider != null) collider.enabled = true;
            if (safeFallback)
            {
                // A disabled wall with a visible house is misleading. Whole WMO
                // instances are removed when the native baseline is unproven;
                // only ordinary M2 foliage/rocks use the collider-only fallback.
                activeBuildings?.RemoveForFallback();
                int unprocessed = BuildingSafety.RemoveUnprocessedBuildingsForFallback();
                report.Buildings.RemovedFallback += unprocessed;
                report.Buildings.Examined += unprocessed;
                if (props.Count == 0) { props = FindProps(); report.PropsExamined = props.Count; }
                foreach (var prop in props)
                    foreach (var collider in prop.Colliders)
                        if (collider != null && collider.enabled && prop.Root.activeInHierarchy && !collider.isTrigger)
                        { collider.enabled = false; report.SolidDecorCollidersDisabled++; }
                report.AddedCollisionsCannotBlockNativeRoute = true;
            }
            Physics.SyncTransforms();
            report.PropsRemaining = props.Count(x => x.Root != null && x.Root.activeInHierarchy);
            report.ReservedViolationsRemaining = props.Count(x => x.Root != null && x.Root.activeInHierarchy && zones.Any(z => IntersectsZone(x, z)));
            report.NativeInteractablesAfter = Object.FindObjectsOfType<BaseInteractable>()
                .Count(x => x != null && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy);
            activeBuildings?.RefreshReport();
            activeBuildings = null;
            report.ElapsedMilliseconds = timer.ElapsedMilliseconds;
        }
    }

    static List<Prop> FindProps()
    {
        var result = new List<Prop>();
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null || !t.name.StartsWith(Prefix, StringComparison.Ordinal) ||
                !t.gameObject.scene.IsValid() || !t.gameObject.activeInHierarchy) continue;
            bool nested = false;
            for (var parent = t.parent; parent != null; parent = parent.parent)
                if (parent.name.StartsWith(Prefix, StringComparison.Ordinal)) { nested = true; break; }
            if (nested) continue;
            string source = t.name[Prefix.Length..].Replace("(Clone)", "");
            var marker = t.GetComponent<ElwynnPropMarker>();
            if (marker != null && !string.IsNullOrWhiteSpace(marker.SourceId)) source = marker.SourceId;
            result.Add(new Prop { Root = t.gameObject, Source = source });
        }
        var roots = result.ToDictionary(x => x.Root.Pointer, x => x);
        // Avoid Component.GetComponentInChildren<T>(bool): that stripped generic
        // wrapper is invalid in this particular IL2CPP build.
        foreach (var collider in Resources.FindObjectsOfTypeAll<Collider>())
        {
            if (collider == null || !collider.gameObject.scene.IsValid()) continue;
            var prop = OwningProp(collider, roots);
            if (prop != null) prop.Colliders.Add(collider);
        }
        return result;
    }
    static Prop OwningProp(Collider collider, Dictionary<IntPtr, Prop> roots)
    {
        if (collider == null) return null;
        for (var t = collider.transform; t != null; t = t.parent)
            if (roots.TryGetValue(t.gameObject.Pointer, out var prop)) return prop;
        return null;
    }
    static void Remove(Prop prop, ClearanceReport report, string reason)
    {
        if (prop.Removed) return;
        prop.Removed = true;
        activeBuildings?.NoteRemoval(prop.Root, reason);
        prop.Root.SetActive(false);
        report.RemovedBySource.TryGetValue(prop.Source, out var count);
        report.RemovedBySource[prop.Source] = count + 1;
    }
    static bool IntersectsZone(Prop prop, Zone zone)
    {
        // Foliage without a solid collider uses its foot point. The canopy must
        // not become an invisible 20-metre-wide gameplay obstacle.
        var center = prop.Root.transform.position;
        var bounds = new Bounds(center, Vector3.one * .1f);
        bool hasCollider = false;
        Bounds sourceBounds = default;
        bool sourceBuilding = BuildingSafety.IsBuilding(prop.Root) && activeBuildings != null && activeBuildings.TryBounds(prop.Root, out sourceBounds);
        if (sourceBuilding) { bounds = sourceBounds; hasCollider = true; }
        else foreach (var collider in prop.Colliders)
                if (collider != null && collider.enabled && !collider.isTrigger)
                { if (!hasCollider) { bounds = collider.bounds; hasCollider = true; } else bounds.Encapsulate(collider.bounds); }
        if (zone.IsBounds)
            return bounds.min.x <= zone.Bounds.max.x && bounds.max.x >= zone.Bounds.min.x &&
                bounds.min.z <= zone.Bounds.max.z && bounds.max.z >= zone.Bounds.min.z &&
                bounds.min.y <= zone.Bounds.max.y + 4 && bounds.max.y >= zone.Bounds.min.y - 4;
        if (Math.Abs(center.y - zone.Center.y) > Math.Max(8, bounds.size.y + 3)) return false;
        float x = Math.Clamp(zone.Center.x, bounds.min.x, bounds.max.x);
        float z = Math.Clamp(zone.Center.z, bounds.min.z, bounds.max.z);
        return (x - zone.Center.x) * (x - zone.Center.x) + (z - zone.Center.z) * (z - zone.Center.z) <= zone.Radius * zone.Radius;
    }
    static void ResolveMasks(MapGenerationController generator, PlayerMovement movement, GameManager gm, ClearanceReport report)
    {
        report.NativeWhatIsGroundMask = gm.whatIsGround.value;
        report.NativeGroundAndObjectsMask = gm.whatIsGroundAndObjects.value;
        report.NativeMovementGroundOnlyMask = movement.whatIsGroundOnly.value;
        int floorLayer = -1;
        bool tiles = MapController.currentMap != null && (int)MapController.currentMap.mapType == 0;
        if (tiles)
        {
            var combined = generator.proceduralTileGeneration?.collider;
            if (combined != null && combined.scene.IsValid() && combined.activeInHierarchy)
            { floorLayer = combined.layer; report.GroundMaskSource = "Native combined procedural tile collider layer"; }
        }
        else
        {
            var mesh = generator.proceduralMapMeshGenerator?.display?.meshCollider;
            if (mesh != null && mesh.enabled && mesh.gameObject.scene.IsValid() && mesh.gameObject.activeInHierarchy)
            { floorLayer = mesh.gameObject.layer; report.GroundMaskSource = "Native procedural mesh collider layer"; }
        }
        if (floorLayer < 0)
        {
            floorLayer = LayerMask.NameToLayer("Ground");
            if (floorLayer >= 0) report.GroundMaskSource = "Native named Ground layer";
        }
        if (floorLayer >= 0)
            report.GroundMask = 1 << floorLayer;
        else
        {
            report.GroundMask = movement.whatIsGroundOnly.value;
            report.GroundMaskSource = "Native player ground-only mask fallback";
        }
        // In this installed build both GameManager masks are 4352 (Ground8 |
        // Object12). Subtracting them silently produced an empty obstacle mask.
        // The actual generated floor collider supplies the authoritative split.
        report.ObstacleMask = gm.whatIsGroundAndObjects.value & ~report.GroundMask;
    }
    static Body MeasureBody(Collider collider, ClearanceReport report, out Vector3 feet)
    {
        feet = collider != null ? collider.transform.position : Vector3.zero;
        float radius = report.NativePlayerRadius;
        float height = radius * 2;
        if (collider != null)
        {
            report.NativeColliderType = collider.GetIl2CppType().Name;
            var bounds = collider.bounds;
            report.NativeColliderBoundsSize = Array(bounds.size);
            report.NativeColliderBoundsFeet = Array(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            var capsule = collider.TryCast<CapsuleCollider>();
            if (capsule != null)
            {
                report.NativeCapsuleLocalHeight = capsule.height;
                report.NativeCapsuleLocalRadius = capsule.radius;
                report.NativeCapsuleDirection = capsule.direction;
                var s = capsule.transform.lossyScale;
                s = new Vector3(Math.Abs(s.x), Math.Abs(s.y), Math.Abs(s.z));
                report.NativeCapsuleScale = Array(s);
                Vector3 localAxis = capsule.direction == 0 ? Vector3.right : capsule.direction == 2 ? Vector3.forward : Vector3.up;
                float axisScale = capsule.direction == 0 ? s.x : capsule.direction == 2 ? s.z : s.y;
                float radiusScale = capsule.direction == 0 ? Math.Max(s.y, s.z) : capsule.direction == 2 ? Math.Max(s.x, s.y) : Math.Max(s.x, s.z);
                float worldRadius = capsule.radius * radiusScale;
                float worldHeight = Math.Max(capsule.height * axisScale, worldRadius * 2);
                var axis = capsule.transform.TransformDirection(localAxis).normalized;
                report.NativeCapsuleAxis = Array(axis);
                float shaftHalf = Math.Max(0, worldHeight * .5f - worldRadius);
                var center = capsule.transform.TransformPoint(capsule.center);
                feet = new Vector3(center.x, center.y - Math.Abs(axis.y) * shaftHalf - worldRadius, center.z);
                // Enclose the real native capsule even if its local axis is
                // rotated. Using only a stale broadphase bounds height can turn
                // a 3.85-unit capsule into a 1.44-unit sphere during startup.
                radius = worldRadius + MathF.Sqrt(axis.x * axis.x + axis.z * axis.z) * shaftHalf;
                height = worldRadius * 2 + Math.Abs(axis.y) * shaftHalf * 2;
            }
            else
            {
                feet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                radius = Math.Max(bounds.extents.x, bounds.extents.z);
                height = bounds.size.y;
            }
        }
        if (!float.IsFinite(radius) || radius <= .01f) radius = .5f;
        if (!float.IsFinite(height) || height < radius * 2) height = radius * 2;
        radius += .18f;
        return new Body(radius, Math.Max(height, radius * 2));
    }
    static bool TryBounds(MapGenerationController generator, out Bounds bounds)
    {
        var placer = generator.randomObjectPlacer;
        if (placer != null && placer.size.x > 10 && placer.size.z > 10)
        {
            bounds = new Bounds(placer.center, new Vector3(placer.size.x, Math.Max(placer.size.y, 512), placer.size.z));
            return true;
        }
        var tiles = generator.proceduralTileGeneration;
        if (tiles != null)
        {
            var size = tiles.GetWorldSize();
            if (size.x > 10 && size.z > 10)
            { bounds = new Bounds(tiles.GetWorldCenter(), new Vector3(size.x, Math.Max(size.y, 512), size.z)); return true; }
        }
        var display = generator.proceduralMapMeshGenerator?.display;
        if (display?.meshCollider != null)
        {
            bounds = display.meshCollider.bounds;
            bounds.Expand(new Vector3(0, 512, 0));
            return bounds.size.x > 10 && bounds.size.z > 10;
        }
        bounds = default;
        return false;
    }
    static bool RailBounds(Rail rail, out Bounds bounds)
    {
        bounds = rail.renderCollider != null ? rail.renderCollider.bounds : default;
        bool found = bounds.size.sqrMagnitude > .01f;
        // A disabled rail collision mesh has empty bounds. Its renderer still
        // describes the full rail, so reserve the actual track instead of a
        // tiny circle around the spline origin.
        if (found) return true;
        foreach (var renderer in Resources.FindObjectsOfTypeAll<Renderer>())
        {
            if (renderer == null || !renderer.gameObject.scene.IsValid() || !renderer.gameObject.activeInHierarchy) continue;
            for (var t = renderer.transform; t != null; t = t.parent)
                if (t.Pointer == rail.transform.Pointer)
                { if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds); break; }
        }
        return found;
    }
    static List<Vector3> FindRoute(Bounds area, Vector3 start, Vector3 gate, Body body, ClearanceReport report)
    {
        var budget = Stopwatch.StartNew();
        float spacing = Math.Max(body.Radius * 2 + .75f, Math.Max(area.size.x, area.size.z) / 112f);
        int width = Math.Clamp((int)Math.Ceiling(area.size.x / spacing) + 1, 3, 114);
        int depth = Math.Clamp((int)Math.Ceiling(area.size.z / spacing) + 1, 3, 114);
        float stepX = area.size.x / (width - 1), stepZ = area.size.z / (depth - 1);
        report.GridSpacing = Math.Max(stepX, stepZ);
        report.GridNodes = width * depth;
        var nodes = new Node[width * depth];
        float rayTop = Math.Max(area.max.y + 64, Math.Max(start.y, gate.y) + 128);
        float rayBottom = Math.Min(area.min.y - 64, Math.Min(start.y, gate.y) - 128);
        for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                if ((index & 255) == 0 && budget.ElapsedMilliseconds > 4000)
                { report.RouteBudgetExceeded = true; report.Reason = "Native surface sampling exceeded the loading-time budget; added solid decoration disabled"; return new(); }
                var point = new Vector3(area.min.x + x * stepX, 0, area.min.z + z * stepZ);
                if (Ground(point, rayTop, rayBottom, report.GroundMask, out var feet))
                { nodes[index] = new(feet, true); report.WalkableNodes++; }
            }
        int startIndex = -1;
        float startDistance = float.PositiveInfinity;
        for (int i = 0; i < nodes.Length; i++)
        {
            if (!nodes[i].Valid) continue;
            float distance = HorizontalDistance(start, nodes[i].Feet);
            if (distance >= startDistance || distance > report.GridSpacing * 2 + body.Radius) continue;
            if (!SurfaceSegment(start, nodes[i].Feet, body, rayTop, rayBottom, report.GroundMask, report.ObstacleMask)) continue;
            startDistance = distance; startIndex = i;
        }
        if (startIndex < 0)
        { report.Reason = "Native spawn cannot be snapped to a conservative walking grid; added solid decoration disabled"; return new(); }
        float goalRadius = Math.Max(report.GridSpacing * 1.5f, body.Radius * 3 + 2);
        var cost = Enumerable.Repeat(float.PositiveInfinity, nodes.Length).ToArray();
        var parents = Enumerable.Repeat(-1, nodes.Length).ToArray();
        var done = new bool[nodes.Length];
        var queue = new PriorityQueue<int, float>();
        var edgeCache = new Dictionary<(int, int), bool>();
        cost[startIndex] = 0;
        queue.Enqueue(startIndex, HorizontalDistance(nodes[startIndex].Feet, gate));
        int found = -1;
        var directions = new (int X, int Z)[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (-1, 1), (1, -1), (-1, -1) };
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (done[current]) continue;
            done[current] = true; report.NodesVisited++;
            if ((report.NodesVisited & 31) == 0 && budget.ElapsedMilliseconds > 4000)
            { report.RouteBudgetExceeded = true; report.Reason = "Conservative native route search exceeded the loading-time budget; added solid decoration disabled"; return new(); }
            var a = nodes[current].Feet;
            if (HorizontalDistance(a, gate) <= goalRadius && Math.Abs(a.y - gate.y) < Math.Max(goalRadius, 12))
            { found = current; break; }
            int currentX = current % width, currentZ = current / width;
            foreach (var direction in directions)
            {
                int nx = currentX + direction.X, nz = currentZ + direction.Z;
                if (nx < 0 || nx >= width || nz < 0 || nz >= depth) continue;
                int next = nz * width + nx;
                if (done[next] || !nodes[next].Valid) continue;
                var b = nodes[next].Feet;
                float distance = Vector3.Distance(a, b), candidate = cost[current] + distance;
                if (candidate >= cost[next]) continue;
                var edge = current < next ? (current, next) : (next, current);
                if (!edgeCache.TryGetValue(edge, out bool passable))
                {
                    passable = SurfaceSegment(a, b, body, rayTop, rayBottom, report.GroundMask, report.ObstacleMask);
                    edgeCache[edge] = passable;
                }
                if (!passable) continue;
                cost[next] = candidate; parents[next] = current;
                queue.Enqueue(next, candidate + HorizontalDistance(b, gate));
            }
        }
        if (found < 0) return new();
        var route = new List<Vector3>();
        for (int index = found; index >= 0; index = parents[index]) route.Add(nodes[index].Feet);
        route.Reverse();
        if (Vector3.Distance(start, route[0]) > .01f) route.Insert(0, start);
        // Follow the sampled ground between grid nodes, rather than drawing a
        // straight capsule chord below a convex slope or above a shallow hollow.
        var corridor = new List<Vector3> { route[0] };
        for (int segment = 1; segment < route.Count; segment++)
        {
            float distance = HorizontalDistance(route[segment - 1], route[segment]);
            int parts = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(.75f, body.Radius)));
            for (int i = 1; i <= parts; i++)
            {
                var p = Vector3.Lerp(route[segment - 1], route[segment], (float)i / parts);
                if (!Ground(p, rayTop, rayBottom, report.GroundMask, out var feet)) return new();
                corridor.Add(feet);
            }
        }
        return corridor;
    }
    static bool Ground(Vector3 position, float top, float bottom, int mask, out Vector3 feet)
    {
        feet = position;
        var origin = new Vector3(position.x, top, position.z);
        if (!Physics.Raycast(origin, Vector3.down, out var hit, top - bottom, mask, QueryTriggerInteraction.Ignore) ||
            hit.collider == null || hit.normal.y < WalkableNormalY || !float.IsFinite(hit.point.y)) return false;
        feet = hit.point; return true;
    }
    static bool SurfaceSegment(Vector3 from, Vector3 to, Body body, float top, float bottom, int groundMask, int obstacleMask)
    {
        float horizontal = HorizontalDistance(from, to);
        if (horizontal <= .001f) return Math.Abs(to.y - from.y) < .25f;
        if (Math.Abs(to.y - from.y) > horizontal * SlopeTangent + .25f) return false;
        int parts = Math.Max(1, (int)Math.Ceiling(horizontal / Math.Max(.75f, body.Radius)));
        Vector3 previous = from;
        for (int i = 1; i <= parts; i++)
        {
            var p = Vector3.Lerp(from, to, (float)i / parts);
            if (!Ground(p, top, bottom, groundMask, out var feet)) return false;
            float run = HorizontalDistance(previous, feet);
            if (Math.Abs(feet.y - previous.y) > run * SlopeTangent + .25f) return false;
            // A ray-only centre line is insufficient: check both sides of the
            // actual capsule footprint for holes and steep height discontinuity.
            var side = new Vector3(-(to.z - from.z), 0, to.x - from.x).normalized * body.Radius;
            if (!Ground(feet + side, top, bottom, groundMask, out var left) ||
                !Ground(feet - side, top, bottom, groundMask, out var right) ||
                Math.Abs(left.y - feet.y) > body.Radius * SlopeTangent + .3f ||
                Math.Abs(right.y - feet.y) > body.Radius * SlopeTangent + .3f) return false;
            if (HasObstacle(previous, feet, body, obstacleMask)) return false;
            previous = feet;
        }
        return true;
    }
    static void Capsule(Vector3 feet, Body body, out Vector3 lower, out Vector3 upper)
    {
        lower = feet + Vector3.up * (body.Radius + GroundSkin);
        upper = feet + Vector3.up * (body.Height - body.Radius + GroundSkin);
    }
    internal static bool HasObstacle(Vector3 from, Vector3 to, Body body, int mask)
    {
        LastObstacleDescription = null;
        if (mask == 0) return false;
        Capsule(from, body, out var lower, out var upper);
        foreach (var collider in Physics.OverlapCapsule(lower, upper, body.Radius, mask, QueryTriggerInteraction.Ignore))
            if (StaticObstacle(collider)) { LastObstacleDescription = collider.name + " layer=" + collider.gameObject.layer; return true; }
        var delta = to - from;
        if (delta.sqrMagnitude <= .0001f) return false;
        // Both the stripped public all-hit stub and the broken abstract-array
        // unmarshalling are bypassed by a concrete pinned native query buffer.
        foreach (var hit in activeCastQueries.Cast(lower, upper, body.Radius, delta.normalized, delta.magnitude, mask))
            if (StaticObstacle(hit.collider)) { LastObstacleDescription = hit.collider.name + " layer=" + hit.collider.gameObject.layer; return true; }
        return false;
    }
    static bool StaticObstacle(Collider collider)
    {
        if (collider == null || collider.isTrigger || collider.attachedRigidbody != null) return false;
        for (var t = collider.transform; t != null; t = t.parent)
            if (t.GetComponent<Enemy>() != null || t.GetComponent<MyPlayer>() != null || t.GetComponent<PlayerMovement>() != null) return false;
        return true;
    }
    static float HorizontalDistance(Vector3 a, Vector3 b)
    { float x = a.x - b.x, z = a.z - b.z; return MathF.Sqrt(x * x + z * z); }
    static float[] Array(Vector3 value) => new[] { value.x, value.y, value.z };
}
