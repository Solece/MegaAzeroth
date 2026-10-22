# Azeroth × Megabonk — work log

## Scope (user-confirmed, 2026-10-20)
- Work root: D:/Games/Saitan/AzerothMegabonkLab.
- Host game: installed Megabonk. Keep native movement, combat, enemy AI and spawn timing.
- Guest: installed Azeroth Survivors. Transfer enemy models/animations and interface appearance.
- Keep Megabonk tomes/books. Unlock all FOUR TOME slots immediately; weapon slots were not requested.
- Parallel work authorized. Each worker owns a separate log and directories.
- Private local project. Do not publish or upload game files.

## Installation — confirmed
- Universal Modder 0.2.0 cloned to universal-modder/.
- Codex marketplace registered and plugin installed at E:/CodexData/Home/plugins/cache/universal-modder/universal-modder/0.2.0.
- CLI installed in .venv/, Python 3.12.14. `um --version` passed.
- Dependencies: numpy, Pillow, PyYAML, mpyq. Git, FFmpeg, .NET SDK 10.0.400 available.
- fal/cloud generation is not configured and is not needed for existing-game model conversion.

## Read-only recon — confirmed
- reports/megabonk-scan.json: Unity IL2CPP 2023.2.22f1, Windows x64, no loader found, no local anti-cheat files found.
- Steam AppID 3405340, installed buildid 21750826.
- reports/azeroth-scan.json: native x64, engine not identified by scanner.
- Guest resources: Data/patch.MPQ and patch-2.MPQ through patch-7.MPQ (approx. 1.6 GB total).
- Knowledge-base Megabonk note reports BepInEx IL2CPP 6.0.755 working with game 1.0.69. This is prior author evidence, not verified on this install yet.
- Knowledge-base command initially attempted GitHub sync and failed with HTTP 403. Fix: use local knowledge path via UM_KB in lab wrapper.

## Route
Asset conversion into the host game. Replace enemy rendering without replacing enemy AI/spawning or player movement. The guest does not need to run concurrently. First validation: one converted animated enemy in an actual Megabonk run; preserve health, hits and deaths.
HUD is a separate module. Book-slot override is a separate runtime-only module.

## Ownership / rollback
- Original game folders have only been read so far.
- Prepare a separate Megabonk lab copy before installing the mod loader.
- Back up saves/config before any modded launch. Do not write permanent unlocks into the user's save.
- Any future launcher must use the lab copy and write its own log.

## Parallel branches
- ASSET_LOG.md: Azeroth MPQ/model conversion.
- BOOKS_LOG.md: Megabonk type research and four tome slots.
- UI_LOG.md: original Azeroth UI analysis and host overlay.

## Next
1. Copy host into lab, obtain pinned official loader, back up saves/config.
2. Launch lab host once to produce IL2CPP interop and loader log.
3. Hand interop to workers; integrate modules; test real gameplay and capture evidence.

## Scope extension — user-confirmed
- Preserve Azeroth Survivors abilities AND their upgrade progression. Native Megabonk weapon progression is therefore not the final ability system.
- Preserve Megabonk books and four tome slots. Ability/hero selection still unspecified; research original before selecting a first class.
- Fixed um.ps1 state override to UM_HOME (actual supported setting).

## Host lab setup
- Original host copied to Megabonk-Lab/.
- BepInExPack IL2CPP 6.0.755 official Thunderstore archive SHA256 A3EC977C7760C6045B742A9BF3E59C7B11BAA80B023F402EBDBFF07BAD6B0D96.
- Loader installed into lab copy only. User save backup in um-data/backups/megabonk-before-lab; PlayerPrefs registry export in backups/.
- No original game binaries changed.

## First host launch — confirmed
- Lab process PID 48756. Main menu visible, version 1.0.69. BepInEx generated 108 interop DLLs.
- SteamAPI_Init reports failed for this lab copy; game still reaches menu without bypassing any ownership check. Modded testing stays local.
- Agent books confirmed InventoryUtility.GetNumAvailableTomeSlots() as the correct runtime-only hook; GetNumMaxTomeSlots shares native code with weapon slots and must not be patched.
- Agent assets found Vanilla MD20 v256 boar, 401 vertices, 504 triangles, 57 bones; original animation baking in progress.
- Agent UI found embedded original localization JSON and Lua strings in Azeroth EXE; numeric native data still being investigated.

## Modules built, awaiting game verification
- BookSlots compiled .NET6, runtime tome slots=4 hook only.
- EnemyVisuals compiled .NET6, native Enemy.InitEnemy postfix, models/animations loaded from AzerothAssets. Native AI, colliders, spawner and movement retained. Bosses not replaced in first slice.
- Added runtime-only guards against lab leaderboard score uploads; user preferences untouched.
- EnemyVisuals nullable attributes incompatible with Unity interop; fixed using Nullable=disable and removing reference annotations. Build now 0 warnings/0 errors.
- Enabled immediate file-log flushing and log append in lab BepInEx config; previous 0-byte logs were buffered.
- Original Azeroth menu screenshot confirms v0.102.3 (f78e6fab); selected Mage for reference only, no game update installed.

## User requested background-only work
- Physical Escape stopped Computer Use. No further UI/input calls will be made this turn.
- User requested work without occupying PC; all workers notified. Source research, conversion, compilation and noninteractive checks continue.
- Actual in-game verification of enemy replacement and tome slots is still pending; do not claim complete.
- Own interactive test processes received graceful close requests; original host folder unchanged.
- Runtime loader log verified both modules load; no first-call slot override observed before UI stopped.
- Original Fireball tooltip captured and native multiplier x1.4 independently found by abilities worker.

## Background-only test prepared
- HUD module deployed to lab plugin folder, original verified textures beside it.
- Six converted creature packs copied to lab (validation handled in ASSET_LOG).
- LabSmoke compiled .NET6 with no errors; only activates with AZEROTH_LAB_HEADLESS_TEST=1.
- Test uses Unity -batchmode, D3D11, muted audio, max30fps, no OS input. BepInEx console disabled. Test-only SaveManager.Save guard installed.
- Headless runner calls native map/run methods and writes reports/headless-smoke.log; it quits automatically. No visual success claim until evidence exists.

- Background process PID58748 had MainWindowHandle=0 (no desktop window). Loader smoke module failed: SaveManager has no Save method; corrected target list to real SaveConfig/Stats/Progression/Controllers/Temp methods from dump. Initial headless run stopped before gameplay.


## Headless test result and correction
- Verified actual Unity batchmode=True, Direct3D11, MainWindowHandle0; native map/run started, native enemies spawned, collider=True.
- Tome slots=4 verified through repeated actual native calls. Test saves suppressed.
- Visual loading failed on IL2CPP ImageConversion.LoadImage Span/GC bug; source-backed fix uses bottom-up RGBA and pinned IntPtr LoadRawTextureData.
- Improved animation memory: each source frame becomes one shared Mesh with recalculated normals/bounds; enemies switch sharedMesh, no per-enemy per-frame vertex uploads. Shared meshes owned by Model cache.
- Model failed-load negative cache prevents repeated failures/log floods. Native material/gameplay remains fallback.
- Offscreen camera capture added to test for rendered model evidence without any desktop window/input.

