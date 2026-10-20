# Procedural Azeroth biomes

Design checkpoint: 2026-10-20. This is a verified integration proposal, not an implemented map replacement.

## First playable target

Keep Megabonk's native level shape, height changes, movement, ground collision, enemy spawning, boss gates and stage transitions. Apply the original Azeroth Elwynn ground textures and populate the generated level with original trees, shrubs, rocks, fences, barrels and campfires.

Each native map seed should produce a different layout while retaining a coherent forest appearance. Use open combat clearings between forest clusters. Reserve clear space around the player spawn, interactables, boss arena and portals. Larger source buildings become deliberate landmarks after their collision and entrances have been checked.

## Verified source assets

The installed Azeroth archives contain eight inspected Elwynn ADT tiles under `World/Maps/Azeroth/`, six decoded 256x256 Elwynn ground textures, fifteen inspected environment M2 models with their textures, and five WMO structures with all 22 referenced groups and textures: farm, Goldshire inn, blacksmith, mage tower and bridge.

The ADT texture references omit the `_s` suffix; the verified archive files use `_s.blp`. Environment model references use `.mdx`, while the verified files use `.m2`. Resolve these source aliases explicitly.

These are real source meshes and textures. They are not yet ready Unity environment prefabs. M2 prop import must preserve material/alpha behavior; WMO group import and collision selection need a separate adapter.

## Native integration

- Clone the selected `MapData`/`StageData` for the biome. Inspect the actual shader properties before binding ground textures to `flatMaterials` or `triplanarMaterial`.
- Supply M2 props through `StageData.randomMapObjects`, also handling `MapData.randomObjectsOverride`. `RandomMapObject` provides prefab choices, placement radius, scale limits, slope limit and ground offset.
- Add tested landmarks through `StageTilePrefabs`, preserving the native boss-spawner and portal objects and their triggers.
- Share each imported mesh/material across instances and protect cached native resources from `Resources.UnloadUnusedAssets`, as with the repaired character/effect adapters.
- Keep decorative randomness reproducible without changing combat randomness. Record the native map seed, biome version and prop seed; isolate additional RNG calls.

## Acceptance criteria

1. Native movement and enemy spawning work on the generated ground. Trees and large rocks have suitable trunk/body collision; foliage does not block the whole canopy footprint.
2. Player spawn, gates and interactables remain reachable. Check several fixed seeds, actual capsule clearance and continuous routes, then remove blocking props.
3. Props follow the real floor and slope. No floating objects, buried landmarks or duplicated invisible native scenery.
4. Defeat a native boss and enter its real portal through every stage to the final arena. Mage, original weapons, spells, books and map markers survive travel and forced unused-resource cleanup.

Full ADT terrain reconstruction can follow separately. It needs height seams, texture alpha layers, holes/water, fresh colliders, map/fog integration and valid spawn positions. It is not required for the first procedural Elwynn biome.

## Evidence

Read-only source investigation: `environment-procedural-recon.md`, `environment-procedural-recon.json` and `environment-selected-dependencies.json` in the coordinating task's local work directory. Native integration fields were checked against the installed IL2CPP metadata and binary. Private source assets and game binaries remain excluded from Git.
