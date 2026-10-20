using System.Text.Json;
using System.Reflection;
using Assets.Scripts.Game.Other;
using Assets.Scripts.Game.MapGeneration;
using Assets.Scripts.Managers;
using Assets.Scripts._Data.MapsAndStages;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace AzerothMegabonk.ElwynnBiome;

public static class NativeBiomeIntegration
{
    const string Prefix = "AzerothElwynn_";
    const int PaletteVersion = 1;
    const float ElwynnGroundScale = .15f;
    const float ElwynnSideScale = .12f;
    static MapData originalMap, runMap;
    static StageData originalStage, elwynnStage;
    static Il2CppReferenceArray<RandomMapObject> nativeOverride, elwynnPalette;
    static GameObject variants;
    static readonly List<Object> owned = new();
    static readonly List<object> materialChanges = new();
    static readonly List<object> variantDiagnostics = new();
    static readonly List<object> preservedTerrain = new();
    static readonly Dictionary<IntPtr, int> decorGroups = new();
    static readonly Dictionary<IntPtr, Material> finalGroundMaterials = new();
    static readonly List<object> finalGroundChanges = new();
    static readonly HashSet<IntPtr> seededGroups = new();
    static int nativeGenerateCalls, nativeObjectLayer = -1, decorSeed;
    static int nativeSpawnerCalls, decorGroupCalls, nativeRngRestores, nativeRngRestoreFailures;
    static float nativeAmountMultiplier;
    static bool overrideRestored;

    public static bool IsElwynnStage => Plugin.Enabled && elwynnStage != null
        && MapController.currentStage != null && MapController.currentStage.Pointer == elwynnStage.Pointer
        && !MapController.IsFinalBossStage() && MapController.GetStageIndex() == 0;

    static T Retain<T>(T value) where T : Object
    { value.hideFlags |= HideFlags.DontUnloadUnusedAsset; owned.Add(value); return value; }

    public static void InstallOnRunConfig(RunConfig config)
    {
        if (!Plugin.Enabled || config?.mapData == null || config.mapData.eMap != EMap.Desert) return;
        if (runMap != null && config.mapData.Pointer == runMap.Pointer)
        {
            // Native RestartRun reuses the same RunConfig object. Re-arm the
            // first-stage override after a previous transition restored it.
            runMap.randomObjectsOverride = elwynnPalette;
            overrideRestored = false; nativeGenerateCalls = 0;
            ResetPlacementTelemetry();
            return;
        }
        var assets = Plugin.EnsureAssets();
        if (assets == null || !assets.AllAssetsValid) throw new InvalidOperationException("Original Elwynn assets are not loaded");
        ReleaseVariants();
        originalMap = config.mapData;
        if (originalMap.stages == null || originalMap.stages.Length == 0) throw new InvalidOperationException("Native Desert has no first stage");
        originalStage = originalMap.stages[0];
        if (originalStage == null) throw new InvalidOperationException("Native Desert first stage is absent");
        runMap = Retain(Object.Instantiate(originalMap));
        runMap.name = originalMap.name + "_ElwynnLab";
        elwynnStage = Retain(Object.Instantiate(originalStage));
        elwynnStage.name = originalStage.name + "_ElwynnLab";
        // Unity Instantiate deep-copies embedded serialized classes. Restore
        // exact native gameplay references rather than relying on equal data.
        elwynnStage.stageTimeline = originalStage.stageTimeline;
        elwynnStage.stageTilePrefabs = originalStage.stageTilePrefabs;
        elwynnStage.mapParameters = originalStage.mapParameters;
        var stages = originalMap.stages.ToArray(); stages[0] = elwynnStage;
        runMap.stages = stages;
        nativeOverride = originalMap.randomObjectsOverride;
        overrideRestored = false;
        nativeObjectLayer = FindObjectLayer(originalStage.randomMapObjects, nativeOverride);
        ApplyGroundPalette(assets, elwynnStage);
        BuildPalette(assets);
        elwynnStage.randomMapObjects = elwynnPalette;
        // MapData overrides StageData whenever it is populated. Scope this field
        // to the private run clone and restore its native value before stage 1.
        runMap.randomObjectsOverride = elwynnPalette;
        var forest = DataManager.Instance?.GetMap(EMap.Forest);
        var forestStage = forest?.stages?.Length > 0 ? forest.stages[0] : null;
        if (forestStage != null)
        {
            elwynnStage.skybox = forestStage.skybox;
            elwynnStage.fogColor = forestStage.fogColor;
            elwynnStage.fogIntensity = forestStage.fogIntensity * .65f;
            elwynnStage.ambienceColor = forestStage.ambienceColor;
            elwynnStage.lightColor = forestStage.lightColor;
            elwynnStage.lightIntensity = forestStage.lightIntensity;
            // Native grass shader and placement remain in use. Its source
            // material is cloned so tinting this biome cannot affect Forest.
            if (forestStage.grassMaterial != null)
            {
                elwynnStage.grassMaterial = Retain(new Material(forestStage.grassMaterial));
                elwynnStage.grassMaterial.name = Prefix + "NativeGrass";
            }
        }
        elwynnStage.particles = null;
        config.mapData = runMap;
        if (config.stageData != null && config.stageData.Pointer == originalStage.Pointer) config.stageData = elwynnStage;
        nativeGenerateCalls = 0; ResetPlacementTelemetry();
        Plugin.Note("First stage uses a private Elwynn palette clone; native generator, map parameters, stage timeline, enemy spawn, movement, ground colliders, special tiles and boss portals retained. " + Describe());
    }

