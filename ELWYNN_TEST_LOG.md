# Elwynn v1 — actual runtime verification

Date: 2026-10-21. Host: Windows x64, Megabonk1.0.69/Unity2023.2.22f1, BepInEx6.0.0-be.755, net6.0. All game probes used an isolated runtime, hidden D3D11 batch rendering and no OS keyboard/mouse control. The user's running game was not stopped. Original game installations remain read-only.

## Final build and installation

- Release `dotnet build --no-restore --no-incremental`: zero warnings/errors.
- Installed DLL SHA256: `B6DD3EE11F7CDE882F09CF8E74DF5149D30F1FF3EF97E45EE9C31C4FB3D81F66`.
- Private Elwynn pack: all61 installed files matched their source hashes. Independent source validation passed for35 archive files,22 decoded textures,15 models,21 mesh passes,1120 original vertices and1009 original triangles. Original alpha, geometry, normals, UVs and orientation matched.
- Dedicated `Start-Elwynn-Mage.cmd` added. Quick launchers now enable `-azeroth-elwynn`; the menu launcher enables the first Desert stage replacement. Existing999-point/max39-talent setup and four book slots remain.

## Actual native seeds and clearance

| Native seed | Visible source props | Capsule route result | Added collider fallback | Evidence |
| --- | --- | --- | --- | --- |
|27183|501|Conservative native walking route not proved|348 added solid colliders disabled; visible art retained|`biome-20261021-011838-60124.log`|
|31415|499|243.835m,334 ground-following samples; ends10.026m from gate|0|`biome-20261021-011151-19056.log`, first-stage assertions passed before the retired portal harness|
|38203351|469|752.715m,864 samples; ends10.382m from gate; one blocking added prop removed|0|`biome-20261021-011648-64600.log`, original full adventure regression|

All runs retained86 native interactables, zero reserved-zone violations and the exact native stage timeline/map-parameter/special-tile references. The measured native capsule and actual floor/object masks were used: floor256, objects4096; both native combined masks are4352. No native terrain, player capsule or AI was modified by clearance. A failed baseline invokes only the added-collider fallback and is never reported as a proved route.

The final first-stage probe measured65.78343 world units of movement through the original MovementTick/physics controller, observed four native enemies and kept the capsule unchanged. Source Mage effects were exercised. All22 environment textures/21 meshes/14 materials survived explicit native `Resources.UnloadUnusedAssets`. All six ground textures had nine actual mip levels and Trilinear filtering.

Both fixed seeds produced different native terrain sample signatures and different source layouts. The native coroutine can consume global randomness across yielded frames; identical seeds are not claimed to guarantee an identical complete world. Eight decoration groups were individually seeded; all16 native group calls restored their captured RNG state with zero failures.

## Boss portals and source regression

The established `AdventureSmokeRunner` plus `VisualTransitionProbe` ran separately from the first-biome probe. Its actual native random seed was38203351. Result at01:18:02+05:00:

`PASS: native stages verified=6; stage bosses killed=2; FinalBossMap loaded, live final boss=Pharaoh3 hp=6134562.5/6134562.5; Mage spell ranks/talents/books retained; no OS input.`

Route: first-stage Elwynn/Pharaoh1 -> native portal -> Desert2/Pharaoh2 -> native portal -> Desert3/final gate -> FinalBossMap/live Pharaoh3. The run's map-wide palette override was restored before leaving Elwynn. All13 original spells/ranks,121 spent talent points and four book slots survived. Mage/staff, all11 enemy models and source VFX survived forced cleanup on each scene. At final proof,8053 effect meshes/67 textures remained valid, four native weapon grants had been rejected,1473 native weapon ticks suppressed and the original item35/display472 staff was active. Actual first-stage, later-stage and final-boss images were inspected.

Full proof retained as `private-assets/elwynn-test-runtime/reports/elwynn/adventure-full-passed-20261021-011802.log`. Other module logs and native player logs remain in the same isolated report tree. The final BepInEx startup slice had no new adapter/interop exceptions; standalone `SteamAPI_Init` failure is the known baseline.

## Corrections and retained failed probes

Earlier logs are retained, not overwritten. Real probes exposed inactive template roots, deep-copied StageData embedded references, an inlined native placement wrapper, world-edge sand, giant ground tiling, incorrect combined collision masks, stripped Physics.CapsuleCastAll and a broken abstract-array unmarshalling path. These were corrected from the installed metadata, native binary and actual rendered evidence. The capsule query now uses the verified native NonAlloc injected binding and a pinned concrete buffer, with checked layouts and conservative overflow handling.

The initial duplicate portal harness also captured the old scene before unload, reused unrelated HUD-capture assertions and assumed native Enemy.Kill was a force-death operation. Native inspection disproved that assumption: Kill submits hp+1 through ordinary damage guards/armor. That duplicate boss driver was removed. The biome probe now covers only first-stage generation/movement/spawn/assets; the existing proven adventure regression owns the full route. No production boss logic was weakened to make a test pass.

## Scope and limits

V1 uses native Megabonk terrain topology, movement and spawning with original Elwynn grass/dirt/rock, static trees, shrubs, rocks, fences, barrels and campfire stones/logs. It does not include WMO buildings/interiors, original ADT heights, tree wind or source campfire particles. Later-stage environments remain native. Long-run balance and performance were not benchmarked.