## Integrated background build
- Updated HUD0.1.1 raw textures and EnemyVisuals pinned raw texture upload.
- Deployed source-backed Mage ability module+mage.json; native original upgrade math recovered (damage/cooldown/targets/radius) and implemented by abilities worker. See ABILITIES_LOG and native disassembly evidence.
- Known remaining fidelity: only implemented Mage spell subset, original talent/racial/crit behavior and VFX not yet reproduced. Native Megabonk rarity/card probabilities are currently the upgrade selection adapter.
- Test selects Mage through module API only; no desktop input/window.

## Integrated background verification — PASS
- Actual headless Unity: isBatchMode=True, D3D11; process MainWindowHandle0; no desktop input.
- Native MapDesert tier1 loaded, player initialized, native spawner produced Mummy enemies with native colliders.
- Boar visual attached, 401vertices and 5 original animation clips loaded; no repeated texture upload failures after pinned raw fix.
- Original Fireball source damage70.7/cooldown1.82 applied to native enemies; real HP reductions and kills logged.
- Original common DAMAGE upgrade applied: rank1→2, source damage70.7→86.76304, cooldown1.82 unchanged; native tome slots stayed4.
- Test stopped itself after20.3s native aliveTime (8probes); save writes suppressed. reports/headless-smoke.log records all steps.
- Offscreen camera render saved as PPM and converted losslessly to reports/headless-enemy-render.png; visual inspection follows.
- Prototype still has fidelity limitations listed README. Do not describe all Azeroth classes/abilities or exact UI as complete.

## Visual inspection correction
- First offscreen camera image contained terrain but no clearly visible creature; runtime attachment logs alone were insufficient proof.
- Correcting scale normalization to authoritative native collider world bounds divided by renderer parent world scale. Added actual renderer bounds telemetry to diagnose visibility.
- Repeating the bounded background probe because the rendered evidence exposed this unresolved issue. User PC remains untouched.

- Scale root cause identified: Collider.bounds is EMPTY while native pooled enemy collider is disabled during spawn. Switched to intrinsic CapsuleCollider.height/radius/center plus world transform (valid while disabled); offscreen camera also targets intrinsic center rather than empty bounds center.

- Transform evidence showed native renderer import rotation/offset is unsuitable for Unity Y-up guest meshes. Changed visual parent to native physics root, positioned guest base center at intrinsic capsule feet, and visual-facing follows native rigidbody velocity. Movement/collider code remains untouched. Camera distance now scales with subject height.

## Final prototype checkpoint — 2026-10-20
- Actual offscreen Unity render visually inspected: original textured Azeroth boar is visibly present in the native Megabonk desert scene. Correct physics-root placement confirmed. Screenshot reports/headless-enemy-render.png.
- Latest headless probe completed, native enemies/colliders active, source Fireball/rank2 upgrade verified, all4book slots retained.
- Test module removed from normal active plugin folder; no game processes or visible windows left running by this work.
- Normal launcher Start-AzerothMegabonk.cmd is ready but has NOT been opened; user asked not to occupy PC.
- Universal Modder installed globally in Codex; CLI+project+all logs live under D:/Games/Saitan/AzerothMegabonkLab. Source and logs have local Git snapshots; no remote/publishing.
- Remaining: complete classes/spells/talents/racials/original VFX, full interactive HUD fidelity, boss appearance integration, long gameplay performance/pooling checks. This checkpoint is a first playable Mage prototype, not the full requested Azeroth port.

## Crash investigation and test battle auto-start
- User reported crash and unclear Mage selection. Preserved logs in D:\Games\Saitan\AzerothMegabonkLab\reports\crash-20261020-202103.
- Fatal stack points to AzerothHud.HudBehaviour.OnGUI / GUI.DrawTextureWithTexCoords native texture argument; migrating HUD drawing away from IMGUI.
- Preparing a separate native battle auto-start launcher with Mage, four tome slots, and test save protection. No desktop input or foreground launch.


- Deployed Mage selector/ability icon fix, HUD 0.2.0 native Canvas, and TestBattle auto-start. Started batch graphics smoke with spell visuals enabled; PID 57512. Test save guard enabled; no desktop input. Results pending.


- Smoke attempt1 found selector generic GetComponentInChildren interop verification error, now replaced by native Resources lookup. Attempt2 verified Mage selection, 5 raw icon uploads, native Desert Stage1 auto-start, 11 spell hits, 4 tome slots and clean exit; also found stripped legacy Font API in new HUD. HUD is being converted to existing native TMP font. Strengthened final smoke to require full Azeroth HUD nodes/render, not only native gameplay. Earlier attempt logs retained.


### Verified checkpoint: Mage battle auto-start and crash fix
- Default Start-AzerothMegabonk.cmd and new Start-Mage-Battle.cmd now auto-select Mage and launch native MapDesert / StageDesert1, tierIndex0, then start combat automatically. Separate Start-AzerothMegabonk-Menu.cmd preserves access to native menus and visible Mage selector.
- HUD fatal GUI.DrawTextureWithTexCoords path removed. HUD0.2.1 uses native Canvas/RawImage/TextMeshPro; live borrowed font/material, prepared RGBA uploads; 59 image nodes and 33 label nodes verified.
- Original five Mage upgrade-card icons use pinned RGBA uploads; unsafe ImageConversion.LoadImage removed. Selector's unsupported generic child lookup replaced by native Resources lookup.
- Final batch graphics smoke PASSED: native auto-start, active Mage/fireball damage, 4 tome slots, active complete Azeroth HUD92nodes, 5 icons uploaded, managed GC and native Canvas camera render. Process59084 had no desktop window (handle0), no input, self-exited after30 combat ticks. Runtime ErrorLog empty.
- Evidence: reports/mage-battle-smoke.log, reports/mage-battle-smoke-player-attempt3.log and reports/mage-battle-canvas.ppm. Earlier failed attempts and user's fatal logs retained separately.
- Test-session save writes are suppressed. Mage currently changes abilities; hero appearance remains the native Megabonk character. This was a short offscreen runtime check; a long interactive play session is still unverified.


## Full Mage port and HUD correction
- User screenshot confirms native and Azeroth HUD overlap. NewHUD0.3 masks exact nativeHUD graphics/weapon+tome grids, preserves encounter/objectives. Background graphics test starting.
- Parallel work: original Human Mage composite character/animations, all13nativeMage spells/progression, source spellVFX via localDBC/MPQ (32M2,67textures,140emitters,61meshpasses). Corrected BLP2 palette alpha decoding, no sourcegame writes.


