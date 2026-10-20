# Mage selection UI fix log

2026-10-20 (Asia/Yekaterinburg). User reported crash and could not find Mage. Scope: an explicit Mage selector for the ordinary pre-run menu. Crash reproduction and automatic battle launcher are owned by root.

## Route and evidence
- Followed Universal Modder local mod-any-game, mashup-mods, Unity reference, and read MODLOG/UI_LOG.
- Real Il2CppDumper dump: global MainMenu fields tabMenu/tabCharacters/tabMaps/settings/credits/quests/leaderboardsFull/blockRaycastOverlay; CharacterMenu/CharacterInfoUI are native menu types.
- Read actual interop UnityEngine.Events.UnityAction implicit conversion from System.Action and UnityEvent.AddListener/RemoveListener. Native GameObject supports Il2Cpp type-array constructor; uGUI and TMP DLLs are already project references.
- Route: native Canvas/uGUI/TMP, rendering from an existing native TMP font/material, created only in Update when a native MainMenu/font exists. No IMGUI, new assets, custom shader/primitive VFX, texture decoding, OS input, visible windows, or game launches by this worker.

## Implemented source
- Added src/AzerothAbilities/MageSelection.cs: top-center compact pre-run panel with title `Azeroth: Маг`, explicit Mage/native selection buttons, state `Маг выбран — Огненный шар`, and a notice that current Megabonk hero appearance stays unchanged.
- Panel shown only in main/character/map tabs before MyPlayer.hasStarted; hides in native blocking windows and combat. Background/text do not receive raycasts; only own two buttons receive input. Native tome cards and inventories are untouched.
- Explicit Mage button uses existing HudBridge.SelectClass("mage"); native button uses "none". No new class content; no game save methods invoked. Existing BepInEx mod config selection persistence remains the previous intended behavior.
- Added only MageSelectionBehaviour registration and ability plugin version 0.1.1 in Plugin.cs, as approved by root. No deployment.
- Delegate wrappers retained for component lifetime, removed on destroy. Managed UI failures disable only the selector and are logged; native menu remains.

## Verification status
- Source inspection completed; compile and runtime status pending below.
- Exact visual placement, Cyrillic glyph coverage, click behavior, and scene transitions require real game evidence. A successful build is not runtime verification. Do not claim this selector fixed the unrelated crash.

## Compile checkpoint
- .NET6 Release build succeeded: 0 warnings, 0 errors. Full log: reports/mage-selection-fix/build.log.
- Checkpoint SHA256 metadata: reports/mage-selection-fix/source-verification.json. Other workers still have active ability/icon and root visual edits; final combined build/hash/deployment belongs to root.
- Root reports actual crash stack in the separate HUD GUI.DrawTexture path; this UI intentionally contains no GUI/GUIStyle/OnGUI or DrawTexture calls. HUD replacement is owned by the HUD worker.
- No game launched or deployed by this worker. Exact runtime visual/input verification remains pending.

- Final runtime smoke: native Mage selection panel visible with mage state; all5 raw icons uploaded64x64; native Desert auto-start with mage; final HUD/GC/render smoke clean. Generic GetComponentInChildren failure was observed in attempt1 and fixed by native Resources lookup.

