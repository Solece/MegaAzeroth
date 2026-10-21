# Original creature variety expansion

Eight distinct original models replace explicit native appearance categories. Source assets come from the user's installed archives and must remain private. The existing Ghost appearance/palette override runs first, and no native RNG, spawner, AI, HP, collision, rewards, or movement code is changed. Ordinary source bodies retain the existing2/3 native-collider-height normalization; bosses retain1 native-renderer-height normalization.

| Source key | Original display | Native enemy enum IDs | Scope |
|---|---:|---|---|
| ghoul |137|6 Ghoul|ordinary|
| zombie |1200|41 Zombie|ordinary|
| scorpion |2485|26 Scorpion,30 ScorpionMedium|ordinary|
| silithidwasp |92|24 Bee|ordinary|
| ent |864|19/20/21 Ent1/2/3|ordinary|
| fleshgolem |1061|31 MummyTank|ordinary|
| doomguard |433|7 MinibossPig|boss only|
| dragonspawncaster |6761|35/36 Pharaoh2/3|boss only|

These are explicit cross-game appearance choices. The old mappings for other categories remain unchanged. Missing or invalid new source packs restore the retained native body instead of silently falling back to Boar/Ogre.

## Geometry and source materials

All28 original LOD0 render passes are retained. Every pass keeps its own texture, source triangle section, blend, cull/depth-write flags and original color/opacity/weight tracks. The texture replacement slots are resolved through the original CreatureDisplayInfo skin rows. There is no flattened diffuse atlas, UV clamp or global-cutout conversion. Opaque source passes ignore texture alpha; actual cutout and additive passes retain their separate modes. The source contract contains original canonical triangle hashes and texture-alpha statistics so the native probe can independently reject omitted sections or opaque-body alpha holes.

Scorpion has an original environmental reflection pass using `texcoord=-1` and `CRABREFLECT.BLP`. Its796 referenced vertices are appended as duplicates of the original source vertices, giving1700 exported vertices from904 originals. Every frame preserves an explicit generated-to-original vertex map. One renderer still draws the complete model. An owned per-instance mesh calculates camera-space sphere reflection UVs for the duplicated reflective section, without mutating cached source meshes or body UVs.

Zombie's two eye quads and Ent's four glow quads use camera-facing presentation. Their weighted source animation centers, shape and original UVs are retained; the small quads face the actual Unity camera in the same owned per-instance mesh. This is an explicit Unity billboard adapter rather than a port of the original client's camera-dependent bone-matrix implementation.

All selected original idle0/walk4/run5/attack16/death1 sequences are retained, including both Ent death variants and original relative frequency weights. The existing live native-driven loop selects idle/run; walk and attack remain exported source clips and are not claimed to have new native event dispatch. Actual death uses the existing detached source corpse, original death sequence and1.2s source combat-clock lifetime.

The shipped `Particles/Standard Unlit` shader provides the verified per-pass blend/cull/depth/alpha controls. Original source lighting/fog and the camera-space reflection response are rendering adaptations; pixel-identical original-client shading is not claimed.

## Ownership and unload

Cached templates, all clip frames, textures and source materials are pinned against `UnloadUnusedAssets` and released by the model owner. Per-instance reflective/billboard meshes are released on rebind, native restoration, duplicate retirement, actor destruction and corpse completion. Cached source frame meshes are never changed by an instance. The existing Ghost material/palette files are unchanged.

## Reproduction and validation

From `scripts/azeroth-assets`, run:

```text
python export_enemy_variety.py --data-dir <owned-original-Data> --output-root <private-output>
python validate_enemy_variety.py --data-dir <owned-original-Data> --packs <private-output> --report <validation.json>
```

The scripts use the existing `enemy_fidelity_m2.py`, `enemy_fidelity_mpq.py`, and `enemy_fidelity_blp.py` helpers plus NumPy, Pillow and mpyq. All paths are arguments; the original archive directory is read-only and an output beneath it is rejected.

The asset validator independently compares original MPQ geometry, pass records, unclamped UVs, pixel data, source curves, animation sequence timings, death variants and file hashes. Metadata injection validation requires exactly the four existing MonoBehaviour lifecycle callbacks, with every generated managed helper hidden. Compilation and asset/metadata validation are not native rendering acceptance.

`copy-manifest.json` lists the six production source files, two export/validation scripts, source metadata and this note. `private-packs/` and the compiled DLL are local deployment artifacts and must not be committed. The parent performs isolated native visibility, pass/material, motion, death, bounds, Ghost-regression and gameplay-RNG checks before installation.

Format references: [pywowlib M2 structures](https://github.com/wowdev/pywowlib/blob/master/file_formats/m2_format.py), [1.12.1 model research](https://github.com/samwhosung/wow-1121-client-internals/blob/main/docs/models.md). Exact record offsets used here are independently checked against the installed files; model-version layout variations are not assumed interchangeable.
