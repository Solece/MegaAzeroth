# Elwynn environment asset pack

## 2026-10-21: original local environment pack

- Source archives are read-only: `D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors/Data/patch.MPQ`, with existing overlay archive priority from `scripts/azeroth-assets/mpq_source.py`.
- `scripts/elwynn-biome/build_elwynn.py` exports a private `private-assets/elwynn-biome` pack. No extracted game asset belongs in Git. The source converter and runtime loader are the deliverable.
- Exported six 256x256 original ground textures: ElwynnGrassBase, FlowerBase, DirtBase2, RockBaseTest2, CobbleStoneBase and Crop92. The installed files have `_s.blp` suffixes although the ADT references omit the suffix.
- Exported 15 original M2 v256 static models: four canopy trees, medium tree, two pines, bush09, two cliff rocks, small detail rock, fence, campfire stones/logs, Westfall barrel and generic barrel. This is 1,120 source vertices, 1,009 source triangles across 21 original render passes; eight passes use source AlphaKey materials. There are 22 runtime RGBA textures (sampler flags preserved separately).
- Every source path, archive, byte length and SHA256 is recorded in the private manifest; runtime RGBA hashes are recorded separately. Every original texture slot and render pass is retained. The source M2 header for v256 has material flags at offset132; offset124 is the replaceable texture lookup and must not be interpreted as render flags.
- Conversion is the existing project convention `source(x,y,z) -> Unity(-y,z,x)`, flipped V and reversed winding. Original positions/normals/UVs are retained in source units. No arbitrary recentering or scaling occurs in the asset loader. `PropBounds` exposes original bounds so placement can lift by `-bounds.min.y * scale`.
- `src/ElwynnBiome/BiomeAssets.cs` exposes `BiomeAssets.Load(folder)`, `GroundTextures`, `Props`, `PropBounds`, `PropSources`, `AllAssetsValid`, `DiagnosticsJson()` and `Release()/Dispose()`. Ground keys are `grass`, `flowers`, `dirt`, `rock`, `cobble`, `crops`; prop keys are lower-case original model stems. Prototypes are inactive children of a persistent inactive library. Scene integration clones a prototype, reparents the clone to the scene, and activates it.
- RGBA is uploaded through pinned pointers using `LoadRawTextureData`; `TextureFormat.RGBA32`, bottom-up rows, original wrapU/wrapV and bilinear filtering. All owned Texture2D, Material, Mesh and prototype objects are explicitly retained with `HideFlags.DontUnloadUnusedAsset`; the loader explicitly releases them on disposal. The loader never modifies source game files or native gameplay objects.
- Material handling preserves distinct source diffuse passes, AlphaKey cutoff0.5, source two-sided flags and texture samplers. Double-sided source cards receive reversed triangles as well so they remain visible with a stripped fallback shader. The loader reports its actual shader names and any AlphaKey-to-sprite fallback in diagnostics. Runtime visual validation is still required to establish which shaders the installed game ships.

### Verification

`validate_elwynn.py` passed against the installed archives: all 35 source files match their hashes; all original geometry arrays, UVs, normals, triangle passes and material flags match exactly; all texture bytes decode identically, retain source alpha and upload in the recorded bottom-up orientation. The six ground textures were viewed in a contact sheet and have correct original colors.

An isolated asset-loader build against the installed BepInEx IL2CPP references passed for net6.0 with zero warnings/errors. The first restore attempt used an empty scratch cache and could not reach NuGet under sandbox network restrictions; the build succeeded using the existing project NuGet cache. No game was launched or modified by this asset subtask.

### Explicit limits

This pack is static M2 geometry. Tree wind, source campfire particles and WMO building interiors are not emitted. Campfire rocks and logs retain both of their actual source textures; no substitute flame is invented. Collider shape, placement density, reserved routes, biome lighting, native material assignment and in-game validation are owned by the scene integration. The loader adds no colliders and changes neither Megabonk generation, movement, spawning nor portals.

## Final integrated runtime evidence

Final native runs confirmed Standard AlphaKey foliage, all six ground textures with nine mip levels/Trilinear, no alpha fallback warnings and all22 textures/21 meshes/14 materials valid after explicit unused-resource cleanup. Source bytes remained unchanged. See ELWYNN_TEST_LOG.md.
