# Private all-class spell VFX exporter

These scripts read the user's installed Azeroth MPQs and the source spell
catalogs. They regenerate the private VFX assets consumed by `SpellVfx.cs`.
They do not launch a game, deploy a runtime, or change the original MPQs.

Commit only these source files in `scripts/azeroth-vfx/`:

- `vfx_paths.py`
- `probe.py`
- `export_vfx.py`
- `augment_assets.py`
- `validate_vfx.py`
- `README.md`

The scripts reuse the repository's existing `scripts/azeroth-assets/`
(`mpq_source.py`, `convert_m2.py`) and `scripts/azeroth-mage/blp_decode.py`.
Use the existing Python environment with `numpy`, `Pillow`, and `mpyq`.
No original assets, decompiled source, extracted textures, model buffers or
generated reports belong in Git.

## Regenerate from the repository root

PowerShell, using the existing environment:

```powershell
$pythonPath = Join-Path (Get-Location) '.venv\Scripts\python.exe'
$env:AZEROTH_VFX_WORK = Join-Path (Get-Location) 'private-assets\azeroth-vfx-v3'
& $pythonPath scripts/azeroth-vfx/probe.py
& $pythonPath scripts/azeroth-vfx/export_vfx.py
& $pythonPath scripts/azeroth-vfx/augment_assets.py
& $pythonPath scripts/azeroth-vfx/validate_vfx.py
```

Run each command only after the preceding command succeeds. Output is under
`$env:AZEROTH_VFX_WORK/assets/`; summaries and the private texture contact sheet
are alongside it. `private-assets/` is already ignored by this repository.
These commands perform export and validation only; deployment is separate.

All tools also accept `--work PATH` (`--out PATH` is an alias). For example,
output can live outside the repository:

```powershell
& $pythonPath scripts/azeroth-vfx/probe.py --work E:\PrivateVfxExport
& $pythonPath scripts/azeroth-vfx/export_vfx.py --work E:\PrivateVfxExport
& $pythonPath scripts/azeroth-vfx/augment_assets.py --work E:\PrivateVfxExport
& $pythonPath scripts/azeroth-vfx/validate_vfx.py --work E:\PrivateVfxExport
```

## Inputs and path overrides

`--repo PATH` or `AZEROTH_LAB_ROOT` selects the repository. The default is
derived from the installed `scripts/azeroth-vfx/` location, without a machine
specific E-drive path.

`--source-data PATH` or `AZEROTH_SOURCE_DATA` selects the user-owned MPQ
directory. Its default is the repository's sibling
`AzerothSurvivorsWindowsNew/AzerothSurvivors/Data` directory. The original
files remain read only. This configures the existing MPQ reader for the
current Python process; it does not edit that helper or a game configuration.

`probe.py --catalogs PATH` accepts either:

- `src/AzerothAbilities/catalogs/` (the default, nine named class files and
  `auxiliary-spells.json`), or
- a private source catalog export containing `abilities/` and
  `auxiliary-spells.json`.

The probe requires 102 class spell identities plus 10 auxiliary identities.
Catalog provenance paths and hashes in a generated manifest can differ between
those two input layouts; the underlying DBC model bindings are the same.

## What is reproduced

1. `probe.py` resolves all nine classes through the installed `Spell.dbc` →
   `SpellVisual` → `SpellVisualKit` / `SpellVisualEffectName` tables. Only kit
   fields 3 through 12 are model attachments. Fields 13 and 14 are sound and
   camera shake, and are excluded.
2. `export_vfx.py` decodes the original M2/BLP assets into private model JSON,
   animated geometry buffers, and RGBA textures.
3. `augment_assets.py` adds the original particle/ribbon bone attachment poses
   and material flags. `*.bones.bin` is row-major float32 Unity-coordinate
   matrices, on the same source animation sample grid as the geometry. The
   original vertices and textures are not rewritten by this step.
4. `validate_vfx.py` checks original hashes, decoded texture/alpha bytes,
   model/geometry sizes, valid attachment slots and bone buffer coverage.

For the pinned source assets, the corrected graph contains 112 spell rows,
421 genuine links, 150 referenced M2 models, 171 textures, 485 particle emitters
and 91 ribbons. There are 130 attachment model buffers. Absent DBC attachments
are reported rather than replaced with unrelated art.

The release checkpoint's **295-file overlay** was a delta over an older private
cache that retained four now-unreferenced model JSON files. It is not a required
file count for a fresh complete export. To recreate that historical overlay
layout, seed `WORK/assets/` from the same old private cache before running the
export and augmentation steps; augmentation preserves those historical files.
A fresh export contains only models referenced by the corrected manifest.
Validation and runtime use the referenced models, not stale cache entries.

This converter retains the documented rendering limits: nested model particles,
specialized particle simulation and camera-facing bones are not made exact by
the bone attachment export. Source binding and byte validation are separate
from native gameplay/frame verification.
