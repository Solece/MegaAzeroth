# Elwynn native generation integration

Date: 2026-10-21. Scope: first procedural Desert stage only, selected by `-azeroth-elwynn`.

## Source evidence

Read the installed `scripts/megabonk-research/Il2CppDumper/dump.cs`, the local `script.json` symbol map, and disassembled `Megabonk-Lab/GameAssembly.dll` using existing Python Capstone/pefile dependencies. No original game files, saves, registry or desktop input changed.

- `Assets.Scripts.Managers.MapController.StartNewMap(RunConfig)` RVA `0x4222F0` stores `newRunConfig.mapData` as `currentMap`, sets `index=-1`, and invokes the original `LoadNextStage()`.
- `MapController.LoadNextStage()` RVA `0x421DF0` increments `index`, then selects `currentMap.stages[index]` directly. A prefix before `StartNewMap` can therefore supply a private map with only `stages[0]` cloned, retaining all later stage references.
- `MapGenerationController.Awake()` RVA `0x49A000` starts its native generation coroutine. Its `MoveNext()` RVA `0x4A26F0` preserves the tile generator, terrain mesh/colliders, tile-object placement, world boundaries, spawn platform, boss spawners, portals and interactables.
- `RandomObjectPlacer.RandomObjectSpawner()` RVA `0x49C2A0` calls native `SpawnPositions.GetObjectSpawnPosition`, then `Object.Instantiate(prefab, position, rotation)`. It multiplies the prefab's existing `localScale` by its random scalar and never calls `SetActive`. Active prefab roots beneath an inactive persistent library therefore create visible, active native instances; entirely inactive roots do not.
- Native static prefabs are passed to a static-combining path. Elwynn variant roots use `isStatic=false` so their markers and conservative collision volumes remain identifiable by the post-generation clearance pass.
- `StageData` owns ground materials, lighting/sky/fog, `randomMapObjects`, `stageTimeline`, `mapParameters`, and `stageTilePrefabs`. `MapData.randomObjectsOverride` takes precedence over the stage palette.

## Implementation

`src/ElwynnBiome/NativeBiomeIntegration.cs` clones the selected Desert map and its first stage. It changes the clone's visual ground palette and decorative `RandomMapObject` groups; generation algorithms, movement, enemy spawning, collision meshes, `stageTimeline`, `mapParameters` and `stageTilePrefabs` are shared native references. Native interactive decorative groups are retained. The private map override is restored before leaving stage 0, preserving subsequent stages and final arena; native `RestartRun` re-arms it for stage 0.

Fifteen original source M2 variants are grouped into canopy trees, medium trees, pines, bushes, rocks, fences, campfires and barrels. Source meshes/materials/textures are shared with the retained `BiomeAssets` pack. Variant child transforms normalize source heights and floor offsets. Native random scale remains 0.85–1.15. Trunks use narrow capsule colliders and rocks use conservative boxes on the native map-object layer; small foliage and decorative fences/barrels do not add movement blockers.

Native `RandomObjectPlacer.Generate()` performs placement. Native ground landmarks remain first and use their original native random stream and unchanged amount/scale/slope/collision parameters. Only the eight new Elwynn decorative groups use separate deterministic seeds derived from `mapSeed ^ 0x6E27794D ^ paletteVersion`, offset by the group index. A Harmony finalizer around each native `RandomObjectSpawner` invocation restores the previous `UnityEngine.Random.state` even on an exception. No new decoration random consumption leaks into subsequent native spawning/interactables.

Root plugin contract: `Plugin.Enabled`, `Plugin.EnsureAssets() -> BiomeAssets`, `Plugin.Note(string)`, marker registration before native generation. Integration diagnostics: `InspectNativeMaterials()` (independent of activation/assets), `IsElwynnStage`, `Describe()` and `ReleaseVariants()`.

## Validation status

- Initial standalone compile check against the installed real BepInEx/IL2CPP/Unity assemblies passed with 0 warnings and 0 errors.
- The root agent ran the native material inventory probe in an isolated test copy, leaving the user's active game intact. Actual output: `private-assets/elwynn-test-runtime/reports/elwynn/material-probe.json`.
- The first stage is `MapDesert / StageDesert1`, `EMapType.ProceduralMesh`. Flat/fill/stairs materials are null. The actual terrain shader is `Toon/Lit Tri Planar Normal`; only `_MainTex` (grass), `_BelowYTexture` (dirt), and `_MainTexSide` (rock) are replaced. `_Noise`, `_Normal`, `_NormalT`, and `_Ramp` are preserved.
- The native ground landmark group contains `Ladnmark1`, `Ladnmark2`, `Landmark3`, `Landmark4`, layer 8, amount 8, random scale 1.3–3. These prefabs are cloned with all original geometry/colliders and only their renderer materials rebound to the cloned Elwynn ground material. Clone names use `AzerothElwynnTerrain_` and do not receive prop markers, so clearance never removes ground landmarks. Decorative source trees/rocks use actual native OBJECT layer 12; mask 4352 combines ground layer 8 and object layer 12.
- The first stage's `MapData.randomObjectsOverride`, flat tiles, and specific tiles are empty in the native inventory. Ground landmarks are explicitly preserved from the native stage palette; desert houses/cacti/rocks/logs/trees/bushes are replaced by original Elwynn decoration.
- Updated standalone compile check passed with 0 warnings and 0 errors after actual shader/layer corrections. Display-name postfix changes only the active cloned stage to `Элвиннский лес`.
- Runtime generation, multiple seeds, passage clearance, native boss portal, later-stage restoration and screenshots are owned by the root/clearance agents. This log does not claim they have passed until their actual results are recorded.