### Source Mage integrated checkpoint
- Original HumanMale Mage robe/model and12nativeclips nowrenderlive; sourceheadportraitcamera feedsHPframe. Native playerbody/weapon renderers hidden, native movement/camera/collidersretained.
- All13sourceMage spells and5originalupgrade types integrated; source65checks pass. Native runtime47casts38hits over32seconds, nofatalErrorLog. Groundticks,cone,frostcontrol,IceBarrier,Polymorph passive implemented.
- HPgeometry aligned to originalPlayerFrame.xml; testedhurtstate65/130 withliveheal changes andoriginalshield81.9/130. Nativeweapon21graphicsandnativebook17graphics hidden0shown; 4ownbookslotsalwaysvisible. WorldHPunderfeet mask added0.4.1, finalruntime pending.
- SourceMageVFX32models/67textures/140emitters/61meshpasses plus7ribbons prepared. Sourceparticleadaptationlimitations recorded MAGE_VFX_LOG.md. Originalgameinputs nevermodified.
- Talentmodule39nodes/3trees,sourceXPthreshold51pointscap,rewardformula+198offlinechecks integrated forruntimeArcaneprobe. WaterElementalnativecastermath beingverified beforefinalFrostintegration.


### Ready to play: maximum Mage talents
- User explicitly requested999testpoints andalltalentsmaxBEFOREbattle. Launchers nowpass -azeroth-test-battle -azeroth-talent-points=999 -azeroth-talents-max. PluginLoad preallocatesall39nodes tosourceMaxRank beforeRunclone andnativeScene/inventorystats:121spent,878remaining. Normalprogression51pointcap unaffected; teststate notsaved.
- Final fullnativegraphicsprobe PASSED40combat ticks: sourceMage+portrait+VFX; all13spells;4bookslots; weapon/tome nativegrids0shown; playerworldHP12graphics hidden; F6talentpanelrender/open/close passed;999spend/refund livepassed withoutchangingranksoflearnedspells.
- Completeall39maxbuild was exercisedinruntime:121ranks/878remaining. WaterElemental made9sourceprojectileattacks,lastdamage56; echoes17/freecasts17/kill-explosions15. No fatalErrorLog; ownedtestprocessselfexited. NativeRunHPandShield/portraitrenderverified.
- Talentsourceofflinechecks204PASS. Abilitysourcechecks65PASS. TestharnessJsonCaseproblem andearlierpanelnativeobjectlifetimeerror corrected; attemptlogsretained. Finalproof reports/mage-battle-smoke.log andmage-final999-player-attempt2.log.
- Added startupmaximumallocationafterthepassedruntimeall-maxprobe; samevalidated39-nodeallocationrunsfromPlugin.Load. No additional long interactive test. Sourcephysics/controladapters andsourceVFXfidelitylimitations remainincomponentlogs.


## 2026-10-20 — HUD 0.5.0, closed Mage head and native boss sizes

- Restored the action-bar backing from the original Azeroth atlas blank stone strip and silver rails. No baked empty weapon grid is used. The bar and original gryphons have an 8-pixel lower inset; book and learned-spell cells share one row.
- Run timer font changed 28 -> 98 (3.5x), with a larger centered text rectangle.
- Native ItemsHud is moved above the bar and gryphons, preserving its own inventory updates and stack counts. Placement is calculated from real screen bounds, does not accumulate offsets, and restores outside the custom HUD. In the native test all three granted items were visible: bottom216px > required176px at1080p.
- Offscreen proof initially missed native item artwork because MainCamera excludes its UI render layer. The capture temporarily includes active Graphic layers and restores the original camera mask afterward; actual gameplay overlay rendering was not changed. The new screenshot visibly confirms all three items.
- Mage export now includes original scalp/ears/back closure geosets1/702/1501:810vertices/918triangles, all12clips rebuilt. Opaque original texture unchanged. Rear-head source checks12/12 and native HeadClosed=True/vertices810 passed.
- Ordinary enemy visual height is2/3 of the prior collider-normalized size. Boss visuals use the original Megabonk renderer height1:1; imported Azeroth Ogre/Infernal/Lich/BoneGolem packs added. Runtime probes: Golem15.984662, Pharaoh28.003963, CalciumDad18.657084 world units, each matching its native reference. Native movement, health, AI, spawning and colliders unchanged by visual code.
- Background native D3D11 probe PID35172, desktop window handle0,40combat ticks: PASS; Mage damage, original model, timer/backing, three actual items, all4books, native UI masks and3boss variants verified. A stale dead-enemy collider assertion in an earlier harness attempt was corrected; previous logs retained. Final proof: reports/ui-head-fix/ui-boss-smoke-passed.log and ui-boss-player-passed.log.
- Repository origin set to https://github.com/Solece/MegaAzeroth.git; branch main. Private game assets/binaries and local reports remain excluded by .gitignore. Native portal progression is undergoing its separate full-route probe.

## 2026-10-20 — final native route and map navigation checkpoint

- Actual native route PASS: Desert1/Pharaoh1 -> native portal -> Desert2/Pharaoh2 -> native portal -> Desert3/final gate -> FinalBossMap/live Pharaoh3. The same inventory and learned Arcane Missiles rank2/Fireball rank1 persisted, with all4books and121spent talents. Available native maps are Desert/Forest/Graveyard; their menu tiers are unlocked by runtime getters without completion/save forgery. Test battle selects native maximum tier so the route continues beyond stage1.
- Stage changes now rebind Mage scene-owned projectiles/statuses without clearing learned spell instances or upgrade factors. True new runs still reset normally.
- Fixed map marker masking in MageVisuals0.1.3: exact native player icon and both native MinimapArrow classes/arrowDict renderers are protected from skin hiding. Native close-target arrow hiding is retained.
- Navigation binds the current native boss gate after generation, observes the original full-map RGB fog pixels and native bossSpotted bit, then registers the ordinary native direction arrow. Gameplay does not reveal unexplored terrain. In the batch probe only a native radius110 fog circle was revealed: gateRGB0->255 with alpha255 retained; all other fog remained.
- Final native map proof: moved the player beyond the minimap, confirmed matching player/icon XZ, far gate arrow active/rendered in map layer14, directionDot~1, hiddenByMage0. Original minimap256x256 and full-map1024x1024 RTs contained real pixels. Native Pause -> Map -> Resume completed; player marker and gate/arrow verified visually on both maps. Final proof reports/mage-battle-smoke.log; earlier near-target and capture-helper attempts preserved.
- HUD0.5.1 hides while native Pause/Map is open, then restores in battle. The capture helper temporarily suppresses map-only 3D meshes when adding native UI render layers to the main offscreen camera. This removes the giant blue player-map marker from combat proof images; real gameplay camera keeps its original layer mask.
- Source, logs and launchers are checkpointed in Solece/MegaAzeroth. Original game installations/progression are unchanged. Test launch retains999points/all39max,121spent878free and4books.

## 2026-10-20 — persistent scene visuals, grounded bosses and original Mage weapon

