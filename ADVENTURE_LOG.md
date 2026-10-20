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