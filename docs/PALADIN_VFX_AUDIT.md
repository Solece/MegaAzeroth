# Paladin VFX source audit

Original executable SHA256: `2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a`.

The audit re-read the original MPQ content for all 22 unique models bound to the 11 Paladin abilities. Each source M2 hash, version256 and first animation timing matches the exported asset metadata. The contracts contain only factual paths, identifiers, hashes, counts, timing, and findings; no textures, mesh arrays, animation frames, particle tracks, or executable data are included.

## Initial source findings and corrections

The initial generic ground-AoE path requested only the persistent `area` visual and omitted the separately mapped initial `cast` visual. The narrow correction now requests the original cast kit once and retains each original model duration; this code correction does not itself establish native visual equivalence. The source native ground cast sends a spell visual packet through `1458C4750` at `14596100E`, then creates the dynamic ground object through `145964CE0` at `1459610F8`. The packet helper writes opcode `0x132`; identifying this packet does not require claiming an unextracted source-client implementation.

The original size helper recognizes spell26573 and returns base radius8. The ground-object path divides effective radius by this value. Thus the persistent visual scale must be `actualEffectiveRadius / 8`, including applicable talent modifiers and radius softcap. The previous visual scale used learned radius and tome size only.

The required initial burst is `ce08f23a5004322c`, `Spells\Holy_Precast_Uber_Base.m2`, kit165/slot5/effect211. Its original clip is 3333..4400ms: normalize that source animation offset to elapsed0 and retain the1.067s one-shot emission. Slots6/7 both reference hand model `5c0ed9a6c8d8d885` with a1s clip. The current map loader deduplicates the two hand references and does not preserve their source attachment anchors. A dispatch-only fix resolves the missing central burst but does not establish complete attachment fidelity.

The persistent field is `d02bc71a978e2379`, `spells\consecration_impact_base.m2`, effect2542. It contains two additive geometry nodes and one emitter. Original geometry starts at zero opacity, reaches full opacity at0.5s, pulses between0.4 and1, and fades to zero at8.5s. This12s source clip and particle timeline must not be mistaken for a constant-opacity decal. Source rank1 combat duration is11.2s with11 scheduled ticks and first tick at0.25s; the actual source-client dynamic-object lifetime implementation beyond the available tracks remains outside this audit.

## Source particle correction (PSF2)

The ground field emitter is a separate visual role from the initial caster kit. Source flags32777 (`0x8009`) include the `0x8000` positive-rate burst latch. Rate0 at0ms changes to200 at67ms; the source emits once at the first positive-rate sample and latches, rather than emitting200particles per second throughout the area duration. The integer operation is CRT `truncf`, proven through the actual IAT (`145BA8267 ->145D49398`), not `ceilf`. A positive count below1 emits zero and still latches. Effective zero-rate rearms the emitter.

Original raw shape2 maps to the spherical helper. Its signed angles convert to Unity direction `(-cos(h)*cos(v), sin(v), -sin(h)*cos(v))`; zero vertical range makes this Consecration burst spread horizontally rather than form an upward column. Source particle life is1.2s. For a single cast with recasts suppressed, require actual early0.2..0.9s admission/center evidence and expect zero live field particles at1.5..2s. Later6.2..7.7s samples require the appropriate ground geometry, without requiring particles to remain alive. Ground geometry legitimately fades to zero at8.5s.

The renderer admits the original200-particle burst using an explicit256 burst capacity; the source simulation cap is1024. Larger bursts may still clip and must report admission loss. Bonevelocity, unverified parenttransform branches and specialized0x1000quad handling are outside this correction. Exact source addresses and limitations are in [NATIVE_PARTICLE_PROOF.md](NATIVE_PARTICLE_PROOF.md). The historical runtime hashes in the first audit remain baseline evidence; the revised critical contract pins PSF2 separately.

The field-plane review confirmed the original gold ground texture, upward triangle winding and source single-sided CullBack material. Center-ray ground anchoring alone was not evidence of burial; the subsequent parent sampling found24/25 field points above ground and one tree obstruction. No speculative height workaround is part of this audit. Native A/B images remain necessary: renderer visibility and mechanical checks do not prove the golden carpet's pixel appearance. An armed batch fixture may use an explicitly marked neutral32x32 controlled QA stage above the actual map after the flat-footprint search fails. Those captures test the source effect against a controlled background; they are not evidence of natural-terrain composition or an original-client A/B match. This synthetic fixture is not a production terrain or effect-height workaround.

## Required native verification

1. A real Consecration cast must produce the specific initial burst key once; an area event or any other nonempty asset cannot satisfy this check.
2. Capture live geometry, particles, color, animation and camera coverage around0.1/0.333/0.65s. The hand duplicate/anchor limitation must remain explicit.
3. Verify field scale against actual gameplay radius and source opacity milestones. For one cast, independently check one200-particle horizontal burst, no clipping, flat early centers, and zero particles at1.5..2s. Later ground samples must not require continuous emission.
4. Record native enemy HP decreasing on separated field ticks, pause/resume clock behavior, expiry, and critical-effect budget drops or evictions.

These contracts are source evidence and acceptance criteria, not a claim that a native visual test has already passed.

## Reproduction

Run `freeze_contract.py --lab <local-lab> --graph <source-vfx.json> --assets <generated-vfx-assets> --output <this-audit-folder>` with the existing MPQ Python dependencies. The output folder must already contain the bounded local disassembly evidence files listed in the contract. The script reads the user's original archives through the existing local MPQ reader and writes only the requested output folder. It never launches or modifies either game.

The runtime source hashes in the contracts identify the audited pre-fix implementation. Later fixes should retain this baseline evidence rather than silently replacing its hashes.

## Git contents

Copy only the metadata contracts and this audit note according to `copy-manifest.json`. Keep local M2/BLP data, generated VFX assets, and raw disassembly evidence outside Git. The evidence files are individually hashed in the critical contract for local verification.
