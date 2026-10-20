# Native adventure flow

## 2026-10-20 — full stage route, local runtime unlocks and Mage stage retention

- Root cause: test autostart selected native `RunConfig.mapTierIndex = 0`. Native `InteractableBossSpawner.CanSpawnPortal` permits a next-stage portal only when `MapController.index < runConfig.mapTierIndex` and the map still has another stage. The initial test consequently ended after its first tier boss.
- Added separate BepInEx `src/AdventureFlow` plugin. With `-azeroth-test-battle`, `MapSelectionUi.StartMap` selects the map's final native tier (`stages.Length - 1`) before native run creation. Normal menu runs retain the tier selected by the player. Native difficulty scaling for that final tier remains in effect.
- All existing maps and their tier buttons are temporarily available inside the lab through map-only `MyAchievements` getters and `MenuMeta.GetHighestCompletedTier`. No achievements are completed, no `completedTiers` arrays are populated, and no original progression files are written by this feature. This availability override does not unlock unrelated characters or items.
- Native boss release, native portal activation and interactions, `MapController.LoadNextStage`, and `LoadFinalStage` remain responsible for actual travel. A narrow post-release guard restores an accidentally inactive native portal after every summoned boss is released. A stale ordinary portal on the final stage routes to the existing final-arena loader rather than indexing beyond `stages[]`.
- Fixed `AzerothAbilities.StartRunPatch`: native `MyPlayer.StartPlayer` on a later stage/final arena now calls `RebindNativeStage` rather than `ResetRun`. Existing `LearnedSpell` objects, ranks, damage/cooldown/targets/radius/duration factors and upgrade-pick counts remain intact. Scene-owned projectiles, statuses and transient VFX are cleared. The talent snapshot is not reset during a stage rebind.
- Native inventory persistence remains owned by `MapController`: source disassembly confirms it transfers `GameManager.GetPlayerInventory()` into its retained inventory before native scene load. Four-tome-slot logic remains the existing BookSlots plugin.

## Reproducible background probe

- Deploy `src/AdventureFlow/bin/Release/net6.0/AzerothMegabonk.AdventureFlow.dll` to `BepInEx/plugins/AdventureFlow/` and rebuilt `src/AzerothAbilities/bin/Release/net6.0/AzerothAbilities.dll` to its current plugin folder.
- Set environment `AZEROTH_LAB_HEADLESS_TEST=1`; run with `-batchmode -azeroth-test-battle -azeroth-talent-points=999 -azeroth-talents-max -azeroth-adventure-smoke`. Do not combine with the unrelated 40-second `-azeroth-test-smoke` runner.
- The adventure runner seeds original Fireball rank1 and a real original Arcane Missiles damage upgrade (rarity2, rank2), captures current spells/ranks and talent allocation, interacts with native boss spawners, calls native boss `Kill`, waits for native portal activation, interacts with native stage portals and final gate, then verifies a live native final boss in `FinalBossMap`.
- Every stage verifies retained Mage spell ranks, all four tome slots and (with maxed startup preset) 121 spent talent points. Probe-only survivability refills player health, and native level-up selections are advanced without keyboard/mouse input. Native boss flags/health/height and transition state are appended to `reports/adventure-smoke.log`. The runner self-exits on PASS, failure or 240-second timeout.
- Public batch-only hooks: `AdventureBridge.Describe`, `DescribeMaps`, `SpawnStageBossForProbe`, `DefeatStageBossForProbe`, `EnterPortalForProbe`.

## Validation

- Build: `dotnet build src/AdventureFlow/AdventureFlow.csproj -c Release`: 0 errors, 0 warnings. Rebuild includes the changed abilities assembly.
- Source evidence: `scripts/megabonk-research/capture_adventure_evidence.py` generated `reports/megabonk-research/adventure-native-evidence.txt` from this installed native binary and matching IL2CPP signatures.
- Full native travel runtime probe is being run by the coordinating agent; its actual result must be read from `reports/adventure-smoke.log`, not inferred from compilation.

## Scope

Locations and final arena remain original Megabonk map assets and scene generation. This change opens their native routes; it does not replace their terrain with Azeroth maps or chain separate map families into an invented campaign.
### First runtime probe repair

- `reports/adventure-smoke.log` attempt1 reached the native Desert stage1 with tierIndex2, source Arcane Missiles rank2, Fireball rank1, four tome slots and 121 spent talent points. Every discovered map reported available/unlocked through the intended runtime getters.
- The probe then revealed a native minimap race: `MinimapCamera.OnBossSpawnerInteract` unconditionally dereferences `bossSpawner` before the minimap's first gameplay Update has populated it. Added a narrow callback guard that calls the native `TryFindBossSpawner` first and skips only the minimap arrow callback if no target exists.
- The initial probe now waits for normal test-battle `StartPlaying` readiness rather than starting the game early. Later native stages can resume the batch-only probe after generation without desktop input; a two-second wait lets their native scripts initialize.
- Rebuilt AdventureFlow and abilities: 0 errors, 0 warnings. The coordinating agent is repeating the actual native travel probe.

### Actual native progression PASS — 21:56:55 +05:00

The coordinating agent's second background run completed the native route:

1. Desert stage1: native Pharaoh1 spawned, defeated and released; its native stage portal became active and accepted interaction.
2. Desert stage2: native Pharaoh2 spawned, defeated and released; its native stage portal accepted interaction.
3. Desert stage3: native final-arena gate accepted interaction and invoked `MapController.LoadFinalStage`.
4. Native final arena: live native Pharaoh3 observed with 6,133,366.5 health. Mage remained selected; Arcane Missiles rank2, Fireball rank1 and all existing starters survived; all four tome slots and 121 spent talent points remained.

The same native PlayerInventory pointer (`1798615239648`) was retained across every transition. `reports/adventure-smoke.log` ends with PASS. The process exited itself; no visible desktop window, keyboard or mouse was used.

The successful probe's printed `stage bosses killed=5` counted repeated calls while two bosses were still in their native death animations. Actual unique stage bosses were Pharaoh1 and Pharaoh2; the live final boss was Pharaoh3. Future probe bookkeeping now issues Kill only once per unique stage/pointer/id. This affects only batch-only probe bookkeeping. The callback logger now reports only an actual transition from a tracked summoned boss to the last released boss, avoiding duplicate messages for unrelated enemy releases. Snapshot diagnostics additionally include the active Unity scene name. Rebuild after these diagnostic corrections: 0 errors, 0 warnings; no additional gameplay change was made.
