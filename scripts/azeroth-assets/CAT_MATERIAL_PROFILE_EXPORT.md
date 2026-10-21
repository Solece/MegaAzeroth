# Rebuild the original Druid Cat material profile

`export_cat_material_profile.py` uses only Python's standard library. Input is a privately generated, user-owned canonical DruidCat pack containing model.json, conversion.json, original.m2, texture.rgba/texture.rgba.json and its vertex-frame files. No game assets are included in this script package.

Run from the repository root:

```powershell
python scripts/azeroth-assets/export_cat_material_profile.py --pack private-assets/hero-assets/creatures/cat
```

Default output is PACK/source-material-profile.json. Use `--output <private-output-path>` to write elsewhere. Only the profile is written. Existing source/geometry/texture inputs cannot be overwritten, and their hashes are checked unchanged. The exporter does not launch/deploy a game or access processes, desktop, saves or registry.

Only original DruidCat display892 is accepted: model path Creature\DruidCat\DruidCat.m2 and original SHA256 `2e48a967e14b4258e0d598b474019b6545ed2233acad567f76bc20e522915f77`. It validates original LOD0 submesh and texture-unit tables against conversion provenance, checks source-to-runtime vertex mapping and reversed triangle winding, and retains all742triangles in the exact existing vertex/UV/atlas order.

The four source passes are body670triangles(flags0/blend0 opaque), fur60(flags4/blend1 two-sided cutout), teeth8(flags1/blend0 opaque/unlit), eyes4(flags17/blend2 alpha blend/unlit/no depth write). Per-pass alpha checks specifically establish that low-alpha pixels on the source opaque body must not be cut out, while fur needs its source cutout mode. Do not make the whole mesh opaque or erase all source alpha. The precise native alpha-test threshold was not established by this audit.

Generated profile format is azeroth-creature-material-profile-v1, with sourceSha256, sourceDisplayId, texture and passes[{sourceSubmesh,sourceMaterialIndex,flags,blend,textureId,triangles}]. Expected profile SHA256 is `ec7355ca6b98b126949ec0e00c65c8c511288e87b587625df08e910296af9b1c`; the exporter rejects a mismatch before writing. This exact profile was rebuilt with33source checks and all geometry/source/texture inputs unchanged. Four negative checks rejected wrong display, changed original bytes, reordered triangles and overwriting source input.

Copy only the two files listed in copy-manifest.json into Git. Generated source-material-profile.json contains original game triangle data and belongs with ignored private assets. Hash/source validation is not a claim of native render testing; the coordinator verifies the four-material runtime separately.