- Root cause of broken appearance after travel: native Unity unused-resource cleanup collected cached runtime Mesh/Material/Texture objects held only by managed dictionaries. Original Mage/companion/enemy/VFX resources now survive native stage cleanup; transient failures retry with native-null checks.
- MageVisuals0.1.5 adds the installed source Mage starter staff: CharStartOutfit10, item35/display472, original 52-vertex mesh/texture, hand attachment1/bone104, twelve matching baked clips. Native weapon renderers and attacks remain suppressed, including newly created player-owned objects. One invisible non-attacking native inventory placeholder is retained for engine initialization. Native additional weapon grants are blocked; source spell choices replace native weapon cards without changing books/items.
- Boss placement now follows native collider feet and nearby real ground, including each source animation frame. The reported MinibossScorpion/Tarantula was previously anchored to renderer minimum -19.915 rather than collider feet12.994; the corrected visible source model stays above the floor. Ordinary enemy size2/3 and native boss reference heights remain.
- Removed deep per-frame VFX-cache scans and per-spawn enemy-frame scans; full validation remains in diagnostics/recovery. Native card hover/focus is preserved. Duplicate native boss-arrow registrations reuse the existing arrow and portal color.
- Final actual native D3D11 background probe PASS at23:01:02+05:00: Desert0/1/2 -> FinalBossMap, two stage bosses defeated and live Pharaoh3. Every scene forced Resources.UnloadUnusedAssets: Mage325 meshes, staff325 meshes, all11 enemy models, VFX8053 meshes/67 textures stayed valid; no graphics recovery required. All13 spells/ranks,121spent talents and4book slots survived. Actual native levelup selected one Azeroth spell card; four real Aegis AddWeapon attempts were rejected without inventory growth;1388 native weapon ticks suppressed. Latest-run BepInEx slice had no new adapter/interop exceptions; expected standalone SteamAPI initialization messages are separate.
- Earlier probe attempts retained: first Update could see the previous inactive staff before LateUpdate rebind; readiness now waits with a deadline. A second attempt hit the existing64-effect budget; batch probes temporarily delay new automatic casts while checking cleanup, keep existing projectile lifetimes ticking, then restore casting. Normal launchers do not enable these probes.
- Procedural environment research saved in docs/PROCEDURAL_BIOMES.md. Verified original Elwynn textures/props/buildings exist. Proposed first biome keeps native generation/physics/spawns/portals with original Elwynn materials and props; this map replacement is not yet implemented.

## 2026-10-21 — first procedural Elwynn biome (implementation started)
Route: scoped BepInEx IL2CPP StageData/MapData clones before native StartNewMap; original M2/BLP environment exported locally. Native generation, movement, ground physics, enemy spawning and portal flow retained. Existing laboratory profile/config/launchers backed up to D:\Games\Saitan\AzerothMegabonkLab\backups\elwynn-20261021-003848. All source game installations stay read-only. Knowledge-base Megabonk build1.0.69/Unity2023.2.22f1 route confirmed against existing local project.

## 2026-10-21 — procedural Elwynn v1 installed and verified

- Replaced the first native Desert environment through private StageData/MapData clones and the original native RandomObjectSpawner. Original generation/map-parameter/timeline/special-tile references, movement, physics, spawning and portal flow remain. Original Elwynn M2/BLP assets are exported locally; game assets stay excluded from Git.
- Added6 original ground textures/15 static prop types, real source alpha passes, retained resource caches, correct triplanar scale/mipmaps and post-generation world-edge rebinding. Original native ground landmarks retain geometry and colliders.
- Passage protection uses the measured native capsule and real native physics. Seed31415 proved243.835m to the gate approach; native seed38203351 proved752.715m and removed one blocking added prop. Native seed27183 used an explicit added-collider-only fallback, without claiming a route pass. All86 native interactables retained.
- Final first-biome probe PASS01:19:10:65.78343m native movement, four native enemies, unchanged capsule, source effects exercised and22 textures/21 meshes valid after explicit unused-resource cleanup. Native RNG restored16/16 times across8 decoration groups.
- Established full adventure/visual regression PASS01:18:02: Elwynn -> Desert2 -> Desert3 -> FinalBossMap/live Pharaoh3, two native bosses and portals, all13 source spells/ranks,121spent talents and4books retained. Original models/staff/VFX survived per-scene cleanup. A duplicate test-only boss driver was retired after its native Kill assumption failed; production boss logic was preserved.
- Installed tested Elwynn DLL and61 verified private pack files. Added Start-Elwynn-Mage.cmd; current quick launchers now start Elwynn, menu enables the first-stage replacement. No user game was launched or stopped. Detailed evidence/limits: ELWYNN_TEST_LOG.md.

## 2026-10-21 — Elwynn buildings, multi-stage environment and combat/UI fixes started
Native movement/generation/spawn remain in use. Work scope: source WMO buildings with route-safe placement, Elwynn on subsequent native stages, single golem model, native tome compatibility, source XP bar and paused learned-spell inventory, movement-only Mage animation/slide loop, frozen spell simulation in native pause/shop/levelup. Previous runtime checkpoint: D:\Games\Saitan\AzerothMegabonkLab\backups\elwynn-v2-20261021-014042. Game probes use the isolated test runtime; original installs/read-only saves remain untouched.

## 2026-10-21 — playable Elwynn v2 installed

Installed route-safe original buildings on all three ordinary stages, single-body golem with native spawn effects, real native tome compatibility, paused source simulation, source XP/inventory/results and movement animation fixes. Full native route PID61008 passed through both portals and the final arena. Latest feature PID51996 passed21 checks with0 failures; unavailable native Bounce tome is explicitly skipped. HUD0.7.6/Talents0.1.1 hotfix restores native gold, removes the complete top XP strip, offsets the player portrait, replaces encounter/result/pause weapon rows with all learned spells without scrolling, and fixes talent damage labels/icons and additive Luck. Backup/hash manifests retained; user game left running. Details and known limits: V2_INTEGRATION_LOG.md.

## 2026-10-21 — original concurrent talent window installed

Replaced the flat list/intermediate tabs with three simultaneous source trees plus sidebar, following recovered original Rust layout/constants. Source backgrounds, icons, rank values and prerequisite links retained. Mage-compatible0.1.3 passed23 native checks and rendered inspection before installation; backup/hash records retained. User game not stopped. Full nine-class gameplay remains isolated pending its own runtime validation.

Background model-check incident: a standalone development utility raised unhandled .NET parsing/assertion failures and displayed a Windows crash dialog. The exact owned ModelChecks process was closed. The utility now uses UseAppHost=false and a top-level exception handler with stderr/nonzero exit; controlled failure returned1 with a log, normal checks returned0, and no ModelChecks/WerFault processes remained. No Windows/WER/global registry settings changed.

## All-class integration: final isolated candidate

- Integrated frozen F3 source (42E8CC09) with fractional native healing/carry, shield admission, source-clock DoT delivery and noncritical DoT ticks. This source is not yet installed in the user runtime.
- Added explicitly armed VitalsProbe and optional native test-menu mode: nine source choices, temporary max999 talents, native saves suppressed and no battle autostart.
- Hunter first pause-resume check failed without a state snapshot; retained as failed evidence. Repeat PID65168 passed112/0 with recorded actual native resume, no production unpause override.

All-class native startup regression: Roster0.1.3 introduced a PassiveData.GetName hook at native RVA381570, shared with StageData.GetName already hooked by Elwynn. Shaman65624/52368 stack-overflowed before class fixture admission. Removed only the shared-address GetName hook in0.1.4; source names remain in CharacterInfoUI, unique PassiveData.GetDescription RVA38D030 retains the SmartFormat fix. Retained failed reports; native rerun required.

