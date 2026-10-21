# Source particle correction: spherical emitter and positive-edge burst

This is a source-only renderer correction for `src/AzerothAbilities/SpellVfx.cs`. It does not change spell damage, collision, projectile travel, targeting, movement, spawning, books or combat clocks. No original game files or disassembly are included in this document.

## Pinned source and original reference

Original owned `AzerothSurvivors.exe`, observed version 0.102.3 / `f78e6fab`, SHA256 `2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a`.

Read-only native PE inspection resolved the actual particle simulation through embedded source-path panic locations. The executable contains no available source PDB; addresses below were resolved against PE unwind ranges and actual CRT import names, not fabricated symbol names.

An actual original-client run using native positional arguments `elwynn paladin human`, isolated saves and `um win record` produced 57 distinct native PNGs and two decoded 640×360/30-fps videos on 2026-10-21. The original Human Paladin's starting Consecration, DBC26573, visibly forms a broad golden ground field. This is reference evidence, not an automatic visual-equivalence pass for the candidate.

## Spherical shape: verified general native helper

The original format parser at `145ABA100..145ABA125` maps raw M2 shape 2 to internal variant 1. Original simulation `1415C5D60`, tied to `crates/benilla-world/src/particles/sim.rs`, calls the spawn helper at `1418D21E0`.

Native track provenance is explicit. Parser `145ABB289..145ABB2FF` reads M2 tracks at raw offsets `+0x34`, `+0x50`, `+0x6C`, `+0x88`, in speed, variation, vertical-range and horizontal-range order. `145ABB3AB..145ABB450` and converter `145A168D0` preserve that array order. The parsed source base is runtime-emitter `+0xD0` (`1415C9665`); track block runtime `+0x118` is parsed `+0x48`. Evaluator `145A79E30` stores the third/fourth sampled values into output offsets `+8`/`+0xC` at `145A7A2A0..145A7A2A6`.

Sphere branch `1418D2395..1418D2495` samples signed angular ranges:

```text
v = (2 * random - 1) * verticalRange
h = (2 * random - 1) * horizontalRange
M2 direction = (-sin(h) * cos(v), cos(h) * cos(v), sin(v))
```

The call stubs `145BA826D` / `145BA8273` resolve through the actual PE IAT to CRT `sinf` / `cosf`. Applying the existing exporter's M2-to-Unity mapping `[-y,z,x]` gives:

```text
Unity direction = (-cos(h) * cos(v), sin(v), -sin(h) * cos(v))
```

Thus vertical range zero produces a flat ring, with zero vertical velocity before gravity and any existing parent transform. The previous adapter used `cos(polar)` as vertical velocity and therefore sent zero-range particles straight upward.

Native `1418D23CF..1418D2495` also samples the spherical birth radius as `length + random * max(0, width - length)` and places particles at `radius * direction`. The adapter previously used a rectangular offset for all shapes. The candidate corrects direction and birth offset for raw shape2 only. The plane path is retained. Bone/parent velocity transforms, billboard axes and unverified flags are retained.

The same native sphere helper has two further **velocity-only** conditions, retained in PSF3. If `zSource` is zero and flag `0x100` is set, `1418D24AF..1418D24C7` selects positive M2Z, i.e. Unity up, while the birth positions remain spherical. If `zSource` is nonzero, `1418D254B..1418D25CA` instead normalizes the birth offset minus the sourceZ origin. The sourceZ case takes precedence over flag `0x100`. In Unity this is `normalize(birthOffset - (0,zSource,0))`. Native normalization checks a finite positive reciprocal length; degenerate/overflowing vectors fall back to positive M2Z/Unity up. This prevents NaNs rather than normalizing a zero vector. PSF2 omitted these conditions; PSF3 corrects that review finding without changing the proven ring or Consecration0x8009 direction.

