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