F4 source vitals: static native GetStat included selected talent HP, but PlayerHealth constructor cached source base only. After native StartPlayer and complete talent ResetRun postfix, refresh native MaxHealth/HealthRegen caches. Fill only a new run; preserve current HP/fractional carry/overheal on stage refresh. Actual cached-max assertion added; all-class acceptance restarts with this final candidate.

## All-heroes v3 accepted

druid PID12668 116PASS/0FAIL; hunter PID26292 113PASS/0FAIL; warrior PID64712 112PASS/0FAIL; paladin PID53516 113PASS/0FAIL; rogue PID52240 112PASS/0FAIL; priest PID42180 114PASS/0FAIL; shaman PID66396 113PASS/0FAIL; mage PID47352 119PASS/0FAIL; warlock PID61420 113PASS/0FAIL

Packaged menu47840 passed195 checks; packaged Mage67540 passed. Feature62292:20PASS/0FAIL/2SKIP. Native vitals Druid/Paladin/Priest passed. Warlock63996 retained original permanent pets and all11 spells through3ordinary stages,2stage bosses and live Pharaoh3 final arena, including forced UnloadUnusedAssets on4scenes. Standalone source checks696 passed; controlled failure exit1 is handled, successful exit0, no Windows apphost. Build accepted beside stablev2; no user game process stopped. Original source assets stay local and are excluded from Git.

## Post-combat portrait hotfix

HUD0.8.1 prefers the persistent original class portrait in results; the live gameplay portrait is unchanged. Talents0.2.2 hides the F6 hint outside an active run. Feature66620 passed20 checks with0 failures/2 explicit skips, including the actual active DeathStatsUi texture name. Updated playable-v3 hashes; no user game stopped. Nine-class gameplay acceptance remains applicable because gameplay modules are unchanged.

## VFX fidelity audit started

User reports missing death effects and invisible Warlock Rain of Fire. Baseline de3bf82, user's playable PID57736 left running. Isolated runtime private-assets/vfx-fidelity-runtime created with independent BepInEx and class VFX assets; baseline DLL hashes retained. Audit source spell stages, M2 particle bones/blends, native death/pool lifecycle, and active-effects capacity. All experiments and screenshots stay isolated.


### 2026-10-21: explicit test decor density and fidelity checkpoint

- Requested test-only obstacle/decor density: `-azeroth-decor-density=1.2` is accepted only with `-azeroth-test-menu` or `-azeroth-test-battle`; all other modes/values retain multiplier1. Applies to11 owned Elwynn source groups across the three normal native stages. Native terrain, interactables, enemy spawning and final arena are unchanged.
- Native run62636:258 baseline requests ->309 requests/placements, then22 clearance removals ->287 active source roots. Integer rounding and original terrain/door/boss/portal clearance remain authoritative, so final survivors need not be exactly20% higher. Native RNG was restored for11/11groups,0failures. Evidence retained in private-assets/vfx-fidelity-runtime/reports/vfx-fidelity-62636 and native generation log.
- Combined Warlock/Rain/death/shield/budget run62636:56PASS,0FAIL,2explicitSKIP. Independent Quantity/targeting run55444:31PASS,0FAIL,1explicitSKIP. Nine classes in native character/map selection59864:231PASS,0FAIL; screenshots use actual postprocessed camera rather than composited preview.
- Rejected Ghost palette baseline48604/63752: all four ordinary tier albedo multipliers are white; actual tier colors come from distinct native `_AlbedoMap` textures. Runtime inventory retained; palette correction pending. Never infer tier color from debuff albedo alone.
- Full nine-class VFX matrix started; results remain pending. No current player process or v3 runtime modified.


## 2026-10-21: all-heroes v4 acceptance


## 2026-10-21 — v4 candidate: source VFX, targeting and presentation

Source-only changes prepared against the accepted all-heroes v3 baseline. This entry records candidate evidence; it does not announce a packaged v4 release or replace the accepted v3 gameplay matrix.

### Spell VFX and reproducible export

- `rain_of_fire` (DBC 5740, SpellVisual 329) uses the original `Spells\RainOfFire_Impact_Base.m2`. Original emitter attachment bones 13/14 were previously ignored, leaving high particle origins static. Exported bone samples now drive particle/ribbon positions. Ground-area visuals use the native ground mask and raycast; the gameplay area center, damage radius and tick schedule are unchanged by visual anchoring.
- Corrected the source SpellVisualKit schema: ten model attachments are fields 3–12; fields 13/14 are SoundID/ShakeID. The corrected all-class graph has 112 identities, 421 links, 150 referenced M2 models, 171 textures and 130 bone buffers. Original material flags/alpha are retained. Nested model particles and specialized source simulation remain fidelity limits.
- Bounded admission remains 64 active M2 model instances, with four reserved for area/areaKit effects. At capacity, an area may evict only an old non-looping cast/impact/precast cosmetic effect. Missiles, auras, channels and gameplay projectiles are protected. Explicit drop/eviction/renderer diagnostics replace silent admission loss; an all-protected pool can still reject a new visual.
- Portable source export uses `scripts/azeroth-vfx/{probe.py,export_vfx.py,augment_assets.py,validate_vfx.py,vfx_paths.py}` and the accompanying README. Clean export passed 4025 checks; 150 model JSON and 130 bone files match the frozen VFX payload by SHA256, and all 421 binding tuples match. The 295-file historical overlay includes stale cache entries and is not the fresh-export file-count contract. Generated game data stays private.

### Requested targeting adapter

Volley targets are ordered by actual distance and unique native enemy ID. Each projectile takes a distinct nearest target before round-robin reuse; delayed launches retain volley claims. Dead/pooled targets are retargeted to the nearest unclaimed eligible enemy using the remaining travel budget. Quantity and native tome bounce semantics are retained.

Initial source/override detection scans and projectile reach are multiplied by 1.2. Ground-area initial acquisition and totem/passive activation scans share that detection change. Damage AoE/cone shapes, area ticks, chain-hop radius and established totem attack radius are unchanged; source auto-weapons and pet AI acquisition are outside this change. This is an explicit user-requested adaptation, not an original-source rule.

### Death, Shield, menu and test decoration

- Detached source Death playback lasts 1.2s of pause-aware combat time and survives native pool release. It adds no collider, physics, rewards or generic elemental death burst. Existing diffuse model material is retained; omitted embedded body particles are not claimed restored.
- Native Shield/PlayerShieldEffects and particle subtrees are excluded from character-skin hiding. Native pickup activation, lifetime, pause and expiration remain authoritative. The fixed issue is suppressed native presentation, not a new immunity implementation.
- All nine source hero previews retain body, equipment and idle animation through native map selection. Preview camera DOF focuses on the actual rendered bounds; saved native focus/aperture/override states restore on release. Concrete native FloatParameter writes and readback avoid the broken generated generic float marshaller.
- `-azeroth-decor-density=1.2` is guarded by explicit test-menu/test-battle mode and Elwynn activation. It changes the amount multiplier for exactly 11 original decor/obstacle groups. Native ground/interactables, placement/clearance and final-arena palette remain unchanged. In 62636, native seed 1526482981 requested/placed 309 objects versus 258 baseline requested; clearance removed 22, leaving 287. This is one observed test fixture, not proof of exactly 20% more surviving objects on every map or a paired-seed terrain comparison.

