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
