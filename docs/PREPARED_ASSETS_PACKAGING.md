# Prepared runtime asset package

The packer reads a verified existing MegaAzeroth runtime and creates a separate converted-asset payload. It does not launch a game, modify the source runtime, or publish anything.

```powershell
python -I scripts/release/pack_prepared_assets.py --runtime "C:/MyBuild/Megabonk-Lab" --output "C:/PrivatePackage/Assets"
python -I scripts/release/validate_prepared_assets.py --runtime "C:/PrivatePackage/Assets/Runtime" --report "C:/PrivatePackage/Assets/closure-validation.json"
```

Only Python standard-library modules are required. The prepared game does not require Python or the original Azeroth installation. Game executables, loader files and mod DLLs are supplied separately by the release packager.

Outputs:

- `Runtime/`: ordinary copied files with all source asset-directory junctions dereferenced.
- `asset-files.json`: portable `megaazeroth-prepared-assets-v1` manifest with `Files[{Path,Bytes,Sha256}]`; paths are runtime relative.
- `MegaAzeroth-prepared-assets.zip`: the same runtime-relative files without a parent directory prefix. Every ZIP member is independently read and compared with the manifest.
- `pack-audit.json`: private build audit containing source paths and exclusions. Do not include this audit in the distributable.

The package includes the four converted asset roots and generated catalog/UI files for AzerothAbilities, AzerothHud, HeroRoster and MageTalents. Original MPQ, M2, BLP, DBC, WMO, EXE/DLL files, conversion reports and previews are excluded. Optional decoded PNGs and source-derived animation buffers remain local prepared data. Source conversion audits referenced only by provenance metadata are not runtime dependencies.

Only absolute workstation paths in the top-level talent `source` and VFX `sourceCatalogs[*].path` provenance fields are replaced by `source-owned/<filename>`. Render geometry, texture pixels, animation buffers, material profiles, spell/talent values and asset references are unchanged. An absolute path in any other JSON field fails packaging.

The independent closure validator checks model indices and buffer dimensions, all declared animation/attachment/portrait/grip frames, death profiles, the active and fallback spell manifests, VFX geometry/bone buffers, Elwynn file references and every RGBA payload size. It distinguishes runtime model raw textures from optional PNG preview names, and checks auxiliary decoded texture dimensions from PNG IHDR records. These are asset completeness checks; native gameplay validation remains a separate release check.

The current v5 asset payload has 4,249 files (223,997,358 bytes), including 54 model definitions, 383 declared animation clips, 20 death profiles, 128 VFX geometry definitions and 1,001 UI raw texture entries. The closure validator passed 5,710 checks and validated dimensions for all 1,402 distinct RGBA files. The source v5 runtime was read only.