## First real generation and corrections

The isolated first preview (`biome-20261021-005014-63112.log`, seed 27183) produced 475 visible source props, 76.33 units of movement through the native controller, native enemy spawning, and valid source assets after explicit unused-asset cleanup. It exposed two implementation assumptions that required correction:

1. Unity `Instantiate(StageData)` deep-copied embedded `stageTimeline` and `stageTilePrefabs` serialized classes. Their data remained native, but object identity differed. The clone now explicitly reassigns `stageTimeline`, `stageTilePrefabs`, and `mapParameters` to the original native references. This guarantees unchanged enemy-spawn timeline and special-tile configuration.
2. Native generation bypasses the standalone `RandomObjectPlacer.Generate` wrapper: the coroutine directly loops stage groups and calls `RandomObjectSpawner` at RVA `0x4A38AF`; its interactables branch also calls the same spawner at `0x4A36AF`. Thus `nativeGenerateCalls=0` was a real unused-hook observation, and the decoration base seed had not been initialized. The private spawner prefix now computes `decorSeed` directly from the current native map seed. Per-group hook hits and native RNG restoration are instrumented with `nativeSpawnerCalls`, `decorGroupCalls`, `seededUniqueGroups`, `expectedDecorGroups`, `nativeRngRestores`, `nativeRngRestoreFailures`, and `rngIsolationVerified`. The expected passing condition is eight unique decoration groups, at least eight group calls, equal restoration/call counts, and zero restore failures.

`Describe().terrainRendererBindings` now inspects active native landmark clone renderers after their `Start` callbacks, recording actual bound material and top/side texture names plus the presence of `ObjectStageMaterial`. This proves whether native component initialization preserves the rebound Elwynn ground material. The native `ObjectStageMaterial.Start` implementation at RVA `0x49AAB0` can reset a renderer from its stage-material table; component presence on these prefabs has not yet been assumed.

All corrections compiled against real IL2CPP/Unity assemblies with 0 warnings and 0 errors before the next root-owned preview. No game launch, deployment or Git operations were performed by this integration agent.

## Remaining sand surface audit

The root's actual `elwynn-overview-27183.png` shows source Elwynn trees and grass on the generated surface, plus a large triangular foreground surface still using Desert sand. The screenshot alone does not establish whether that surface is a world-edge renderer or a native landmark. `Describe().groundRendererAudit` therefore records the actual material and all texture bindings, role, layer and bounds for active ground-layer renderers, the main procedural renderer, `proceduralMapWorldEdges`, `MapEdges`, and cloned native landmarks.

`ApplyFinalTerrainBindings(MapGenerationController)` is a stage-scoped post-generation operation for the root to call after native `Start` callbacks and before clearance. It excludes all new Elwynn prop visuals and any native interactable hierarchy. It clones only verified original Desert terrain materials (`m_1_TerrainDesert*` or actual `SandNew`/`SandRockNew` albedo bindings), preserves the exact source shader/render state/UV controls/normal/noise slots, and replaces only `_MainTex`, `_BelowYTexture`, `_MainTexSide` with original Elwynn textures. Geometry and colliders are unchanged; each changed binding is recorded in `finalGroundChanges`.

For cloned ground prefabs, any actually present `ObjectStageMaterial` now receives a copied stage-material table with its stage-0 material rebound to Elwynn. Other stage entries remain native. The source prefab and its serialized table are never modified. `preservedTerrain[].stageMaterialTables` records component presence instead of assuming it.

This extension compiled with 0 warnings and 0 errors. The root owns its next actual runtime validation.

## Verified triplanar frequency adjustment

Root runtime inspection confirmed the actual `Toon/Lit Tri Planar Normal` scalar properties: original `_Scale=0.015` made the source Elwynn leaf pattern span approximately 66 world units, while `_SideScale=0.04` controlled cliff texture frequency. The private Elwynn triplanar clone and post-generation cloned ground/world-edge materials now set `_Scale=0.15` and `_SideScale=0.12`, each guarded with `Material.HasProperty`. All other floats remain untouched.

Actual bound values are recorded in `Describe().groundScale`, `materials[].groundScale`, `finalGroundChanges[].groundScale`, and `groundRendererAudit[].materials[].groundScale`. A standalone `dotnet build --no-incremental` against installed Unity/IL2CPP assemblies passed with 0 warnings and 0 errors; root project binaries were not rebuilt or changed by this agent.


## Final integrated runtime evidence

Final actual runtime PASS: eight unique decoration groups,16 calls/16 RNG restorations/zero failures; original gameplay references retained; original sand world-edge material rebound once. Native full route reached live Pharaoh3 after restoring the later-stage palette. Different seeds produced different native terrain signatures; complete-world determinism is not claimed. See ELWYNN_TEST_LOG.md.

## V2 native probe correction
First v2 probe PID68224 reached all native decorative groups but GenerateMap raised NullReferenceException after a null map-wide override was supplied. The original uses an empty serialized array. V2 now preserves that shape with Array.Empty<RandomMapObject>() while keeping per-stage palettes; the next native probe owns confirmation. Failed logs retained in private-assets/elwynn-v2-test-runtime/reports.