### Actual acceptance and remaining work

| Native run | Result | Scope |
| --- | --- | --- |
| 62636 | 56 PASS / 0 FAIL / 2 SKIP | Real Rain cast, pixel contribution, native pause/VFX age, synthetic render load with peak 64 and area eviction, detached source corpse/pool release, actual Shield pickup/render/pause/expiry. |
| 55444 | 31 PASS / 0 FAIL / 1 SKIP | Real native Quantity tome and projectile constructors, distinct nearest targets, excess round robin, dead-target retarget, actual 1.1R included / 1.21R excluded, computed AoE radius invariant. |
| 59864 | 231 PASS / 0 FAIL | Native nine-choice menu, original bodies/equipment, map-tab persistence, hidden native placeholder and observed camera focus for all nine classes. |

62636 explicitly skips visual confirmation of falling-meteor appearance and natural-collision Shield HP protection. 55444 skips actual AoE boundary-hit proof. Nonzero geometry/pixels do not establish source-engine visual equivalence. The earlier 46604 menu fixture failed on the transition to its second class; 59864 is the completed nine-class acceptance.

Ghost0.2.7 accepted by native34864: 126PASS/0FAIL/1SKIP. Actual native _AlbedoMap colors are sampled once per texture, composed with material-slot0 status modifiers, then applied to source luminance atlas/opacity. Alpha-over replaces additive source passes so actual black remains visible. Embedded particles remain unsupported. The first0.2.6 run46332 exposed generated managed methods in IL2CPP injection; explicit HideFromIl2Cpp on generated helpers fixes registration. Historical rejected runs retained.

Final native VFX matrix:9classes/102catalogrows,93explicitCAST/9natural-passiveSKIP;494PASS/0FAIL/24SKIP checks. Native pixels prove contribution, not pixel-for-pixel source raster equivalence. Read-only source review found no P1/P2 blockers. Harmony audit118bindings/70unique native signatures,0collisions/0unresolved.


Separate v4 packaged runtime accepted: native menu49112 231PASS/0FAIL; native Warlock Rain/death/Shield/budget67708 56PASS/0FAIL/2SKIP. Source-built DLL hashes and local asset overlay retained in builds/all-heroes-v4/build-manifest.json. Root launchers now point to v4; the active v3 user process was not stopped/restarted or patched. Native collision Shield HP and actual AoE boundary remain explicit probe skips. Source-only commit excludes all original/extracted game files.

Final Ghost visibility fixture correction: earlier25024 pinned the native root Y to the terrain and sank the actor collider/source mesh, producing misleading hidden-body frames. The fixture now pins only X/Z and preserves native vertical/flight movement. Native34864:126PASS/0FAIL/1SKIP, independent floor clearance for all five source bodies and real per-tier close-camera frames. No production ground placement was changed for this fixture issue. Original source Ghost is spectral and segmented; three embedded source emitters remain unsupported.

## 2026-10-21: Cat source materials, projectile arrival and stronger visual acceptance

User reports transparent holes in Druid Cat and projectiles disappearing when their target dies; later reports missing Paladin explosion and rejects weak visual-test criteria. Active v4 process68340 is left untouched. New work stays in isolated fidelity runtime and a separate future playable build.
Cat source audit: geometry complete742triangles, but global cutout wrongly discards alpha on opaque body/teeth. Original four passes670/60/8/4 triangles have flags0/4/1/17 and blend0/1/0/2. Planned fix retains source partitions/material modes; no geometry/texture patch. Reproducible exporter remains source-only.
Projectile root cause: invalid target with no retarget immediately returnsfalse/Dispose; old arrival distance may hit before actual visual arrival. Preserve bounded flight budget and volley policy; retain last valid world destination, move before arrival damage and keep nondamaging impact after target death.
Visual-test quality correction: nonzero mesh/pixel contribution is insufficient for expected effect/stage fidelity. Previous494PASS does not certify all original bursts. Paladin source-contract audit and real render comparison now required, including missing stages and appearance; no blanket equivalence claim.

### Cat and projectile native acceptance

Cat native51288:38PASS/0FAIL, original four material passes bound to every animated mesh, 3137 formerly transparent body pixels recovered against the actual legacy global-cutout shader. Five actual movement clips, unrelated Bear control and native resource unload passed. Earlier21440/35192/60720 fixture failures remain retained; the final comparison uses identical geometry with the actual old shader instead of an ineffective substitute.

Projectile native28720:69PASS/0FAIL/1SKIP. Actual ordinary Skeleton targets were killed during the same slow projectile's flight. Retarget flight stays visible and live reserve HP changes after exact visual arrival; no-replacement flight reaches the cached collider endpoint with a nondamaging impact. Computed AoE shape invariance passes; natural AoE boundary hit remains unverified. Source review then fixed a large-timestep accounting edge case: a nearby arrival must consume only actual segment length, preserving unused bounce range. The repository managed regression extracts actual current production projectile/targeting code and passes116 assertions in11cases; native rendering is not claimed by this managed fixture.

### Paladin source contract and recording acceptance

Consecration26573 omitted its initial cast stage. Original native14596100E dispatches the visual before persistent ground creation1459610F8; each source cast asset now retains its own1.067/1.0s duration. The area shares actual gameplay radius/8, including talent/tome radius.

Dedicated baseline49288 correctly failed missing initial cast roles, while recording the whole cycle instead of aborting. Fixed58924 passed58 mechanical checks, but recording failed at8.35FPS with.545s gaps caused by synchronous role-pair renders. Capturev3 separates the main timeline from a second real cast used for isolated role pixels; native62996 passed90checks, actual15.01FPS and maximum.126s gap. FFmpeg encodes measured frame timestamps as VFR. These results establish their declared contracts, not source appearance equivalence.

Actual original Azeroth reference was independently recorded by original nativeEXE (Rust/Bevy), positional elwynn/paladin/human. Ready Universal Modder `um win record`/FFmpeg captured two decoded30FPS videos and57 native screenshots. Wrong initial MainWindowHandle and short35s startup attempts were rejected; enumerating real nonzero-client render HWND and allowing startup succeeded. The120s reference run observed unchanged foreground HWND659244/PID60436 throughout; only owned original/capture processes were closed. Original source EXE/MPQ metadata remained unchanged, profile/save/cache were isolated. Reproducible scripts are in scripts/azeroth-reference; game binaries, extracted assets, raw disassembly and recordings remain outside Git.

Visual A/B then exposed a stronger source contract gap: the field emitter shape2 uses an equatorial ring at vertical0, while the old adapter emitted a vertical column. Original helper1418D2412..2495 proves the angular formula; source flags0x8000 use a one-shot burst latch, not continuous rate*dt. This is a failed fidelity finding despite the previous90 declared checks; source-backed direction/burst checks are being added. Terrain measurements found24/25 sampled quad locations above native ground and one tree intersection, so no unproven terrain/cull/height workaround was applied.

### Enemy model variety in progress

