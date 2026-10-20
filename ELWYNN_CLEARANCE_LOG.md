# Elwynn decoration clearance

## 2026-10-21 — native API audit and implementation

Scope: `src/ElwynnBiome/BiomeClearance.cs` only. This module preserves native generation, terrain, movement, enemy spawning, interactables, and rails. Its writes affect only scene instances of new source decoration whose root names begin `AzerothElwynn_` and their own colliders. Extracted assets and decompiled code are not included in this module.

Verified installed Megabonk metadata (`scripts/megabonk-research/Il2CppDumper/dump.cs`):

- `MapGenerationController` TypeDef4734: `tileSpawnPosition`, `mapSeed`, `isGenerating`, `randomObjectPlacer`, `proceduralTileGeneration`, `proceduralMapMeshGenerator`, `A_GenerationComplete`. Native postgeneration ordering remains owned by the main integration module; call clearance only after native interactables exist and the player has a valid collider.
- `RandomObjectPlacer` TypeDef4737: native `center` and `size` are generated world placement bounds. Fallback bounds come from `ProceduralTileGeneration.GetWorldSize()/GetWorldCenter()` or native procedural mesh collider bounds.
- `PlayerMovement` TypeDef4631: `GetPlayerCollider()`, `GetPlayerRadius()`. The probe uses runtime capsule dimensions, with 0.18 world units of horizontal clearance margin. It does not change the player collider.
- `BaseInteractable` TypeDef4756 is the common parent of native chests, shrines, boss spawners and portals. Native boss targets are `InteractableBossSpawner` and `InteractableBossSpawnerFinal`.
- `Rail` TypeDef4742: `renderCollider` describes native rail bounds. When a disabled collision mesh has empty bounds, renderer ancestry supplies the full track footprint.
- `GameManager.whatIsGround` supplies native floor collision layers. `whatIsGroundAndObjects & ~whatIsGround` supplies native obstacle layers. No layer number is hard-coded.

Runtime contract:

- Register `ElwynnPropMarker` with `ClassInjector.RegisterTypeInIl2Cpp<ElwynnPropMarker>()` before creating source prototypes. `SourceId` is a diagnostic hint only: native `Instantiate` need not clone a managed field. Root name prefix remains mandatory.
- New prototypes may have an active self flag under an inactive persistent parent; inactive prototypes are excluded because discovery requires a valid scene and `activeInHierarchy`.
- Call `BiomeClearance.Apply(MapGenerationController generator)` once after generation. It returns `ClearanceReport`; `LastReport` and `DiagnosticsJson` expose the latest result.
- Source colliders are found through a typed native resource scan and transform ancestry. The module avoids this build's invalid stripped `Component.GetComponentInChildren<T>(bool)` wrapper.

Implemented checks:

1. Reserve both the native tile spawn and actual player feet, every active native interactable, an 11-unit minimum boss gate/portal area, and each full rail footprint. Deactivate only new decoration overlapping these zones.
2. Temporarily disable only added decoration colliders while proving a baseline walking route from actual spawn to the native boss gate's approach area. Sample native floors, limit slope to 50 degrees, check both sides of the measured player capsule for holes and height discontinuities, and capsule-cast native obstacle layers. Grid size is bounded to at most 114×114 samples. Route search has a 4-second loading-time budget.
3. Follow sampled floor heights between graph nodes; clear only new decoration intersecting the actual capsule corridor. Restore source colliders and verify that the route remains free of native/static object obstacles.
4. If no conservative walking route can be established (native cliffs, missing data, or budget), leave all visible assets in place but disable only added solid decoration colliders. `NativeRouteVerified=false` remains explicit; this fallback guarantees the added biome cannot introduce a new physics blockade and does not claim the native terrain was proved reachable.

Diagnostics record native map seed, source prop counts/removals, reserved-zone violations, preserved native interactable counts, actual radius/height, masks, graph size/visited nodes, path points/length, baseline vs decorated route status, fallback collider count, reason and elapsed time. `NativeGeometryUntouched=true` describes the module's limited mutation scope, not a claim that the base game's map generation is perfect.

Validation so far: independently compiled against the installed runtime interop assemblies, .NET6 target, Release, zero warnings/errors. No game was launched and no runtime result is claimed by this implementation entry; the owning integration task runs seeded native probes and appends their evidence.

## 2026-10-21 — fix demonstrated by first native preview

The owning task's actual seed27183 preview (`private-assets/elwynn-test-runtime/reports/elwynn/biome-20261021-005014-63112.log`) measured 76.33 world units of native movement, four native enemies, 475 visible source props and all86 native interactables retained. Clearance used its fallback and disabled311 added solid colliders because it could not snap the initial player position to a floor route; no route pass was claimed.

Two assumptions were disproved by that run:

- `GameManager.whatIsGround` and `whatIsGroundAndObjects` both equal4352 (`Ground` layer8 plus `Object` layer12) in this installed game. The earlier subtraction produced obstacleMask0. Clearance now obtains the floor bit from the real generated procedural mesh collider (or real combined native tile collider), then derives obstacleMask from the native combined mask. Named native Ground/player ground-only mask are explicit fallbacks. The next probe records all three original masks and the selected mask source.
- `MapGenerationController.tileSpawnPosition` remains zero for the first ProceduralMesh map. It is not a valid extra spawn in that mode. Only Tiles reserves that field; the actual player spawn remains reserved in both modes.