    public static void RestoreNativePaletteForLaterStage()
    {
        if (runMap == null || overrideRestored || MapController.GetStageIndex() < 0) return;
        if (MapController.currentMap == null || MapController.currentMap.Pointer != runMap.Pointer) return;
        runMap.randomObjectsOverride = nativeOverride;
        overrideRestored = true;
        Plugin.Note("Native map override restored before leaving Elwynn stage; later stages and final arena retain their original palettes.");
    }

    static int FindObjectLayer(params Il2CppReferenceArray<RandomMapObject>[] palettes)
    {
        foreach (var palette in palettes)
            if (palette != null) foreach (var entry in palette)
                if (entry?.prefabs != null) foreach (var prefab in entry.prefabs)
                    // The native probe confirms layer 8 is GROUND (landmarks),
                    // and layer 12 is OBJECT (rocks/cacti/tree obstacles).
                    if (prefab != null && prefab.layer == 12) return prefab.layer;
        var layer = LayerMask.NameToLayer("Object");
        if (layer < 0) layer = LayerMask.NameToLayer("Objects");
        if (layer < 0) throw new InvalidOperationException("Could not identify the native map-object collision layer");
        return layer;
    }

    static void BuildPalette(BiomeAssets assets)
    {
        variants = Retain(new GameObject(Prefix + "VariantLibrary"));
        variants.SetActive(false); Object.DontDestroyOnLoad(variants);
        var palette = new List<RandomMapObject>();
        var native = nativeOverride?.Length > 0 ? nativeOverride : originalStage.randomMapObjects;
        // Ground landmarks are part of native traversable geometry. Keep their
        // exact native placement order and parameters before decorative groups.
        if (native != null) foreach (var entry in native)
        {
            if (entry?.prefabs == null || !entry.prefabs.Any(g => g != null && g.layer == 8)) continue;
            var terrainPrefabs = entry.prefabs.Select(CloneNativeGroundPrefab).ToArray();
            palette.Add(CopyGroup(entry, terrainPrefabs));
        }
        void Group(string[] ids, float height, int amount, int maxAmount, float checkRadius, float maxSlope, string collider)
        {
            var prefabs = ids.Select(id => MakeVariant(assets, id, height, collider)).ToArray();
            var group = new RandomMapObject
            {
                prefabs = prefabs, amount = amount, maxAmount = maxAmount,
                scaleMin = .85f, scaleMax = 1.15f, checkRadius = checkRadius,
                maxSlopeAngle = maxSlope, upOffset = 0,
                randomRotationVector = new Vector3(0, 1, 0), alignWithNormal = false
            };
            decorGroups.Add(group.Pointer, decorGroups.Count);
            palette.Add(group);
        }
        Group(new[] { "elwynntreecanopy01", "elwynntreecanopy02", "elwynntreecanopy03", "elwynntreecanopy04" }, 25, 70, 90, 5.5f, 25, "trunk");
        Group(new[] { "elwynntreemid01" }, 14, 22, 30, 3.5f, 28, "trunk");
        Group(new[] { "elwynnpine01", "elwynnpine02" }, 16, 22, 30, 3.5f, 28, "trunk");
        Group(new[] { "elwynnbush09" }, 1.7f, 50, 65, 1.4f, 35, "none");
        Group(new[] { "elwynncliffrock01", "elwynncliffrock02", "elwynnrock2" }, 3.2f, 26, 38, 2.6f, 32, "rock");
        Group(new[] { "elwynnwoodfence01" }, 2.5f, 6, 9, 3f, 12, "none");
        Group(new[] { "elwynncampfire" }, 1.2f, 4, 6, 2f, 15, "none");
        Group(new[] { "westfallbarrel01", "barrel01" }, 1.7f, 10, 15, 1.5f, 20, "none");
        // Preserve native interactive objects even if an asset author placed
        // one in the decorative palette rather than GenerateInteractables.
        if (native != null) foreach (var entry in native)
        {
            if (entry?.prefabs == null) continue;
            bool interactive = entry.prefabs.Any(g => g != null && IsInteractiveName(g.name));
            if (interactive) palette.Add(entry);
        }
        elwynnPalette = palette.ToArray();
    }