Actual affected assets include Frost Trap emitters with flags8969/8457, Ice Barrier297, Divine Shield313, Redemption257 and Mind Blast16681/17193. Nonzero sourceZ also occurs in source root/frost/holy effects. The managed suite includes these actual flag values, sourceZ precedence and finite-fallback negative controls.

## Burst flag: verified positive-rate latch

Native `1415CAC70` tests the low word sign bit, i.e. flag `0x8000`. The ordinary branch adds `rate * deltaTime` to its budget. The burst branch `1415CACC0..1415CACED` requires a positive effective rate and an unset latch at emitter `+0x436`, truncates the effective count once, then sets the latch. Stub `145BA8267` resolves through actual IAT `145D49398` to CRT `truncf`; this is not a ceiling operation. A positive count below1 emits zero particles and still sets the latch. Subsequent positive samples do not emit again. `1415CADE0..1415CADE3` rearms when the effective source rate becomes zero. Disabled source emission takes the zero-rate path. A frozen combat clock does not invoke further simulation; the helper additionally leaves its state unchanged when deltaTime is zero.

Consecration model key `d02bc71a978e2379`, `spells/consecration_impact_base.m2`, has flags32777 (`0x8009`), shape2, zero vertical range, horizontal rangePI, speed8.333333, radius0.833333, particle lifetime1.2 seconds and rate keys0 at0ms, 200 at67ms. The first positive sample therefore emits200 particles once. The previous adapter treated the source burst count as a continuous rate.

The original per-emitter simulation admits up to1024 particles (`1415CAFCB`). This port retains a bounded renderer: continuous capacity192 is unchanged; burst capacity256 admits the real Consecration200 without truncation. Larger bursts can be clipped by this explicit adapter limit. `ClippedParticles` reports actual admission loss; the candidate does not claim the256 limit is the original limit. A clipped one-shot burst is not silently retried.

Flag `0x1000` has a separate original quad-basis branch at `1417F7777`; Consecration0x8009 does not contain it. No new quad-flag behavior is inferred from that observation. The native local/global transform branches require their own contract before changing bone directions; they are outside this correction.

## Probe contract and validation

Existing `RendererSnapshotJson` particle layers retain their previous fields and expose:

```text
Shape, Capacity, LiveParticles
AllParticleCentersMinimum[3], AllParticleCentersMaximum[3]
MeanParticleCenterY, MaxParticleCenterVerticalOffset
TotalSpawned, EmissionBursts, ClippedParticles
LastRequested, LastAdmitted, LastClipped, BurstLatched
```

Bounds and vertical-offset metrics include **all** live centers; the existing first12 sample positions remain available as samples only. Counters belong to the actual production admission path.

Managed checks compile the marked production helper unchanged from `SpellVfx.cs`, rather than a second reference implementation. They cover a flat zero-angle ring, normalization, signed-angle symmetry, source radius bounds, rate0 not consuming the burst, positive-edge count200, no repetition after1.5seconds, zero-rate rearming, paused clocks, explicit clipping and unchanged continuous one-particle carry. PSF3 adds actual Trap/Shield flag controls, sourceZ precedence, finite degenerate/overflow fallbacks and a Consecration direction invariant. The final suite contains10390 assertions. The managed checks do not validate Unity rendering, pixel appearance, bone matrices or native gameplay.

Native acceptance remains separate: create one actual Consecration field with automatic recasts suppressed; inspect200 spawned/one burst/no clipping, flat particle-centerY at early0.2..0.9seconds, and no live field particles at1.5..2seconds after the one-shot particles expire. Capture the actual native renderer and compare the ground effect against the original reference. Any lingering geometry and material-alpha checks remain governed by their existing source-stage contract.

`affected-bindings.json` inventories current private-catalog model keys, shape2/burst emitter indices and class/spell/stage bindings. These are potential affected visuals, not a claim that every catalog stage is requested during every cast. Warlock regression should include Rain of Fire and the remaining bound spell stages. Source assets and raw disassembly stay private.
