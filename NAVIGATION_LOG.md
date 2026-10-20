# Native map navigation repair

## 2026-10-20 — investigation and implementation

- User issue: player location/direction missing from minimap and full map; boss gate navigation missing after discovery through fog.
- MageVisuals agent confirmed its generic native-renderer mask could hide renderers below exact native navigation roots. That separate patch preserves `MyPlayer.minimapCameraScript.playerIcon`, `MyPlayer.arrow`, `shootingArrow`, and typed Minimap marker components. This feature does not duplicate the original player markers.
- Added `src/AdventureFlow/NativeMapNavigation.cs`: after procedural generation completes, rebinds missing native `MinimapCamera.bossSpawner` using the native finder. A discovered target is registered through native `AddArrow`, with native boss/portal colors. Native `UpdateArrows` continues to position, orient and show/hide the directional indicator.
- Discovery follows either the native `bossSpotted` bit or a genuinely revealed pixel in the original full-map fog. No production code reveals any world fog or enables an undiscovered gate icon. During the active boss fight (`bossEnemies.Count > 0`), the removed gate arrow is not recreated; completed-fight state uses the actual `numBossesDefeated > 0` counter rather than list allocation; after native boss defeat its active native portal may receive the native portal direction.
- Exact native projection (`FullMap.QueueRevealFog`, RVA0x53572A): `x = int((world.x + worldSize/2) / worldSize * textureSize)`, `y = int((world.z + worldSize/2) / worldSize * textureSize)`, `index = y*textureSize+x`.
- Exact native fog semantics: `InitFogTexture` initializes pixels `(0,0,0,255)`. `RevealFog` writes increasing `(reveal,reveal,reveal,255)`; `fullyRevealed[index]` becomes true only at reveal255. Alpha remains255 and is not the discovery signal. The adapter reads RGB and the existing full-reveal flag. Defaults are textureSize64 and revealRadius110 world units.
- Stage binding is refreshed after map generation and reset across native stage/scene changes; map coordinates and native render/culling layers remain untouched.

## Diagnostics and bounded background verification

- `AzerothMegabonk.AdventureFlow.NavigationBridge.Describe()` returns native scene/stage, player-icon path/activity, minimap camera state/culling mask, stable gate path/distance/discovery, registered native arrows with layer/activity/renderer state and actual radial-direction dot, and each full map's precise gate pixel and RGB/reveal state.
- `RevealGateForProbe()` is restricted to `Application.isBatchMode` and `AZEROTH_LAB_HEADLESS_TEST=1`. It queues and reveals one native fog circle centered on the gate using native `QueueRevealFog` and `RevealFog`. Only the movement-based queue throttle is temporarily bypassed and then restored. It logs the initial/final pixel RGB, alpha, native radius, arrow registration, and complete diagnostics. This path is not invoked in ordinary play.
- `SetFullMapVisibleForProbe(bool)` opens the existing map through native `PauseUi.Pause()` and `GoToMap()`, then closes through `Resume()`. The actual FullMapUi OnEnable/OnDisable events run normally; no synthetic map or manually raised event is used.
- Source capture: `scripts/megabonk-research/capture_navigation_evidence.py` writes `reports/megabonk-research/navigation-native-evidence.txt` from this installed binary and matching signatures.
- Build: `dotnet build src/AdventureFlow/AdventureFlow.csproj -c Release` — 0 errors, 0 warnings.
- Coordinating agent is running combined native rendering/navigation verification; its runtime result must be read from actual probe logs before declaring the map repair tested.
## Actual native render/navigation verification — 22:06:41 +05:00