Additional native code audit confirms `PlayerMovement.GetPlayerCollider()` RVA0x33D4D0 returns `playerCollider` at offset0x140. `Initialize()` RVA0x33DBD0 obtains the actual `CapsuleCollider`, reads its local height at0x33DE4F, and caches its local radius at0x33DE6E. This is not a hurt-box accessor. The preview broadphase bounds height1.44 conflicted with the native capsule local height3.85 observed by the owning probe; clearance must not infer an undocumented collider type from that conflict.

The revised probe synchronizes transforms, measures the actual capsule height/radius/direction with lossyScale and TransformPoint(center), derives its world axis and enclosing upright footprint, and records both analytic shape and native broadphase bounds. It projects measured feet onto the authoritative native floor before testing graph connections. It does not teleport the player or alter the native capsule. Missing ground or a failed route still triggers the same source-collider-only fallback with `NativeRouteVerified=false`.

Validation: revised standalone .NET6 Release build succeeds with zero warnings/errors. Runtime validation remains with the owning task; this entry does not claim the new route has passed yet.

## 2026-10-21 — bypass stripped public capsule cast wrapper

Actual second probe `biome-20261021-010109-56928.log` confirmed floorMask256 and obstacleMask4096, CapsuleCollider local height3.85/radius0.5, upright axis and unit scale, analytic probe radius0.68/height3.85. Player spawn projected successfully to native floor; native broadphase collider bounds were zero during this startup frame. Graph floor sampling found11189 valid nodes. The first capsule edge query failed with `NotSupportedException: Method unstripping failed`, so the conservative collider-only fallback was retained and no route pass was claimed.

Decompiling the installed interop `UnityEngine.PhysicsModule.dll` established the exact cause: public `Physics.CapsuleCastAll(Vector3,Vector3,float,Vector3,float,int,QueryTriggerInteraction)` is a throw-only generated stub. In contrast, `Physics.Query_CapsuleCastAll(PhysicsScene,Vector3,Vector3,float,Vector3,float,int,QueryTriggerInteraction)` is fully generated, calls `Query_CapsuleCastAll_Injected`, and unmarshals the native `BlittableArrayWrapper` into the native RaycastHit array. `OverlapCapsule` is also fully generated through `OverlapCapsule_Internal_Injected`.

Both former public CapsuleCastAll calls now use that existing generated query binding with `Physics.defaultPhysicsScene`. No hand-written P/Invoke, guessed function address, or invented managed physics approximation was introduced. The installed `UnityPlayer.dll` contains the exact `UnityEngine.Physics::Query_CapsuleCastAll_Injected` registration string at file offset0x18A7500 and `OverlapCapsule_Internal_Injected` at0x18A7655. These offsets document binary-string evidence, not callable addresses.

`ClearanceReport.FailureDetails` now records `Exception.ToString()` so any further interop failure includes its full stack; `CapsuleCastApi` identifies the binding used. The updated standalone Release build succeeds with zero warnings/errors. The owning task performs the next real probe; runtime capsule-query success is not claimed here.

## 2026-10-21 — concrete nonalloc buffer avoids broken abstract-array unmarshal

The next real probe (`biome-20261021-010337-67936.log`) reached the native query but `BlittableArrayWrapper.Unmarshal<RaycastHit>` then threw `InvalidOperationException: Instances of abstract classes cannot be created`. Full report stack identifies exactly that generic conversion. The normal public `CapsuleCastNonAlloc` helper also creates an `Il2CppSystem.Span<RaycastHit>`, a separate fragile generic layer already implicated elsewhere in this game.

The final binding now resolves the existing `UnityEngine.PhysicsScene::Internal_CapsuleCastNonAlloc_Injected` icall directly with `Il2CppInterop.Runtime.IL2CPP.ResolveICall`. Its signature was read from the installed generated `PhysicsScene` delegate and injected helper. The exact registration string also exists in installed `UnityPlayer.dll` at file offset0x18AC380. Native span layout is pointer at0, length at8, size16. The installed RaycastHit explicit layout is point@0, normal@12, faceId@24, distance@28, uv@32, colliderId@40, size44.

For each clearance invocation, one concrete CLR `RaycastHit[]` is pinned with a GCHandle, passed as this exact native span, then unpinned in finally. No IL2CPP Span or abstract Il2CppArrayBase unmarshalling is used. The 128-hit starting buffer grows and retries whenever full; saturation at8192 fails to the conservative source-collider-only fallback instead of silently losing hits. Layout sizes are checked before querying. The custom span also avoids a duplicated `ManagedSpanWrapper` type in the installed UnityEngine facade and CoreModule references.

Standalone Release build: zero warnings/errors. No game launch, deployment or runtime PASS claimed by this entry; the owning task performs the real check.

## Final integrated runtime evidence

Final native NonAlloc binding ran without an interop exception. Seed31415 proved243.835m with334 samples and no collider fallback; native seed38203351 proved752.715m with864 samples and removed one added corridor prop. Seed27183 conservatively disabled348 added solid colliders after no native baseline was proved. All runs retained86 native interactables and zero reserved violations. See ELWYNN_TEST_LOG.md.
