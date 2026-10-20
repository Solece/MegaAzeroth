using UnityEngine;

namespace AzerothMegabonk.ElwynnBiome;

public sealed class BuildingDoorReport
{
    public int SourcePortalIndex { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float[] Floor { get; set; }
    public float[] Exterior { get; set; }
    public float[] Interior { get; set; }
    public float ExteriorFloorY { get; set; }
    public float InteriorFloorY { get; set; }
    public float ExteriorStep { get; set; }
    public float InteriorStep { get; set; }
    public bool CapsuleVerified { get; set; }
    public string Reason { get; set; }
}
public sealed class BuildingInstanceReport
{
    public string SourceId { get; set; }
    public string Name { get; set; }
    public float[] FootprintMin { get; set; }
    public float[] FootprintMax { get; set; }
    public float[] ReservedFootprintMin { get; set; }
    public float[] ReservedFootprintMax { get; set; }
    public float FloorY { get; set; }
    public float FitDeltaY { get; set; }
    public float NativeGroundMinY { get; set; }
    public float NativeGroundMaxY { get; set; }
    public float FoundationDepth { get; set; }
    public int NativeFootprintSamples { get; set; }
    public bool Active { get; set; }
    public string RemovedReason { get; set; }
    public List<BuildingDoorReport> Doors { get; set; } = new();
}
public sealed class BuildingSafetyReport
{
    public int Examined { get; set; }
    public int Remaining { get; set; }
    public int RejectedTerrain { get; set; }
    public int RejectedDoors { get; set; }
    public int RejectedOverlap { get; set; }
    public int RemovedReserved { get; set; }
    public int RemovedCorridor { get; set; }
    public int RemovedFallback { get; set; }
    public int DoorViolationsRemaining { get; set; }
    public int DecorRemovedInside { get; set; }
    public int DecorRemovedApproaches { get; set; }
    public List<BuildingInstanceReport> Instances { get; set; } = new();
}

// This owns only additional WMO scene instances. Source bounds and portals are
// transformed through the actual visual child; no native terrain is flattened.
internal sealed class BuildingSafety
{
    const string Prefix = "AzerothElwynn_building_";
    const float MaxDoorStep = .42f;
    readonly BiomeAssets assets;
    readonly BiomeClearance.Body body;
    readonly int groundMask, obstacleMask;
    readonly List<Entry> entries = new();
    internal BuildingSafetyReport Report { get; } = new();

    sealed class Door
    {
        internal BuildingEntrance Source;
        internal Vector3 Floor, Outward, Exterior, Interior;
        internal BuildingDoorReport Report;
    }
    sealed class Entry
    {
        internal string Id;
        internal GameObject Root;
        internal Transform Visual;
        internal Bounds Bounds;
        internal Bounds ReservedBounds;
        internal float FloorY;
        internal List<Door> Doors = new();
        internal BuildingInstanceReport Report;
    }
    internal static bool IsBuilding(GameObject root) => root != null && root.name.StartsWith(Prefix, StringComparison.Ordinal);
    internal BuildingSafety(BiomeAssets source, BiomeClearance.Body capsule, int floorMask, int solidMask)
    { assets = source; body = capsule; groundMask = floorMask; obstacleMask = solidMask; }