    static RandomMapObject CopyGroup(RandomMapObject source, GameObject[] prefabs) => new()
    {
        amount = source.amount, maxAmount = source.maxAmount, checkRadius = source.checkRadius,
        scaleMin = source.scaleMin, scaleMax = source.scaleMax, maxSlopeAngle = source.maxSlopeAngle,
        upOffset = source.upOffset, alignWithNormal = source.alignWithNormal,
        randomRotationVector = source.randomRotationVector, prefabs = prefabs
    };

    static GameObject CloneNativeGroundPrefab(GameObject source)
    {
        if (source == null) return null;
        var clone = Retain(Object.Instantiate(source));
        clone.name = "AzerothElwynnTerrain_" + source.name;
        clone.transform.SetParent(variants.transform, false);
        int renderers = 0, stageMaterialTables = 0;
        void Visit(Transform t)
        {
            // The non-recursive MeshRenderer wrapper is present in this build;
            // the generic GetComponentInChildren<T>(bool) path is stripped.
            var renderer = t.gameObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                int materialCount = renderer.sharedMaterials?.Length ?? 0;
                if (materialCount > 0)
                {
                    renderer.sharedMaterials = Enumerable.Repeat(elwynnStage.triplanarMaterial, materialCount).ToArray();
                    renderers++;
                }
            }
            var stageMaterial = t.gameObject.GetComponent<ObjectStageMaterial>();
            if (stageMaterial?.stageMaterials != null)
            {
                // Clone the serialized table on our prefab only. The native
                // Start callback may otherwise bind its original sand again.
                stageMaterial.stageMaterials = stageMaterial.stageMaterials.Select(entry => entry == null ? null : new ObjectStageMaterial.StageMaterial
                {
                    stageIndex = entry.stageIndex,
                    material = entry.stageIndex == 0 ? elwynnStage.triplanarMaterial : entry.material
                }).ToArray();
                stageMaterialTables++;
            }
            for (int i = 0; i < t.childCount; i++) Visit(t.GetChild(i));
        }
        Visit(clone.transform);
        clone.SetActive(true);
        preservedTerrain.Add(new { source = source.name, clone = clone.name, source.layer, renderers, stageMaterialTables, nativeGeometryAndCollidersRetained = true });
        return clone;
    }

    static bool IsInteractiveName(string name)
    {
        string lower = name.ToLowerInvariant();
        return new[] { "pot", "chest", "shrine", "rail", "portal", "spawner", "tomb", "interactable", "barrelbreak" }.Any(lower.Contains);
    }

    static GameObject MakeVariant(BiomeAssets assets, string id, float height, string collider)
    {
        if (!assets.Props.TryGetValue(id, out var source) || !assets.PropBounds.TryGetValue(id, out var bounds) || bounds.size.y < .01f)
            throw new InvalidDataException("Original Elwynn source model unavailable: " + id);
        var root = Retain(new GameObject(Prefix + id + "_variant"));
        root.transform.SetParent(variants.transform, false);
        root.layer = nativeObjectLayer; root.isStatic = false;
        var marker = root.AddComponent<ElwynnPropMarker>(); marker.SourceId = id;
        var visual = Object.Instantiate(source);
        visual.name = "OriginalElwynnVisual";
        visual.transform.SetParent(root.transform, false);
        float scale = height / bounds.size.y;
        visual.transform.localScale = Vector3.one * scale;
        visual.transform.localPosition = new Vector3(0, -bounds.min.y * scale, 0);
        visual.SetActive(true);
        var size = bounds.size * scale;
        if (collider == "trunk")
        {
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.radius = .55f; capsule.height = 4; capsule.center = new Vector3(0, 2, 0);
        }
        else if (collider == "rock")
        {
            var box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(Mathf.Max(.7f, size.x * .55f), Mathf.Max(.6f, size.y * .8f), Mathf.Max(.7f, size.z * .55f));
            box.center = new Vector3(bounds.center.x * scale, box.size.y * .5f, bounds.center.z * scale);
        }
        // The root is activeSelf beneath an inactive persistent library. Native
        // Instantiate(position,rotation) detaches it, creating an active clone.
        root.SetActive(true);
        variantDiagnostics.Add(new { source = id, height, scale, nativeObjectLayer, collider, visualBounds = new[] { size.x, size.y, size.z } });
        return root;
    }

    static void ApplyGroundPalette(BiomeAssets assets, StageData stage)
    {
        Material Clone(Material source, string id, string role)
        {
            if (source == null) return null;
            var material = Retain(new Material(source)); material.name = Prefix + role;
            var texture = assets.GroundTextures[id];
            var changed = new List<string>();
            foreach (var property in material.GetTexturePropertyNames())
            {
                if (!IsGroundAlbedoProperty(property)) continue;
                string sourceTexture = property == "_BelowYTexture" ? "dirt" : property == "_MainTexSide" ? "rock" : id;
                material.SetTexture(property, assets.GroundTextures[sourceTexture]); changed.Add(property);
            }
            if (changed.Count == 0) throw new InvalidOperationException("Native ground shader exposes no verified albedo texture property: " + source.shader?.name + ", " + role);
            if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            ApplyVerifiedGroundScale(material);
            materialChanges.Add(new { role, source = source.name, shader = source.shader?.name, texture = texture.name, properties = changed, groundScale = GroundScale(material) });
            return material;
        }
        var flat = stage.flatMaterials;
        if (flat != null)
        {
            var ids = new[] { "grass", "grass", "flowers", "grass", "dirt", "grass", "flowers", "cobble" };
            stage.flatMaterials = flat.Select((m, i) => Clone(m, ids[i % ids.Length], "Flat_" + i)).ToArray();
        }
        stage.m_fillMiddle = Clone(stage.m_fillMiddle, "rock", "FillMiddle");
        stage.m_fillTop = Clone(stage.m_fillTop, "grass", "FillTop");
        stage.m_fillMiddleEdge = Clone(stage.m_fillMiddleEdge, "rock", "FillMiddleEdge");
        stage.m_fillTopEdge = Clone(stage.m_fillTopEdge, "grass", "FillTopEdge");
        stage.m_stairs = Clone(stage.m_stairs, "dirt", "Stairs");
        stage.triplanarMaterial = Clone(stage.triplanarMaterial, "grass", "Triplanar");
    }

    // Filled from the installed game's shader property inventory, never by
    // assigning every texture slot (which would overwrite normal/mask maps).
    static bool IsGroundAlbedoProperty(string property) => property is "_MainTex" or "_BelowYTexture" or "_MainTexSide";

    static void ApplyVerifiedGroundScale(Material material)
    {
        // The installed Toon/Lit Tri Planar Normal shader uses these scalar
        // world-space frequencies; other material properties remain native.
        if (material.HasProperty("_Scale")) material.SetFloat("_Scale", ElwynnGroundScale);
        if (material.HasProperty("_SideScale")) material.SetFloat("_SideScale", ElwynnSideScale);
    }

    static object GroundScale(Material material) => new
    {
        scale = material != null && material.HasProperty("_Scale") ? (float?)material.GetFloat("_Scale") : null,
        sideScale = material != null && material.HasProperty("_SideScale") ? (float?)material.GetFloat("_SideScale") : null
    };

    public static string Describe() => JsonSerializer.Serialize(new
    {
        enabled = Plugin.Enabled, active = IsElwynnStage, paletteVersion = PaletteVersion,
        map = runMap?.name, stage = elwynnStage?.name,
        stageCount = runMap?.stages?.Length, nativeStageTimeline = elwynnStage?.stageTimeline?.Pointer.ToInt64() == originalStage?.stageTimeline?.Pointer.ToInt64(),
        nativeMapParameters = elwynnStage?.mapParameters?.Pointer.ToInt64() == originalStage?.mapParameters?.Pointer.ToInt64(),
        nativeSpecialTiles = elwynnStage?.stageTilePrefabs?.Pointer.ToInt64() == originalStage?.stageTilePrefabs?.Pointer.ToInt64(),
        nativeObjectLayer, paletteGroups = elwynnPalette?.Length, nativeGenerateCalls, nativeAmountMultiplier,
        nativeSpawnerCalls, decorGroupCalls, seededUniqueGroups = seededGroups.Count, expectedDecorGroups = decorGroups.Count,
        nativeRngRestores, nativeRngRestoreFailures,
        rngIsolationVerified = decorGroups.Count > 0 && seededGroups.Count == decorGroups.Count && nativeRngRestores == decorGroupCalls && nativeRngRestoreFailures == 0,
        nativeMapSeed = MapGenerationController.mapSeed, decorSeed, overrideRestored,
        groundScale = GroundScale(elwynnStage?.triplanarMaterial),
        materials = materialChanges, variants = variantDiagnostics, preservedTerrain,
        terrainRendererBindings = InspectTerrainBindings(),
        finalGroundChanges,
        groundRendererAudit = InspectGroundRenderers()
    });

    static bool IsNativeDesertTerrainMaterial(Material material)
    {
        if (material == null) return false;
        if (material.name.StartsWith("m_1_TerrainDesert", StringComparison.Ordinal)) return true;
        foreach (var property in new[] { "_MainTex", "_BelowYTexture", "_MainTexSide" })
        {
            if (!material.HasProperty(property)) continue;
            var texture = material.GetTexture(property);
            if (texture != null && texture.name is "SandNew" or "SandRockNew") return true;
        }
        return false;
    }

    static bool AncestorNamed(Transform transform, string prefix)
    {
        while (transform != null)
        { if (transform.name.StartsWith(prefix, StringComparison.Ordinal)) return true; transform = transform.parent; }
        return false;
    }

    static string GroundRole(MeshRenderer renderer, MapGenerationController generator)
    {
        if (generator == null) return null;
        if (generator.proceduralMeshRenderer != null && renderer.Pointer == generator.proceduralMeshRenderer.Pointer) return "proceduralMeshRenderer";
        if (generator.proceduralMapWorldEdges != null && renderer.transform.IsChildOf(generator.proceduralMapWorldEdges.transform)) return "proceduralMapWorldEdges";
        if (generator.mapEdges != null)
        {
            if (generator.mapEdges.edgeTop != null && renderer.Pointer == generator.mapEdges.edgeTop.Pointer) return "mapEdges.edgeTop";
            if (generator.mapEdges.edgeBot != null && renderer.Pointer == generator.mapEdges.edgeBot.Pointer) return "mapEdges.edgeBot";
        }
        if (AncestorNamed(renderer.transform, "AzerothElwynnTerrain_")) return "nativeGroundLandmark";
        var node = renderer.transform;
        while (node != null)
        {
            if (node.gameObject.layer == 8) return "nativeGroundLayer";
            node = node.parent;
        }
        return null;
    }

    static IEnumerable<(MeshRenderer Renderer, string Role)> GroundRenderers(MapGenerationController generator)
    {
        if (generator == null) yield break;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy || !renderer.gameObject.scene.IsValid() || renderer.gameObject.scene.handle != scene.handle) continue;
            if (AncestorNamed(renderer.transform, Prefix)) continue;
            var role = GroundRole(renderer, generator);
            if (role != null) yield return (renderer, role);
        }
    }

    public static int ApplyFinalTerrainBindings(MapGenerationController generator)
    {
        if (!IsElwynnStage || generator == null || elwynnStage?.triplanarMaterial == null) return 0;
        int changed = 0;
        var interactables = Object.FindObjectsOfType<BaseInteractable>();
        foreach (var (renderer, role) in GroundRenderers(generator))
        {
            if (interactables.Any(x => x != null && renderer.transform.IsChildOf(x.transform))) continue;
            var materials = renderer.sharedMaterials.ToArray(); bool rebound = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!IsNativeDesertTerrainMaterial(source)) continue;
                if (!finalGroundMaterials.TryGetValue(source.Pointer, out var replacement) || replacement == null)
                {
                    // Preserve the actual world-edge/ground shader, normal,
                    // noise and render state. Replace the verified Desert
                    // albedo slots and the measured world-space frequencies.
                    replacement = Retain(new Material(source));
                    replacement.name = Prefix + "GroundFinal_" + source.name;
                    foreach (var property in replacement.GetTexturePropertyNames())
                    {
                        if (!IsGroundAlbedoProperty(property)) continue;
                        var previous = replacement.GetTexture(property);
                        if (previous == null || (previous.name is not "SandNew" and not "SandRockNew" && !source.name.StartsWith("m_1_TerrainDesert", StringComparison.Ordinal))) continue;
                        string id = property == "_MainTexSide" || previous.name == "SandRockNew" ? "rock" : property == "_BelowYTexture" ? "dirt" : "grass";
                        replacement.SetTexture(property, Plugin.EnsureAssets().GroundTextures[id]);
                    }
                    ApplyVerifiedGroundScale(replacement);
                    finalGroundMaterials[source.Pointer] = replacement;
                }
                materials[i] = replacement; rebound = true; changed++;
                finalGroundChanges.Add(new { role, renderer = renderer.name, source = source.name, replacement = replacement.name, shaderRetained = replacement.shader.Pointer == source.shader.Pointer, groundScale = GroundScale(replacement) });
            }
            if (rebound) renderer.sharedMaterials = materials;
        }
        if (changed > 0) Plugin.Note("Rebound verified original Desert ground/world-edge albedo slots=" + changed + "; shaders, geometry, colliders and interactables retained.");
        return changed;
    }

    public static object[] InspectGroundRenderers()
    {
        var generator = Object.FindObjectOfType<MapGenerationController>();
        return GroundRenderers(generator).Select(pair => new
        {
            role = pair.Role, renderer = pair.Renderer.name, layer = pair.Renderer.gameObject.layer,
            bounds = new[] { pair.Renderer.bounds.center.x, pair.Renderer.bounds.center.y, pair.Renderer.bounds.center.z, pair.Renderer.bounds.size.x, pair.Renderer.bounds.size.y, pair.Renderer.bounds.size.z },
            materials = pair.Renderer.sharedMaterials.Select(m => m == null ? null : new
            {
                m.name, shader = m.shader?.name, originalDesertTerrain = IsNativeDesertTerrainMaterial(m), groundScale = GroundScale(m),
                textures = m.GetTexturePropertyNames().Select(p => new { property = p, texture = m.GetTexture(p)?.name }).ToArray()
            }).ToArray()
        }).Cast<object>().ToArray();
    }

    static object[] InspectTerrainBindings()
    {
        var rows = new List<object>();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        foreach (var renderer in Resources.FindObjectsOfTypeAll<MeshRenderer>())
        {
            if (renderer == null || !renderer.gameObject.activeInHierarchy || !renderer.gameObject.scene.IsValid() || renderer.gameObject.scene.handle != scene.handle) continue;
            Transform root = renderer.transform;
            while (root != null && !root.name.StartsWith("AzerothElwynnTerrain_", StringComparison.Ordinal)) root = root.parent;
            if (root == null) continue;
            var stageMaterial = renderer.GetComponent<ObjectStageMaterial>();
            rows.Add(new
            {
                root = root.name, renderer = renderer.name,
                hasObjectStageMaterial = stageMaterial != null,
                materials = renderer.sharedMaterials.Select(m => m == null ? null : new
                {
                    m.name, shader = m.shader?.name,
                    top = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex")?.name : null,
                    side = m.HasProperty("_MainTexSide") ? m.GetTexture("_MainTexSide")?.name : null
                }).ToArray()
            });
        }
        return rows.ToArray();
    }

    static void ResetPlacementTelemetry()
    {
        nativeSpawnerCalls = 0; decorGroupCalls = 0; nativeRngRestores = 0; nativeRngRestoreFailures = 0;
        decorSeed = 0; seededGroups.Clear();
    }

    public static void ReleaseVariants()
    {
        if (variants != null) { variants.SetActive(false); Object.Destroy(variants); }
        foreach (var value in owned) if (value != null && value != variants) Object.Destroy(value);
        owned.Clear(); materialChanges.Clear(); variantDiagnostics.Clear(); preservedTerrain.Clear(); decorGroups.Clear(); finalGroundMaterials.Clear(); finalGroundChanges.Clear();
        ResetPlacementTelemetry();
        variants = null; originalMap = null; runMap = null; originalStage = null; elwynnStage = null;
        nativeOverride = null; elwynnPalette = null;
    }

    internal static void NoteNativePlacement(Il2CppReferenceArray<RandomMapObject> objects, float multiplier)
    {
        if (!IsElwynnStage || objects == null || elwynnPalette == null || objects.Pointer != elwynnPalette.Pointer) return;
        decorSeed = unchecked(MapGenerationController.mapSeed ^ (int)0x6E27794D ^ PaletteVersion);
        nativeGenerateCalls++; nativeAmountMultiplier = multiplier;
        Plugin.Note($"Native RandomObjectPlacer.Generate palette={objects.Length}; multiplier={multiplier}; mapSeed={MapGenerationController.mapSeed}; decorSeed={decorSeed}; ground landmark RNG retained, individual decoration groups restore native RNG afterwards.");
    }

    internal static bool BeginDecorGroup(RandomMapObject group, float multiplier, out Random.State saved)
    {
        saved = default;
        if (!IsElwynnStage) return false;
        nativeSpawnerCalls++;
        if (group == null || !decorGroups.TryGetValue(group.Pointer, out int index)) return false;
        decorSeed = unchecked(MapGenerationController.mapSeed ^ (int)0x6E27794D ^ PaletteVersion);
        nativeAmountMultiplier = multiplier;
        saved = Random.state;
        int seed = unchecked(decorSeed + index * (int)0x9E3779B9);
        Random.InitState(seed);
        decorGroupCalls++; seededGroups.Add(group.Pointer);
        Plugin.Note($"Native RandomObjectSpawner decorative group={index}, seed={seed}, mapSeed={MapGenerationController.mapSeed}, multiplier={multiplier}; RNG captured.");
        return true;
    }

    internal static void RestoreDecorRng(Random.State saved)
    {
        Random.state = saved; nativeRngRestores++;
        if (!Random.state.Equals(saved))
        {
            nativeRngRestoreFailures++;
            Plugin.Note("ERROR native RNG restoration did not preserve the captured state");
        }
    }

    public static string InspectNativeMaterials()
    {
        try
        {
            var map = DataManager.Instance?.GetMap(EMap.Desert);
            var stage = map?.stages?.Length > 0 ? map.stages[0] : MapController.currentStage;
            if (stage == null) return "{\"ready\":false}";
            var materials = new List<object>();
            void Add(string role, Material m)
            {
                if (m == null) { materials.Add(new { role, missing = true }); return; }
                var properties = new List<object>();
                foreach (var p in m.GetTexturePropertyNames())
                {
                    var t = m.GetTexture(p); var scale = m.GetTextureScale(p);
                    properties.Add(new { property = p, texture = t?.name, type = t?.GetIl2CppType()?.Name, scale = new[] { scale.x, scale.y } });
                }
                materials.Add(new { role, name = m.name, shader = m.shader?.name, textures = properties });
            }
            if (stage.flatMaterials != null) for (var i = 0; i < stage.flatMaterials.Length; i++) Add("flat" + i, stage.flatMaterials[i]);
            Add("fillMiddle", stage.m_fillMiddle); Add("fillTop", stage.m_fillTop); Add("fillMiddleEdge", stage.m_fillMiddleEdge); Add("fillTopEdge", stage.m_fillTopEdge); Add("stairs", stage.m_stairs); Add("triplanar", stage.triplanarMaterial); Add("grass", stage.grassMaterial); Add("skybox", stage.skybox);
            object Props(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Assets.Scripts.Game.MapGeneration.RandomMapObject> props)
            {
                if (props == null) return Array.Empty<object>();
                return props.Select(p => p == null ? null : new { p.amount, p.maxAmount, p.checkRadius, p.scaleMin, p.scaleMax, p.maxSlopeAngle, p.upOffset, p.alignWithNormal, prefabs = p.prefabs?.Select(g => g == null ? null : new { g.name, g.layer, scale = new[] { g.transform.localScale.x, g.transform.localScale.y, g.transform.localScale.z } }).ToArray() }).ToArray();
            }
            return JsonSerializer.Serialize(new { ready = true, map = map?.name, stage = stage.name, mapType = map?.mapType.ToString(), materials, stageProps = Props(stage.randomMapObjects), mapProps = Props(map?.randomObjectsOverride), flatTiles = stage.stageTilePrefabs?.flatTilePrefabs?.Select(g => g?.name).ToArray(), specificTiles = stage.stageTilePrefabs?.mapSpecificTilesPrefabs?.Select(g => g?.name).ToArray(), stage.fogIntensity, stage.lightIntensity });
        }
        catch (Exception ex) { return JsonSerializer.Serialize(new { ready = false, error = ex.ToString() }); }
    }
}

