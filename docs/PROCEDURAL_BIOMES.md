# Procedural Azeroth biomes

Implementation: 2026-10-21, Elwynn biome v2. The `-azeroth-elwynn` flag replaces the environment of all three ordinary Desert stages with locally extracted original Elwynn assets. The native final arena and other native maps retain their original environments.

## Native gameplay retained

The run uses a private clone of native `MapData` and an individual private `StageData` clone for each ordinary stage. Each stage's original `mapParameters`, `stageTimeline` and `stageTilePrefabs` references are retained explicitly. The existing native procedural mesh generator, ground colliders, movement controller, combat AI, enemy spawning, interactables, boss gates and portal progression continue to run.

The original `RandomObjectSpawner` places three WMO building groups followed by eight M2 decoration groups. Each group derives a seed from the native map seed and biome palette version; its prefix/finalizer saves and restores `UnityEngine.Random.state`. Native ground landmarks remain in the palette with their own geometry, collision and original placement parameters. The map-wide override is the native empty serialized array; stage palettes live on their individual StageData clones. The original stage reference and map palette are restored before native final-arena loading.

## Original local assets

`scripts/elwynn-biome/build_elwynn.py` reads the installed Azeroth MPQ archives and exports the ignored `private-assets/elwynn-biome` pack. `validate_elwynn.py` independently compares hashes, geometry, UVs, normals, material passes, alpha pixels and texture orientation to those archives.

The pack contains six original ground textures and fifteen original static M2 props: canopy trees, a medium tree, pines, shrubs, rocks, fences, campfire stones/logs and barrels. Source aliases are resolved explicitly: ADT references omit the `_s.blp` suffix and environment references can use `.mdx` where the actual archive entry is `.m2`.

The native triplanar shader retains its noise, normal maps and render state. Its top/dirt/side albedos use original Elwynn grass, dirt and rock. Ground textures have generated mipmaps, trilinear filtering and anisotropy4. Private shader scales are `0.15` for the ground and `0.12` for sides. Generated world-edge sand materials are audited and rebound after native initialization. Shared native materials are never edited.

Foliage preserves original AlphaKey passes and two-sided cards. Runtime source meshes, materials, textures and prototype libraries survive `Resources.UnloadUnusedAssets` and are released when the plugin unloads.

## Passage protection

After generation, the adapter reserves the actual player spawn, native interactables, boss gates, portals and rails. It disables only new decoration that intersects those zones. A conservative surface graph uses the actual native floor layer, native player capsule dimensions and real physics queries to find a walking corridor to the boss gate and remove new obstacles from it.

If that native baseline cannot be proved within the loading-time budget, all added buildings are removed and only added M2 decoration colliders are disabled. Visible trees and rocks remain. This preserves the native map's own traversability without claiming that an unproved route passed. The report exposes the reason, masks, capsule dimensions, removed objects, preserved interactable counts, route result and fallback count.

## Launch and local preparation

1. Export and validate the private pack with the two Python scripts.
2. Build `src/ElwynnBiome/ElwynnBiome.csproj` against the installed BepInEx IL2CPP interop assemblies.
3. Copy the pack contents to `Megabonk-Lab/AzerothElwynnAssets` and the compiled DLL to `Megabonk-Lab/BepInEx/plugins/ElwynnBiome`.
4. Run `Start-Elwynn-Mage.cmd` for the prepared Mage battle, with all four book slots and the existing maximum talent allocation. The normal quick launchers select this biome too.

Normal launchers do not use the batch-only seed, movement, boss or capture probes. Local logs are written to `reports/elwynn/biome-*.log`. Source game archives, extracted assets, native binaries, probe runtimes and captured frames remain excluded from Git.

## Scope

These locations use Megabonk's generated terrain dressed with original Elwynn art. Original ADT heightmaps, interior M2 furniture and local WMO lights, source campfire particles and tree wind are not included. Terrain topology, movement and spawning remain native Megabonk behavior.

Actual build and runtime results are recorded in `ELWYNN_TEST_LOG.md`; earlier failed probes are preserved in local reports and the module logs.

## WMO buildings in v2

The private v2 pack adds original farm, goldshireblacksmith and goldshireinn WMO geometry: 16 groups, 142 material batches, 30,280 render triangles and 8,019 original collision triangles. Render UVs/materials and collision faces come from source archives. Four exterior doors have source portal metadata and independent aperture checks. No axis-aligned replacement wall blocks their entrances.

Buildings use the native random-object spawner and native object layer12. A uniform scale1.35 makes source entrances compatible with the actual native player capsule. A building is fitted against its source floor and entrance planes rather than its basement bounds. Real floor queries sample the full footprint; steep, missing, buried, overlapping or reserved placements are removed. Every retained external door must pass standing checks and a capsule sweep from outside through the aperture to the source interior floor. Foliage inside or approaching the doors is removed.

The source walkable corridor is measured with added colliders disabled. Added geometry crossing that corridor is removed; the native stage's own colliders and interactables are preserved. Runtime diagnostics expose each rejected building and reason, retained door checks, capsule dimensions and route verification. The native generator may retain few or no buildings for a particular seed; unsafe placements are not forced to satisfy a count.

The explicit batch feature runner (`-azeroth-feature-smoke`) records real native actions, native HP loss, card selections, captured facades and UI. Its test-only fixtures are guarded by batch mode and `AZEROTH_LAB_HEADLESS_TEST=1`. These flags are absent from normal launchers. See `V2_INTEGRATION_LOG.md` for exact passes, failures and evidence.