    internal void Prepare()
    {
        foreach (var root in UnityEngine.Object.FindObjectsOfType<ElwynnPropMarker>()
            .Where(x => x != null && IsBuilding(x.gameObject) && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy)
            .Select(x => x.gameObject).OrderBy(x => x.name, StringComparer.Ordinal).ThenBy(x => x.transform.position.x).ThenBy(x => x.transform.position.z))
        {
            string id = root.name[Prefix.Length..].Replace("(Clone)", "");
            int suffix = id.IndexOf("_variant", StringComparison.Ordinal); if (suffix >= 0) id = id[..suffix];
            var report = new BuildingInstanceReport { SourceId = id, Name = root.name, Active = true };
            Report.Examined++; Report.Instances.Add(report);
            var entry = new Entry { Id = id, Root = root, Report = report };
            entries.Add(entry);
            for (int i = 0; i < root.transform.childCount; i++)
                if (root.transform.GetChild(i).name == "OriginalElwynnBuilding") entry.Visual = root.transform.GetChild(i);
            if (assets == null || entry.Visual == null || !assets.BuildingBounds.ContainsKey(id) ||
                !assets.BuildingEntrances.TryGetValue(id, out var portals) || portals.Length == 0)
            { Reject(entry, "Terrain: source bounds/visual/entrance metadata unavailable"); continue; }
            if (!FitTerrain(entry)) continue;
            PopulateDoors(entry, portals);
            if (entry.Doors.Any(d => d.Report.Width < body.Radius * 2 + .12f || d.Report.Height < body.Height + .22f))
            { Reject(entry, "Door: source opening cannot accommodate the measured native capsule"); continue; }
            if (entries.Any(other => other != entry && other.Root.activeInHierarchy && other.Visual != null &&
                other.Report.RemovedReason == null && HorizontalOverlap(entry.Bounds, other.Bounds, .5f)))
            { Reject(entry, "Overlap: source building footprints intersect"); continue; }
        }
        Physics.SyncTransforms();
    }
    bool FitTerrain(Entry entry)
    {
        if (Math.Abs(entry.Visual.up.y) < .995f)
        { Reject(entry, "Terrain: source building is tilted"); return false; }
        var source = assets.BuildingBounds[entry.Id]; float sourceFloor = assets.BuildingFloorY[entry.Id];
        var points = new List<Vector3>();
        var localCenter = new Vector3(source.center.x, sourceFloor, source.center.z);
        int columns = Math.Clamp((int)Math.Ceiling(source.size.x * Math.Abs(entry.Visual.lossyScale.x) / 5), 2, 18);
        int rows = Math.Clamp((int)Math.Ceiling(source.size.z * Math.Abs(entry.Visual.lossyScale.z) / 5), 2, 18);
        for (int column = 0; column <= columns; column++)
            for (int row = 0; row <= rows; row++)
            {
                float x = -.96f + 1.92f * column / columns, z = -.96f + 1.92f * row / rows;
                var p = entry.Visual.TransformPoint(localCenter + new Vector3(source.extents.x * x, 0, source.extents.z * z));
                entry.Report.NativeFootprintSamples++;
                if (!NativeFloor(p, out var ground))
                { Reject(entry, "Terrain: source footprint crosses missing/steep native ground"); return false; }
                points.Add(ground);
            }
        float min = points.Min(x => x.y), max = points.Max(x => x.y);
        entry.Report.NativeGroundMinY = min; entry.Report.NativeGroundMaxY = max;
        float span = Math.Max(source.size.x * Math.Abs(entry.Visual.lossyScale.x), source.size.z * Math.Abs(entry.Visual.lossyScale.z));
        float limit = Math.Clamp(span * .045f, 1.2f, 2.6f);
        if (max - min > limit)
        { Reject(entry, "Terrain: native height variation across full source footprint exceeds " + limit); return false; }
        var doorGround = new List<float>();
        foreach (var portal in assets.BuildingEntrances[entry.Id])
        {
            var p = entry.Visual.TransformPoint(new Vector3(portal.Center.x, portal.FloorY, portal.Center.z));
            var outward = entry.Visual.TransformDirection(portal.Outward); outward.y = 0; outward.Normalize();
            if (outward.sqrMagnitude < .9f || !NativeFloor(p + outward * Math.Max(2.2f, body.Radius * 3.5f), out var ground))
            { Reject(entry, "Terrain: native ground absent at a source door approach"); return false; }
            doorGround.Add(ground.y);
        }
        doorGround.Sort();
        float currentFloor = entry.Visual.TransformPoint(localCenter).y;
        float desiredFloor = Math.Max(doorGround[doorGround.Count / 2] + .055f, max - .18f);
        float foundation = Math.Max(0, sourceFloor - source.min.y) * Math.Abs(entry.Visual.lossyScale.y);
        entry.Report.FoundationDepth = foundation;
        if (desiredFloor - min > foundation + .45f || Math.Abs(desiredFloor - currentFloor) > 3)
        { Reject(entry, "Terrain: source foundation would float or require excessive vertical relocation"); return false; }
        float delta = desiredFloor - currentFloor;
        entry.Root.transform.position += Vector3.up * delta;
        entry.Report.FitDeltaY = delta;
        entry.FloorY = entry.Visual.TransformPoint(localCenter).y;
        entry.Report.FloorY = entry.FloorY;
        entry.Bounds = TransformBounds(entry.Visual, source);
        entry.Report.FootprintMin = Array(entry.Bounds.min); entry.Report.FootprintMax = Array(entry.Bounds.max);
        return true;
    }
    void PopulateDoors(Entry entry, BuildingEntrance[] sources)
    {
        entry.ReservedBounds = entry.Bounds;
        foreach (var source in sources)
        {
            var floor = entry.Visual.TransformPoint(new Vector3(source.Center.x, source.FloorY, source.Center.z));
            var outward = entry.Visual.TransformDirection(source.Outward); outward.y = 0; outward.Normalize();
            var localSide = Vector3.Cross(Vector3.up, source.Outward).normalized;
            float widthScale = entry.Visual.TransformVector(localSide).magnitude;
            float heightScale = entry.Visual.TransformVector(Vector3.up).magnitude;
            var exterior = floor + outward * Math.Max(2.2f, body.Radius * 3.5f);
            var interior = floor - outward * Math.Max(1.8f, body.Radius * 2.8f);
            var report = new BuildingDoorReport { SourcePortalIndex = source.SourcePortalIndex, Width = source.Width * widthScale,
                Height = source.Height * heightScale, Floor = Array(floor), Exterior = Array(exterior), Interior = Array(interior), Reason = "Not yet capsule verified" };
            entry.Report.Doors.Add(report);
            entry.Doors.Add(new Door { Source = source, Floor = floor, Outward = outward, Exterior = exterior, Interior = interior, Report = report });
            var side = Vector3.Cross(Vector3.up, outward).normalized * (report.Width * .5f + body.Radius + .75f);
            var far = floor + outward * Math.Max(6, body.Radius * 6);
            foreach (var corner in new[] { floor - side, floor + side, far - side, far + side })
            { entry.ReservedBounds.Encapsulate(corner); entry.ReservedBounds.Encapsulate(corner + Vector3.up * body.Height); }
        }
        entry.Report.ReservedFootprintMin = Array(entry.ReservedBounds.min);
        entry.Report.ReservedFootprintMax = Array(entry.ReservedBounds.max);
    }
    internal void ClearInteriorAndApproaches(IEnumerable<GameObject> props, Action<GameObject> remove)
    {
        foreach (var prop in props)
        {
            if (prop == null || !prop.activeInHierarchy || IsBuilding(prop)) continue;
            var collider = prop.GetComponent<Collider>();
            var b = collider != null && collider.enabled ? collider.bounds : new Bounds(prop.transform.position, Vector3.one * .1f);
            bool removed = false;
            foreach (var entry in entries.Where(e => e.Root.activeInHierarchy))
            {
                if (HorizontalOverlap(b, entry.Bounds, body.Radius + .4f) &&
                    Math.Abs(prop.transform.position.y - entry.FloorY) < Math.Max(7, entry.Bounds.size.y))
                { remove(prop); Report.DecorRemovedInside++; removed = true; break; }
                foreach (var door in entry.Doors)
                {
                    var point = prop.transform.position - door.Floor; point.y = 0;
                    float along = Vector3.Dot(point, door.Outward);
                    float side = Math.Abs(Vector3.Dot(point, Vector3.Cross(Vector3.up, door.Outward)));
                    float propRadius = collider != null ? Math.Max(b.extents.x, b.extents.z) : .25f;
                    if (along < -.5f - propRadius || along > Math.Max(6, body.Radius * 6) + propRadius ||
                        side > door.Report.Width * .5f + body.Radius + .75f + propRadius ||
                        Math.Abs(prop.transform.position.y - door.Floor.y) > 6) continue;
                    remove(prop); Report.DecorRemovedApproaches++; removed = true; break;
                }
                if (removed) break;
            }
        }
        Physics.SyncTransforms();
    }
    internal void ValidateDoors()
    {
        Physics.SyncTransforms();
        foreach (var entry in entries.Where(e => e.Root.activeInHierarchy))
        {
            foreach (var door in entry.Doors)
            {
                door.Report.CapsuleVerified = false;
                if (!StandFloor(door.Exterior, out var exterior) || !StandFloor(door.Interior, out var interior))
                { door.Report.Reason = "Actual exterior/interior collision floor unavailable or too far from source threshold"; continue; }
                door.Report.ExteriorFloorY = exterior.y; door.Report.InteriorFloorY = interior.y;
                door.Report.ExteriorStep = door.Floor.y - exterior.y; door.Report.InteriorStep = interior.y - door.Floor.y;
                door.Report.Exterior = Array(exterior); door.Report.Interior = Array(interior);
                if (Math.Abs(door.Report.ExteriorStep) > MaxDoorStep || Math.Abs(door.Report.InteriorStep) > MaxDoorStep)
                { door.Report.Reason = "Actual source door threshold step exceeds " + MaxDoorStep; continue; }
                // Source metadata alone is not proof. Stand an actual measured
                // native capsule outside, sweep through the source opening, and
                // stand it inside against all real native/source object colliders.
                if (BiomeClearance.HasObstacle(exterior, door.Floor, body, obstacleMask) ||
                    BiomeClearance.HasObstacle(door.Floor, interior, body, obstacleMask) ||
                    BiomeClearance.HasObstacle(interior, interior, body, obstacleMask))
                { door.Report.Reason = "Actual native capsule overlaps/sweeps solid collision at source doorway: " + BiomeClearance.LastObstacleDescription; continue; }
                door.Report.CapsuleVerified = true; door.Report.Reason = "Actual native capsule stood outside, swept the source opening and stood inside";
            }
            if (entry.Doors.Count == 0 || entry.Doors.Any(d => !d.Report.CapsuleVerified))
                Reject(entry, "Door: one or more real external entrances failed native capsule traversal");
        }
        RefreshReport();
    }
    bool NativeFloor(Vector3 expected, out Vector3 feet)
    {
        feet = expected;
        if (!Physics.Raycast(expected + Vector3.up * 32, Vector3.down, out var hit, 64, groundMask, QueryTriggerInteraction.Ignore) ||
            hit.normal.y < .9510565f || !float.IsFinite(hit.point.y)) return false;
        feet = hit.point; return true;
    }
    bool StandFloor(Vector3 expected, out Vector3 feet)
    {
        feet = expected;
        if (!Physics.Raycast(expected + Vector3.up * .8f, Vector3.down, out var hit, 1.6f, groundMask | obstacleMask, QueryTriggerInteraction.Ignore) ||
            hit.normal.y < .7f || !float.IsFinite(hit.point.y)) return false;
        feet = hit.point; return true;
    }
    void Reject(Entry entry, string reason)
    {
        if (!entry.Root.activeInHierarchy) return;
        entry.Root.SetActive(false); entry.Report.Active = false; entry.Report.RemovedReason = reason;
        if (reason.StartsWith("Terrain", StringComparison.Ordinal)) Report.RejectedTerrain++;
        else if (reason.StartsWith("Door", StringComparison.Ordinal)) Report.RejectedDoors++;
        else if (reason.StartsWith("Overlap", StringComparison.Ordinal)) Report.RejectedOverlap++;
    }
    internal bool TryBounds(GameObject root, out Bounds bounds)
    {
        var entry = entries.FirstOrDefault(e => e.Root.Pointer == root.Pointer);
        bounds = entry?.ReservedBounds ?? default; return entry != null && entry.Visual != null;
    }
    internal void NoteRemoval(GameObject root, string reason)
    {
        var entry = entries.FirstOrDefault(e => e.Root.Pointer == root.Pointer);
        if (entry == null || entry.Report.RemovedReason != null) return;
        entry.Report.Active = false; entry.Report.RemovedReason = reason;
        if (reason == "Corridor") Report.RemovedCorridor++;
        if (reason == "Reserved") Report.RemovedReserved++;
    }
    internal void RemoveForFallback()
    {
        foreach (var entry in entries.Where(e => e.Root != null && e.Root.activeInHierarchy))
        {
            entry.Root.SetActive(false); entry.Report.Active = false;
            entry.Report.RemovedReason = "Fallback: native baseline route was not proven; whole building removed";
            Report.RemovedFallback++;
        }
        RefreshReport();
    }
    internal void RefreshReport()
    {
        foreach (var entry in entries) entry.Report.Active = entry.Root != null && entry.Root.activeInHierarchy;
        Report.Remaining = entries.Count(e => e.Report.Active);
        Report.DoorViolationsRemaining = entries.Where(e => e.Report.Active).Sum(e => e.Doors.Count(d => !d.Report.CapsuleVerified));
    }
    internal static int RemoveUnprocessedBuildingsForFallback()
    {
        int count = 0;
        foreach (var marker in UnityEngine.Object.FindObjectsOfType<ElwynnPropMarker>())
            if (marker != null && IsBuilding(marker.gameObject) && marker.gameObject.scene.IsValid() && marker.gameObject.activeInHierarchy)
            { marker.gameObject.SetActive(false); count++; }
        return count;
    }
    static Bounds TransformBounds(Transform transform, Bounds source)
    {
        var bounds = new Bounds(transform.TransformPoint(source.min), Vector3.zero);
        for (int i = 0; i < 8; i++) bounds.Encapsulate(transform.TransformPoint(new Vector3(
            (i & 1) == 0 ? source.min.x : source.max.x, (i & 2) == 0 ? source.min.y : source.max.y,
            (i & 4) == 0 ? source.min.z : source.max.z)));
        return bounds;
    }
    static bool HorizontalOverlap(Bounds a, Bounds b, float margin) => a.min.x <= b.max.x + margin && a.max.x >= b.min.x - margin &&
        a.min.z <= b.max.z + margin && a.max.z >= b.min.z - margin;
    static float[] Array(Vector3 p) => new[] { p.x, p.y, p.z };
}