User requests more variety: source-validated six ordinary models (Ghoul137, Zombie1200, Scorpion2485, SilithidWasp92, Ent864, FleshGolem1061) and two boss models (DoomGuard433, DragonSpawnCaster6761). Exact native enum mapping preserves native RNG, AI, collision, spawn and size normalization (ordinary2/3, boss1); Ghost overrides remain first. Eight packs/28source passes include original texture assignment, blend/cull/depth flags and animated visibility. Native rendered acceptance is pending; no claim that exports alone prove in-game appearance.

The source field's200-particle burst now fits a bounded256 burst capacity (continuous emitters retain192); counters expose emitted/burst/clipped counts and all particle-center bounds. Signed sphere angular ranges, source radial birth offset, one-positive-edge burst with exact native truncf and source0x100/zSource velocity exceptions are implemented in the common particle adapter. Plane emission, bone-direction and0x1000 quad semantics were not changed. PSF1's ceil interpretation was rejected before deployment; PSF2 was followed by PSF3 to preserve source UP velocity for actual Trap/Shield flags. The actual production helper passes10390 managed assertions including those cases; these are not native visual proof.

Enemy variety0.2.8 source was promoted and compiled0/0; independent pack revalidation289PASS/0FAIL and Cecil lifecycle/hidden-helper guard passed. First native45656 rejected the diagnostic string IsKeywordEnabled call because the generated IL2CPP method requires unavailable ReadOnlySpan.GetPinnableReference; diagnostic keyword inspection now uses shaderKeywords (the same already-proven Cat workaround), with fresh0/0 build. Native acceptance is retried, not marked green from static review.

Paladin native66348 rejected a strict12m empty/flat forest fixture instead of claiming false flatness. An explicitly synthetic neutral32x32 QA floor is now created only in the armed batch process above the existing terrain and removed during cleanup; actual native player/Skeleton/cast/HP/pause are retained. No procedural terrain or source field mesh is modified. Native52944 exposed delayed native TeleportPlayer admission: the initial target point must use the chosen patch value, not the player's old transform before its next native update. The armed fixture records both actual positions before casting and preserves failed evidence.

### Final v5 acceptance and delivery

Packaged v5 native4708:1608PASS/0FAIL/0SKIP, all8 actual models/front+back poses/source-material bindings/topology/opaque-hole controls/native sizes, aliases, five existing Ghost mappings, Golem/GhostKing single-body checks, actual native resource unload, and all8 real Kill/source-corpse animations/lifetimes. Native67944 had1605PASS/3FAIL solely because the fixture compared a static source template with a different animation frame's live native SkinnedMesh bounds for Doomguard. The independent test now freezes actual native boss AABB immediately after real SpawnBoss returns, before its next LateUpdate, retaining3% tolerance and saving that native evidence. Production size normalization was unchanged. Native3592's height failures were fixture Y-pinning and invalid remote parking points; the completed fixture pins XZ while native Y/physics settle and parks in verified positions. No production floor workaround was added.

Packaged menu64848:all9 source heroes/map-choice previews,231checks/PASS. Source PSF3 native65440:95PASS/0FAIL, source200-particle once-burst, horizontal centers independently read from actual native mesh quads, no particles after1.5s, persistent original ground geometry, real periodic HP/pause/expiry and15FPS VFR recording. Source PSF3 Warlock41724:94PASS/0FAIL/5SKIP, all11 catalog rows with source passive/proc caveats, Rain/actual native pause, corpse lifetime, Shield and reserved VFX budget. Manual Rain frames show overhead fireballs; exact source-renderer meteor appearance and natural Shield collision protection are not certified by the legacy smoke probe. Consecration's original three caster attachment slots are still represented by two unique source assets; separate hand anchoring and full pixel equivalence remain unverified.

Reproducible managed checks against actual current production source: projectile116 assertions/11cases and particle10390 assertions, no apphost dialog. Original native reference capture57frames/two decoded videos is retained separately. Ready UM window recording and FFmpeg replace a custom capture/comparison stack; scenario hooks and source contracts remain specific to these games. Failed baselines, capture cadence failures and corrected fixture failures are retained as rejected evidence.

Independent all-heroes-v5 runtime packaged, per-plugin hashes refreshed. Future root All-Heroes launchers point to v5; previous v4 is retained and no user game was stopped or restarted. Six new ordinary species and two boss species preserve native spawn/AI/RNG/collision and existing ordinary2/3/boss1 size policy. Private game/model assets and recordings are excluded from Git; only source, exporters, metadata contracts, tests and logs are committed.

## 2026-10-22: Single launcher and private portable archive

User requests GitHub download and removal of obsolete launchers. Seven previous root launchers were archived under ignored backups and replaced by one Play-MegaAzeroth.cmd with PowerShell helpers. The same entry selects Test (default: 999 points, max talents, decor1.2) or Normal (native profile and saves). Previous build instructions remain in docs/BUILD_HISTORY.md. Original installed games and former builds are retained.

Initial public-mod bootstrap design required local Azeroth extraction. User clarified that prepared assets should be included, then explicitly requested a complete personal installation archive including Megabonk. User chose making Solece/MegaAzeroth private. Repository PATCH and subsequent GET verified private=true/visibility=private before any game upload. The complete runtime is stored as a private Release asset rather than bloating source Git history. This packaging scope is a personal backup, not a new license to redistribute either game's content.

The exact native game reports version1.0.69 (Steam build21750826, Unity2023.2.22f1); earlier shorthand1.0.6 was incorrect. Core EXE/GameAssembly/global-metadata hashes are pinned. Prepared assets were materialized from accepted v5:4249files/223997358bytes, no external links, no original Azeroth EXE/MPQ or rawM2/BLP/DBC/WMO.21 JSON provenance-only absolute paths were sanitized. Independent closure validation:5710PASS/0FAIL across model/clip/death/VFX/UI buffers. Accepted ten plugin DLLs remain byte-identical to v5.

The private bundle also includes original Megabonk files, the verified official BepInExPack_IL2CPP6.0.755 bytes, bundled.NET6.0.7 and the matching pre-generated interop. UpdateInteropAssemblies=false prevents runtime downloads. Personal profiles/saves/accounts/credentials, logs, caches, recordings and development folders are excluded. Licenses and source references are retained. WindowsPowerShell5.1 bundled-launcher fixtures:13PASS/0FAIL. Final extracted-native and upload verification remain pending until recorded below.

Fresh ZIP extraction in a different folder with Cyrillic and spaces passed the WindowsPowerShell5.1 full runtime manifest check. Owned hidden native menu62144 passed231checks across all9heroes, source bodies/equipment/portraits/map previews, temporary999budget and every selected class's maximum talents. Native rendered contact sheet was visually inspected. Matching interop loaded without generation/download. A second launcher validation after BepInEx generated its normal configs passed; the Normal argument set contains no test/max-talent flags. Additional extracted Warlock battle57608 is running before final release acceptance.

