# Procedural Azeroth biomes

Implementation: 2026-10-21, Elwynn biome v1. The `-azeroth-elwynn` flag replaces the first native Desert stage's environment with locally extracted original Elwynn assets. Later stages keep their original environments. Other native maps are unchanged.

## Native gameplay retained

The first stage uses private clones of the native `MapData` and `StageData`. Its original `mapParameters`, `stageTimeline` and `stageTilePrefabs` references are retained explicitly. The existing native procedural mesh generator, ground colliders, movement controller, combat AI, enemy spawning, interactables, boss gates and portal progression continue to run.

The original `RandomObjectSpawner` places eight groups of Elwynn decoration. Each group derives a seed from the native map seed and biome palette version; its prefix/finalizer saves and restores `UnityEngine.Random.state`. Native ground landmarks remain in the palette with their own geometry, collision and original placement parameters. The run clone's map-wide palette override is restored before advancing to the next stage.

## Original local assets

`scripts/elwynn-biome/build_elwynn.py` reads the installed Azeroth MPQ archives and exports the ignored `private-assets/elwynn-biome` pack. `validate_elwynn.py` independently compares hashes, geometry, UVs, normals, material passes, alpha pixels and texture orientation to those archives.

The pack contains six original ground textures and fifteen original static M2 props: canopy trees, a medium tree, pines, shrubs, rocks, fences, campfire stones/logs and barrels. Source aliases are resolved explicitly: ADT references omit the `_s.blp` suffix and environment references can use `.mdx` where the actual archive entry is `.m2`.

The native triplanar shader retains its noise, normal maps and render state. Its top/dirt/side albedos use original Elwynn grass, dirt and rock. Ground textures have generated mipmaps, trilinear filtering and anisotropy4. Private shader scales are `0.15` for the ground and `0.12` for sides. Generated world-edge sand materials are audited and rebound after native initialization. Shared native materials are never edited.

Foliage preserves original AlphaKey passes and two-sided cards. Runtime source meshes, materials, textures and prototype libraries survive `Resources.UnloadUnusedAssets` and are released when the plugin unloads.

## Passage protection

After generation, the adapter reserves the actual player spawn, native interactables, boss gates, portals and rails. It disables only new decoration that intersects those zones. A conservative surface graph uses the actual native floor layer, native player capsule dimensions and real physics queries to find a walking corridor to the boss gate and remove new obstacles from it.

If that native baseline cannot be proved within the loading-time budget, the adapter disables only added solid decoration colliders. Visible trees and rocks remain. This preserves the native map's own traversability without claiming that an unproved route passed. The report exposes the reason, masks, capsule dimensions, removed objects, preserved interactable counts, route result and fallback count.

## Launch and local preparation

1. Export and validate the private pack with the two Python scripts.
2. Build `src/ElwynnBiome/ElwynnBiome.csproj` against the installed BepInEx IL2CPP interop assemblies.
3. Copy the pack contents to `Megabonk-Lab/AzerothElwynnAssets` and the compiled DLL to `Megabonk-Lab/BepInEx/plugins/ElwynnBiome`.
4. Run `Start-Elwynn-Mage.cmd` for the prepared Mage battle, with all four book slots and the existing maximum talent allocation. The normal quick launchers select this biome too.

Normal launchers do not use the batch-only seed, movement, boss or capture probes. Local logs are written to `reports/elwynn/biome-*.log`. Source game archives, extracted assets, native binaries, probe runtimes and captured frames remain excluded from Git.

## Scope

This first location is Megabonk's generated terrain dressed with original Elwynn art. Original ADT heightmaps, WMO buildings/interiors, source campfire particles and tree wind are not part of v1. Their source files were researched; their existence does not imply they have been integrated. Terrain topology, movement and spawning remain native Megabonk behavior.

Actual build and runtime results are recorded in `ELWYNN_TEST_LOG.md`; earlier failed probes are preserved in local reports and the module logs.
