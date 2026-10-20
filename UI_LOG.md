# Azeroth UI investigation log

2026-10-20 (Asia/Yekaterinburg). Assigned UI, now also original skill/progression data research. Requirements: Megabonk movement and spawn systems; original Azeroth UI, monsters, hero abilities and their upgrades; native Megabonk tomes with all four tome slots unlocked immediately. Actual slot unlock is owned by another agent.

Sources: original native install D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors; original site https://wow.aethus.pl/; reconstruction OutThisLife/azeroth-survivors-re is explicitly not an authoritative representation of this installed binary.

Read Universal Modder AGENTS, mod-any-game skill and Unity playbook. This task owns scripts/azeroth-ui, src/AzerothHud, private-assets/azeroth-ui, reports/azeroth-ui and this log. Do not modify original games or launch games; root handles runtime.

Initial MPQ listing has no listfiles in all seven archives. This means names must be supplied externally; it does not mean archives are empty. Network urllib official site read failed locally (sandbox), then elevated request reached server but received HTTP 403. web tool can read the official landing text; no native UI layout from it yet. Need exact source data before recreating skill formulas.

UI prototype source written src/AzerothHud. Compile-only verification .NET6 succeeds (0 warnings/errors) against lab BepInEx interop. Build initially hit SDK NuGet cache permissions; used lab-independent E:/workspace scratch cache, then fixed Unity/Core NullableAttribute issue by disabling nullable and avoiding record generation. Does not claim runtime verified. Native bindings: MyPlayer.Instance.inventory.playerHealth hp/maxHp/shield; playerXp.GetXpInt()/level; XPUtility.CurrentLevelProgress; goldInt; TomeInventory.GetTomeLevel; DataManager.GetAllTomes icons; GameManager.GetAliveTime. No invented HUD values. Four book outlines drawn; actual unlock by separate plugin. Native encounter screens remain until replacement verified. Azeroth ability bridge uses reflection with exact arrays (coordinated with abilities agent).

Original EXE English JSON extracted: offset 0x6b9bc92, 170926 bytes, 2632 keys; source SHA256 2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a. Source manifest and JSON in reports/azeroth-ui. Extracted embedded Azeroth Lua overrides confirm stock WoW FrameXML use, custom talent window, action tooltip anchors next to action button; no generic fake UI accepted as exact.

Original assets extraction initially exposed mpyq uncompressed multi-sector bug: missing header bytes and one massive invalid Spell.dbc. Correct Archives.read from assets agent handles no compression directly. Invalid intermediate Spell.dbc deleted by exact path. Verified PNGs and BLP signatures BLP2 now in private-assets/azeroth-ui/verified; original FRIZQT font signature validated. One later escalation failed because automatic approval review endpoint returned 403; action was not executed, review infrastructure failure, not unsafe determination. Work on workspace source continued independently.

Root reports original visible version v0.102.3 (f78e6fab); screenshot pending. Abilities agent subsequently found original native numeric table (Fireball row0x6b899a0 stride0x78), owns decode and runtime. Do not infer exact native formulas from reconstructed OutThisLife source.

Real original HUD screenshot inspected: reports/reference/azeroth-original-hud.png. Updated HUD to original PlayerFrame texture texcoords and original mainbar 4-quadrant stone textures with two gryphon caps, original empty slots/icons. Genuine native HP, level, XP, gold/time; four real tome icons/levels. Ability reflection bridge with exact managed arrays + Active/ClassId. Build 0 warnings/errors after actual reference update. Native encounter windows preserved; UI fidelity limited portrait/native minimap/font and abilities picker owned companion. Staged local-only plugin at private-assets/azeroth-ui/plugin; root deploys and checks runtime. FrameXML extraction retry succeeded after transient auto-review infrastructure 403. Source evidence in reports/azeroth-ui/abilities-source.md.

Final headless guard fixes: tome cells require HasTome before reading rank; class bridge rejects empty string. Rebuilt 0 warnings/errors, source and staged DLL synchronized. User is working on PC: no launches, no UI/input/foreground actions by this agent. Runtime visual verification remains explicitly pending. Native UI left enabled as fallback until user permits a visual test.

2026-10-20 headless repair requested by root: runtime PNG upload in another plugin exposed ObjectCollectedException via Il2CppSystem.Span constructor. HUD v0.1.1 removes ImageConversion.LoadImage and its assembly reference completely. prepare-raw-ui.py preconverts original PNGs to bottom-up RGBA32 plus ASCII width/height .size sidecars; keeps PNG originals, writes size/hash manifest. HUD validates exact width*height*4 byte count, pins managed pixels using GCHandleType.Pinned, calls Texture2D.LoadRawTextureData(IntPtr,int) then Apply, releases handle in finally. GPU object creation remains deferred inside OnGUI/Tex; Load and Update do not upload image assets. ReadBridge, ReadStatic and drawing helpers have HideFromIl2Cpp; former System.Object local function removed to avoid injection warnings. Build passes net6, 0 warnings/errors. Stage source/DLL/assets updated, no processes or foreground windows touched. Root deploys and verifies headless/native run. Native HUD still preserved.

## 2026-10-20 — native HUD crash repair (0.2.0)
- Evidence: BepInEx/ErrorLog.log fatal System.AccessViolationException in il2cpp_gchandle_get_target -> Internal_DrawTextureArguments.set_texture -> GUI.DrawTextureWithTexCoords -> HudBehaviour.OnGUI on ordinary graphical launch.
- Removed OnGUI, all GUI/GUIStyle draw calls, and the IMGUIModule assembly reference. The failing texture-argument path is no longer used.
- Rebuilt the Azeroth artwork overlay as native Unity uGUI Canvas + RawImage + Text, including HP, actual XP, timer, gold, native tome icons/ranks, original Mage spell icons/ranks/cooldowns. Four tome cells remain visible. RawImage.uvRect preserves source atlas crops/mirrors.
- Texture data still comes from validated original bottom-up RGBA assets and pinned native uploads; retained native components/textures/font are owned by the HUD instance. The passive overlay has no raycast targets or GraphicRaycaster.
- Version 0.2.0 built successfully against the actual generated Unity 2023.2.22f1 interop DLLs, 0 compiler errors. NuGet audit warning NU1900 reflects restricted network during the local cached restore, not a compiler or runtime failure.
- Managed helpers taking custom UiImage/UiText classes, collections or System.Type are marked HideFromIl2Cpp; only Update/OnDestroy are Unity message methods.
- Runtime render/GC verification is pending integration by the root agent; no desktop input or game window launched by this repair task.

### Native render follow-up — 0.2.1
- The integrated batch smoke exercised the new Canvas path and found Font.CreateDynamicFontFromOSFont(String,Int32) is stripped in this game's native build (NotSupportedException: Method unstripping failed).
- Replaced every legacy UI.Text/Font factory with TextMeshProUGUI using a live TMP_FontAsset and shared material obtained from Resources.FindObjectsOfTypeAll<TextMeshProUGUI>(). This is the same lookup already exercised successfully by the class selector.
- If no TMP template is loaded yet, HUD construction waits for the next update; it creates no partial Canvas. Borrowed font/material are retained and never destroyed by this plugin.
- Build passes against the generated game DLLs, 0 compiler errors; runtime recheck pending root integration. No game window/input used by this task.

- Runtime verified 20.10.2026: HUD0.2.1 built59 RawImages+33TMP labels; offscreen native camera render showed Azeroth frames/action bar and original Fireball icon. Managed GC exercised, fatal ErrorLog empty,30-tick test clean exit.

