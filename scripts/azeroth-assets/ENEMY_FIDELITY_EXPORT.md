# Reproducible original enemy death and Ghost addon export

This package contains exporter code and verification hashes only. It does not contain extracted models, textures, animation frames, MPQ archives or game binaries. All input content comes from the user's own installed AzerothSurvivors. Keep every generated output under an ignored private-assets directory.

## Requirements and inputs

- Python with the three versions in `requirements-enemy-fidelity.txt`. These are the versions used for the verified byte-for-byte rebuild; no package installation is performed by the exporter.
- Original Azeroth `Data` directory containing `patch.MPQ`, then `patch-2.MPQ` through `patch-7.MPQ`. Latest archive wins, preserving the original reader's order. Source files are read-only.
- For death/all mode, an existing private baseline eleven-model pack (`model.json` and its original first-death vertex files). The existing `convert_m2.py`/`build_bosses.py` workflow creates that pack. The death addon must match its exact vertex order and first-death frame format; it is not a replacement for those baseline assets. Each original M2 is freshly read from MPQ and its SHA256 must match the baseline catalog.
- An explicit output directory outside this script package and outside original source Data. There are no machine-specific input/output/import paths embedded in these scripts.

## Run from the repository root

Set `AZEROTH_DATA_DIR` to the owned game's Data directory, and optionally `AZEROTH_BASE_ASSETS` to the private eleven-model pack. Then:

```powershell
python scripts/azeroth-assets/export_enemy_fidelity.py --data-dir "$env:AZEROTH_DATA_DIR" --base-assets private-assets/azeroth --output-root private-assets/enemy-fidelity
```

Equivalent environment-only inputs are `AZEROTH_DATA_DIR`, `AZEROTH_BASE_ASSETS`, and `AZEROTH_FIDELITY_OUTPUT`. CLI arguments override them. `--mode death` builds only the death addon; `--mode ghost` requires no baseline pack and builds Ghost directly from MPQs; default `--mode all` builds both.

Output includes `assets/AzerothAssets/` (the runtime overlay), private `scratch/ghost/` working source/preview files, and `rebuild-manifest.json` with file hashes. Do not commit any of these outputs or logs. The script does not deploy, launch a game, change source archives, save files, registry or native rendering materials.

## Exact runtime overlay

Death addon: `death-profile.json` for boar, bonegolem, gnoll, infernal, kobold, lich, murloc, ogre, skeleton, tarantula, wolf, plus `murloc/death_variant_11.vertices.bin` (12 files). It preserves source frequencies (Murloc10921/21845), exact source animation IDs/intervals/row hashes, source1.2-second corpse lifetime, and the baseline's first variant buffers.

Ghost: seven `*.vertices.bin` clips (idle, walk, run, attack, death, cast, cast_omni), `model.json`, `texture.rgba`, `texture.rgba.json`, `death-profile.json`, and `ghost-material-profile.json` (12 files). Original Ghost model SHA256 and display146 binding are pinned. All26 original render passes are represented in three material groups only when their complete material flags, blend and animation tracks are identical. Original M2 RGB/opacity tracks are retained. Three embedded body particle emitters remain unsupported and are explicitly not claimed as rendered.

`enemy-fidelity-reference.json` lists all24 exact relative paths, sizes and SHA256 values. Every selected output is verified against this frozen asset reference; a mismatch exits1. Hash equality proves asset reproduction, not native visual correctness. The rejected0.2.5 native palette reader observed only white debuff modifiers. Runtime0.2.7 independently samples native tier textures and composes material-slot0 status values; native34864 passed126checks with0failures and1explicit embedded-particle skip. Runtime-only luminance/alpha-over adaptation is separate from these byte-exact original exports; original native lighting/outline and embedded emitters remain outside that proof.

## Coordinator copy list

Copy only the files listed by `copy-manifest.json` to `scripts/azeroth-assets/`; `README.md` is renamed to `ENEMY_FIDELITY_EXPORT.md`. No existing exporter/production file needs replacement. Do not copy generated runtime assets, working previews, `__pycache__`, validation outputs or game files into Git.