[HarmonyPatch(typeof(MapController), nameof(MapController.StartNewMap))]
static class ElwynnRunPalettePatch
{
    static void Prefix(RunConfig newRunConfig)
    {
        if (!Plugin.Enabled) return;
        try { NativeBiomeIntegration.InstallOnRunConfig(newRunConfig); }
        catch (Exception ex) { Plugin.Note("Elwynn palette installation failed: " + ex); throw; }
    }
}

[HarmonyPatch(typeof(MapController), nameof(MapController.LoadNextStage))]
static class ElwynnLaterStagePatch
{ static void Prefix() => NativeBiomeIntegration.RestoreNativePaletteForLaterStage(); }

[HarmonyPatch(typeof(MapController), nameof(MapController.LoadFinalStage))]
static class ElwynnFinalStagePatch
{ static void Prefix() => NativeBiomeIntegration.RestoreNativePaletteForLaterStage(); }

[HarmonyPatch(typeof(RandomObjectPlacer), nameof(RandomObjectPlacer.Generate))]
static class ElwynnNativePlacementPatch
{
    static void Prefix(Il2CppReferenceArray<RandomMapObject> objects, float amountMultiplier)
        => NativeBiomeIntegration.NoteNativePlacement(objects, amountMultiplier);
}

[HarmonyPatch(typeof(RandomObjectPlacer), "RandomObjectSpawner")]
static class ElwynnDecorGroupSeedPatch
{
    sealed class PlacementState { public bool changed; public Random.State saved; }
    static void Prefix(RandomMapObject randomObject, float amountMultiplier, out PlacementState __state)
    {
        __state = new PlacementState();
        __state.changed = NativeBiomeIntegration.BeginDecorGroup(randomObject, amountMultiplier, out __state.saved);
    }
    static Exception Finalizer(Exception __exception, PlacementState __state)
    { if (__state?.changed == true) NativeBiomeIntegration.RestoreDecorRng(__state.saved); return __exception; }
}

[HarmonyPatch(typeof(StageData), nameof(StageData.GetName))]
static class ElwynnStageDisplayNamePatch
{
    static void Postfix(StageData __instance, ref string __result)
    {
        if (NativeBiomeIntegration.IsElwynnStage && __instance.Pointer == MapController.currentStage.Pointer)
            __result = "Элвиннский лес";
    }
}
