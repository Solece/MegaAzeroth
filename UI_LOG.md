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


## 2026-10-20 — single HUD, book slots and readable Mage frame (0.3.0)
- Inspected the user's screenshot showing the native red HP bar, weapon/tome grid and gold/timer behind the Azeroth overlay.
- Added a reversible native Graphic mask scoped to UiManager.hud and exact generated native types/field references: InventoryHud.weaponParent/tomeParent, HealthUI player HUD, XpBarUI, GameTimer and KillsAndGoldCounter.t_gold. Native scripts remain enabled and continue tracking actual game state.
- The mask never disables UiManager, its hud root, shared parents, pause/levelup/encounter widgets, objectives, arrows, alerts or cinematics. It records previous Graphic.enabled state, enforces visibility after native updates, and restores original state when disabled, outside the run, on errors or destruction. Kills, silver and chest price are preserved.
- Enlarged the Azeroth player frame 1.5x; hero name 24px, HP 20px, visible XP value/progress, level 20px; one XP display instead of an additional bottom XP bar.
- Exactly four clearly labelled book slots (Книжки) remain visible including empty slots. Native weapon inventory cells are masked. Mage ability cells appear only for actually learned spell IDs; capacity 16, absent spells do not create a visible empty grid. Learned abilities retain icons, ranks and cooldowns.
- Build passed against the actual interop assemblies with 0 compiler errors. Runtime selective-mask and screenshot verification pending root background TestBattle; no game windows/input launched by this task.
- Prepared DLL: reports/hud-layout-fix/AzerothHud.dll; plugin runtime deployment left to root for the combined test.

### 0.3.0 native runtime verification and 0.3.1 layout polish
- Root's integrated background TestBattle confirms NativeGraphicsHidden=51; native weaponParent has 21 Graphics with 0 shown, native tomeParent has 17 Graphics with 0 shown. The native mask executes successfully in the real scene.
- Inspected outputs/mage-ui-fixed-preview.png: the extra bottom grid was baked into UI-MainMenuBar-Human art, not live native weapon/tome cells. Removed that atlas from the actionbar background; retained original gryphon endcaps and the actual EmptySlot art for exactly four books plus actually learned spells. Plain dark/gold backing has no embedded cells.
- Root's current screenshot includes a world health bar because its capture temporarily moved a WorldSpace canvas; that capture issue is handled by root and is separate from the mask.
- Labels now use a private clone of the game's TMP material with guarded _OutlineWidth/_OutlineSoftness/_FaceDilate set to zero and underlay keywords disabled. The game's shared font/material is not modified. Private material is destroyed with this HUD.
- Added guarded reflection read of HudBridge.BarrierRemaining for active Mage: the independent Azeroth Ice Barrier pool is added to the native shield value for display. Missing property remains compatible with older ability DLLs.
- Version 0.3.1 builds with 0 compiler errors; new artwork/text material runtime capture pending combined root test. No deployment or game launch by this task.

## 2026-10-20 — source-aligned HP frame (0.4.0)
- Inspected the user's cropped malformed frame and actual source gameplay reference reports/reference/azeroth-original-hud.png (1920x1080), plus the locally extracted Interface/FrameXML/PlayerFrame.xml and original UI-TargetingFrame.png atlas (256x128).
- The atlas UV crop itself is correct: XML left=1/right=.09375/top=0/bottom=.78125 maps to mirrored 232x100 source pixels, uGUI UV=(1,.21875,-.90625,.78125). The previous independently guessed widget coordinates did not align with that crop, placing HP and text across the metal rails and drawing an oversized black background.
- All player-frame elements now share one PlayerRect source coordinate transform. The source rects are HP=(106,41,119,12), second row=(106,52,119,12), backdrop=(106,22,119,42), original frame=(0,0,232,100). Native current/max HP fills the first row with centred readable numbers; the name is centred in the name plaque above it.
- The second source row is explicitly labelled Shield with blue fill and independent number, including the Mage Ice Barrier pool from HudBridge.BarrierRemaining when exposed. The hybrid has no native mana pool; no fabricated mana value is shown. Its fill is displayed relative to max HP, clamped to the row width; its actual total remains visible numerically.
- XP is now a separate labelled purple bar below the frame, so it no longer occupies the source mana/shield row. It reads actual native level progress.
- The unrelated native Megabonk portrait is replaced for active Mage with original Azeroth Fireball icon inside the portrait ring as a temporary class illustration. Actual rendered Mage portrait remains a separate missing asset; source screenshot fidelity is not claimed for this temporary icon or the shield/XP adaptation.
- Build passed with 0 compiler errors. Runtime alignment screenshot test is pending root's background TestBattle. Source and reports/hp-frame-fix/AzerothHud.dll saved; no plugin deployment, game launch or desktop input by this task.
- Added optional live portrait contract: reflection lookup AzerothMegabonk.MageVisuals.MageVisualBridge.PortraitTexture. If it exposes a live UnityEngine.Texture, active Mage shows that actual model head at the exact source portrait rect (42,12,64,64). Otherwise the original Fireball class icon remains an inscribed 44x44 fallback. The external texture is borrowed and never destroyed by the HUD; no portrait rendering or visible-window use is performed by this plugin.

### 0.4.1 — local-player feet HP duplicate
- Root's combined 0.4.0 scene test passed: original Mage head portrait, actual ~68/130 green HP, shield ~81 and two learned spell cells/four book cells. Inspected outputs/mage-hp-and-model-preview.png; the remaining red HP bar at the character feet is outside UiManager.hud.
- The native HealthUI type is specifically bound to PlayerHealth and OnPlayerInventoryInit; its followPlayer instance follows the local player. Extended the existing reversible Graphic mask only to active HealthUI.followPlayer branches outside the main HUD. Enemy/boss bar types and unrelated world canvases are not queried or disabled.
- Native scripts, transforms, HP values and damage logic are untouched; previous Graphic.enabled states are restored when leaving the custom HUD. HudDiagnostics.PlayerWorldHealthGraphicsHidden exposes the additional count for the next real-scene assertion.
- Corrected blast_wave icon to the verified catalog value Spell_Holy_Excorcism_02. Raised HP text from 19 to 20 without moving the source-aligned frame geometry.
- 0.4.1 build passes with zero compiler errors; the added feet-bar mask is pending the root background runtime check. Prepared reports/hp-frame-fix/AzerothHud.dll; no plugin deployment, game launch or input from this task.