- Coordinating agent ran the combined background Direct3D test with MageVisuals0.1.2 and AdventureFlow navigation. No OS input or visible game window was used. `reports/mage-battle-smoke.log` ended in PASS, then the process exited itself.
- Native player marker was enabled, active and included by both map cameras (`cullingMask=16384`, UiCamera layer14). Its world position tracked the native player's scripted3-by4 movement exactly; MageVisuals diagnostics reported `hiddenByMage=0`.
- Gate fog pixel at native texture index841 changed RGB0→255 while alpha remained255, confirming the actual native limited-radius reveal path. The full map remained fogged elsewhere.
- The gate was already within native minimap discovery range (distance89.7→93.4), so native `bossSpotted` and its registered arrow were already true before the full-map reveal. The native edge arrow correctly remained inactive while the gate was inside the minimap view. This run verifies the visible in-map gate icon, not a far-away edge indicator.
- Native Pause/Map opened and closed successfully. Native cameras rendered their own targets and the complete Canvas screenshots were saved as `reports/mage-navigation-minimap.ppm` and `reports/mage-navigation-full-map.ppm`.
- Both screenshots were visually inspected: the blue player-direction marker and red boss-gate marker are visible in the minimap and the revealed section of the full map; remaining full-map terrain stays covered by fog.

## Actual undiscovered/far-gate navigation — 22:09:34 +05:00

- With MageVisuals0.1.3, a gate514 world units away started with `bossSpotted=false`, no registered arrow, and native fog RGB0. This directly verifies that production navigation does not reveal an unknown gate.
- The batch-only native110-radius fog reveal changed that gate's RGB to255 and registered its native arrow. After controlled native movement, the gate remained337 units away and `ArrowPrefab(Clone)` was active, renderer-enabled and visible to both native map cameras. Direction dot was `1.0000001` (normal floating-point rounding of1), proving it pointed directly toward the gate.
- MageVisuals0.1.3 additionally preserves exact roots from `MinimapCamera.arrowDict.Values` and the namespaced `Assets.Scripts.Camera.MinimapArrow`, which is a distinct type from the global `MinimapArrow` class. Native near-gate arrow hiding remains intact.
- The initial full-screen capture assertion expected the gameplay HUD to stay visible while native Pause/Map correctly hides it. This was a test-harness assertion failure; the native player marker, far-gate arrow, native map texture rendering and fog navigation had already passed. The coordinating agent is correcting that assertion and running a short final native screenshot check; AdventureFlow production source and DLL remain unchanged.

## 2026-10-20 — final native route and map navigation checkpoint

- Actual native route PASS: Desert1/Pharaoh1 -> native portal -> Desert2/Pharaoh2 -> native portal -> Desert3/final gate -> FinalBossMap/live Pharaoh3. The same inventory and learned Arcane Missiles rank2/Fireball rank1 persisted, with all4books and121spent talents. Available native maps are Desert/Forest/Graveyard; their menu tiers are unlocked by runtime getters without completion/save forgery. Test battle selects native maximum tier so the route continues beyond stage1.
- Stage changes now rebind Mage scene-owned projectiles/statuses without clearing learned spell instances or upgrade factors. True new runs still reset normally.
- Fixed map marker masking in MageVisuals0.1.3: exact native player icon and both native MinimapArrow classes/arrowDict renderers are protected from skin hiding. Native close-target arrow hiding is retained.
- Navigation binds the current native boss gate after generation, observes the original full-map RGB fog pixels and native bossSpotted bit, then registers the ordinary native direction arrow. Gameplay does not reveal unexplored terrain. In the batch probe only a native radius110 fog circle was revealed: gateRGB0->255 with alpha255 retained; all other fog remained.
- Final native map proof: moved the player beyond the minimap, confirmed matching player/icon XZ, far gate arrow active/rendered in map layer14, directionDot~1, hiddenByMage0. Original minimap256x256 and full-map1024x1024 RTs contained real pixels. Native Pause -> Map -> Resume completed; player marker and gate/arrow verified visually on both maps. Final proof reports/mage-battle-smoke.log; earlier near-target and capture-helper attempts preserved.
- HUD0.5.1 hides while native Pause/Map is open, then restores in battle. The capture helper temporarily suppresses map-only 3D meshes when adding native UI render layers to the main offscreen camera. This removes the giant blue player-map marker from combat proof images; real gameplay camera keeps its original layer mask.
- Source, logs and launchers are checkpointed in Solece/MegaAzeroth. Original game installations/progression are unchanged. Test launch retains999points/all39max,121spent878free and4books.