Extracted Warlock battle57608 completed94PASS/0FAIL/6SKIP in50.13s: actual class casts, source VFX geometry/pixels, real source corpse death/lifetime, Shield renderer/pause/expiry and VFX budget were exercised from the archive-only runtime. Skips remain explicit: exact Rain/source meteor appearance, passive Soul Fire free-cast/pixels, two incidental XP choices, and natural Shield collision HP protection. No source-engine visual-equivalence claim is added. Archive DLLs remain exactly accepted v5; packaged assets passed5710 closure checks. Fresh WinPS5.1 full verification, normal flags and repeat launch validation passed. No human game was stopped or controlled.

Ready private release: one Play-MegaAzeroth.cmd, complete native1.0.69 runtime, all prepared resources and matching loader/interop. All ZIP members will be re-read and SHA256-checked after final documentation updates before upload; server digest and private visibility will be checked before publication. Packaging scripts take paths as arguments and never alter source installations.

GitHub private release v0.5.0-private published2026-10-22T14:19:23Z: complete340102303byte ZIP, SHA256abe0c2e24553b9609acfc8468215b8f115e9fa1964f02515385ea2a32773bdf7,4695members/4667runtimefiles. GitHub server digest matched for ZIP/checksum/verificationJSON; private visibility rechecked before publish. Source commit67ddc2c2e83afa6487d53d68a96544f9e56b9bd0. No game files entered Git source history. All seven old root launchers are archived and removed; the sole root entry is Play-MegaAzeroth.cmd.

## 2026-10-22: Original menu and loading presentation

User additionally requests Azeroth main-menu background and map-loading appearance. Source reference work/reference/azeroth-mage-menu.png and native14027e1d0..14027f02e confirm selected-zone LoadingScreens art for the menu, RGBA tint(.4,.38,.36,1) and106%overscan. Earlier inference that menuScene AnimatedZone implied no background image was rejected against the actual source screenshot and direct native binding. Original Elwynn key loadscreeneasternkingdom and LoadingScreensDBCid4 confirmed; Loading-BarBorder/Fill direct native bindings retained. Five converted textures were re-exported from owned MPQ and matched hashes. RGBA is already bottom-up; no additional vertical flip is applied. Exact dark-gradient geometry remains an adaptation.

First isolated Presentation candidate75356 completed full native menu/character/map/loading/game/pause/end/return flow. Mage contributed3024actualpixels on character tab and10393on map tab in same-frame visible/hidden renderer pairs. Fifteen actual loading observations and full real-Canvas offscreen captures retained. Report95PASS0FAIL includes repeated phase5 checks and is not95independentcriteria. Review identified excess repeated test StartPlaying/capture while waiting for pause and poor sample coverage of loading fill; fixture is revised. Visual inspection found source menu art obscured by native depth-of-field on the main title even though map selection was clear. Candidate is not promoted until menu DoF-only override/restore and sampled real fill geometry are validated. No human process is modified.

## 2026-10-22 — menu/loading native QA, rejected candidates

- Presentation v4 native PID28348: timeout at phase0. Production art/masks/DoF were active without errors, but fixture incorrectly read Graphic.color.a instead of CanvasRenderer.CrossFadeAlpha opacity. Candidate not promoted.
- Presentation v5 native PID62036: source main/map art visually reviewed, actual Mage contributions11909/12904 pixels; actual loading reached25/50/90 percent. Off/on restored identified native menu scenery. Failed fixture at end-run through an older HUD diagnostic retaining a destroyed pause object. Also detected an incorrect pre-layout loading ROI and premature next-stage admission. Real same-frame fill difference bounds independently measured at (368,487,448,502); a rendered bar is not accepted merely because data says progress changed.
- Failed reports, source freezes and captures retained in isolated QA. v6 fixture must require a new native scene/player, measure the actual camera-bound fill rect, and call native KillPlayer/ShowStats directly. No failed candidate installed in the user's playable runtime.
## 2026-10-22 — accepted Azeroth main-menu and loading presentation

- Accepted AzerothPresentation0.1.6 frozen-v7, DLL SHA2560ab8ef41c64b68c7679f5fd361795b72b19adf138970ca385285ac1447781883. Source Eastern Kingdom art, tint(.40,.38,.36,1),106% overscan, original bar border/fill; vignette geometry and bar placement remain documented adaptations. Original loading layout was not recorded.
- Native PID63888:22PASS/0FAIL/0SKIP in23.22s; real main/character/map UI, source hero pixel contributions, explicit off/on restoration, native loading, battle/pause/resume, changed-scene second-stage admission, results and return-menu. Two actual loading instances; no duplicate canvases or input raycasters; actual fill geometry follows native progress. Visible/hidden same-frame fill changed1453pixels inside measured1696pixel ROI. Actual next-stage call is a lifecycle fixture, not a natural portal/boss completion claim.
- Separate native nine-hero menu PID86572:231PASS/0FAIL, all9 classes and maximum test talents, original selected hero retained on map picker. Final main/map/loading/returned-menu frames manually inspected using actual native camera and Canvas transport, not OS-window capture.
- Closure5720PASS/0FAIL,1407 decoded RGBA buffers. Ten existing gameplay assemblies remain byte-identical to the accepted v5 portable runtime; presentation is an eleventh plugin.
- Rejected v6 completed lifecycle21PASS but fill ROI remainedzero due managedVector3[]→Il2CppStructArray input-copy marshalling. V7 uses an explicit native array and reads its written values. Independent review confirmed this from generatedinterop; no pixel threshold was reduced. Failed native logs and freezes retained separately.
- New independent playable all-heroes-v6 runtime and source-only Git module prepared; previous user runtimes remain intact. Single root launcher prefers the completed v6 build and falls back to v5.
### Private publisher metadata follow-up

During v0.5.1 upload, a read-only metadata check exposed PowerShell nested-array handling when Invoke-RestMethod is wrapped in @(...). Direct JSON-array assignment correctly lists the actual draft420856859 and published v0.5.0. The publisher now uses direct assignment before filtering tags, so retrying an existing matching draft does not mistakenly create a second release. The active upload and runtime payload are unchanged.
## 2026-10-23 — complete closed tester handoff

The owner requested a build to give a selected tester on another PC and explicitly requested Megabonk itself inside the archive. The tester package is based on the complete accepted v0.5.1 archive (SHA2560fb0190f3efbfb549b333716a62a2d1f512946ae2a693a6607c7fb30f4d40f60), including Megabonk1.0.69, all eleven plugins, prepared art and pinned loader/runtime. No gameplay assembly or native Steam check is changed.

Added a short Russian tester guide, one launcher with diagnostic menu3/-CollectLogs, and startup-error reports. The collector can run before integrity verification, including a broken installation. It includes only build metadata, three recent play logs, three startup-error logs, one BepInEx log and basic Windows/CPU/GPU details. Each file is bounded to its final2MiB, package/profile paths are redacted, and saves/account files/environment dumps are excluded.

Actual Windows PowerShell5.1 checks:11PASS/0FAIL, including incomplete runtime, startup failure capture, private-save/.env exclusion, bounded tails, path redaction and interactive menu3. Archive integrity and a fresh extracted native startup are recorded in the delivered verification JSON; the hardware remains the developer PC, not a second physical machine.
Tester diagnostic follow-up:12PASS/0FAIL on actual PowerShell5.1, including Unity forward-slash and case-variant path redaction. Runtime files remain unchanged.
