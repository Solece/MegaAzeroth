# Mage visual transfer log

2026-10-20, Asia/Yekaterinburg

- Source is the installed AzerothSurvivors MPQ overlay, read-only. The Mage visual is a composed playable character, not a standalone NPC model.
- Verified character: Human male, race 1/class 8/sex 0, skin/face/hair/facial-hair appearance 0. Original Character/Human/Male/HumanMale.m2 (MD20 v256).
- Original CharStartOutfit row 10 links Mage robe item 56 to ItemDisplayInfo 12647; shirt 6096/display 2163, trousers 1395/display 9924, boots 55/display 9929. The original source robe, body, face and equipment textures are composited in the Vanilla texture regions. Weapons are intentionally excluded from the character pack.
- Only selected original geosets are exported: 0, 101, 201, 301, 401, 802, 1302. Robe sleeves and skirt use the source ItemDisplayInfo geoset groups. Export has 745 vertices and 840 triangles.
- Exported original clips: Stand 0, Walk 4, Run 5, JumpStart 37, Jump 38, JumpEnd 39, ReadySpellDirected 51, ReadySpellOmni 52, SpellCastDirected 53, SpellCastOmni 54, ChannelCastDirected 124, Death 1. Names were checked against installed AnimationData.dbc. Vertex frames are baked at 24 fps; source timing is preserved.
- Original M2 spell-hand attachment IDs 21 and 22 are transformed by source animated bone matrices and baked with every frame, in sibling .attachments.bin files. Runtime exposes HandWorldPositions for original cast VFX placement.
- Found and corrected a Pillow BLP2 paletted-alpha limitation: PIL._BLPBaseDecoder._read_bgra uses palette alpha and skips the separate pixel alpha plane. Several source robe textures therefore decoded fully transparent. scripts/azeroth-mage/blp_decode.py reads palette RGB and the independent 1/4/8-bit alpha planes. Preview after correction shows the complete indigo robe; exported composed-body alpha is 255 throughout.
- private-assets/azeroth-mage/human-male/validation.json reports PASS: index bounds, finite geometry/UV/animation values, exact byte counts, original item/display linkage, 12 animation clips, finite original hand attachments, opaque complete texture. Software-rendered preview was inspected at private-assets/azeroth-mage/human-male/preview.png.
- Runtime project src/MageVisuals builds Release/net6.0 with 0 errors and 0 warnings. Output AzerothMegabonk.MageVisuals.dll. Uses pinned RGBA32 upload, no ImageConversion.LoadImage, no IMGUI.
- Runtime attaches only a visual child to PlayerMovement physics root, aligned by native CapsuleCollider intrinsic height/center. It retains native movement, camera, HP and colliders. Original player/hat/weapon renderers are hidden while Azeroth Mage is active and restored on deactivation/unload. Children named Azeroth* are exempt so new spell VFX remain visible.
- Animation state reads native velocity/grounded and HudBridge.CastCount. Idle casts use original directed cast; movement keeps original run; native airborne state uses original Jump. The full source pack also contains omni/channel/start/end/death clips for later state integration.
- Integration is owned by parent agent. This worker did not launch a game or use desktop input. The built DLL/assets need parent deployment and runtime evidence before declaring the visual operational.
- Appearance limit: the installed character profile is RSE1 encoded; no claim is made that appearance 0 matches an unidentified previously selected saved Azeroth character. This is the verified original Human male Mage starter outfit, not every possible race/customization/armor set.

Source schema references:
- https://github.com/wowdev/WoWDBDefs/blob/master/definitions/CharSections.dbd
- https://github.com/wowdev/WoWDBDefs/blob/master/definitions/CharStartOutfit.dbd
- https://github.com/wowdev/WoWDBDefs/blob/master/definitions/ItemDisplayInfo.dbd
- https://wowmodelviewer.github.io/wowmodelviewer/WoWItem_8cpp_source.html

- Optional Polymorph visual pack exported at private-assets/azeroth-mage/sheep: installed CreatureDisplayInfo 856 links model 140 with Sheep skin. 222 vertices, 230 triangles, 48 bones; original idle/walk/run/death clips and raw RGBA texture. No runtime Polymorph behavior is claimed by this asset worker. Reproduce with scripts/azeroth-mage/build_sheep.py.

## Companion and Polymorph follow-up

- Verified installed DBC linkage: CreatureDisplayInfo 9517 does not exist. CreatureModelData 110 is Creature/WaterElemental/WaterElemental.mdx, and CreatureDisplayInfo 110 references model 110 with WaterElementalSkin. The native pet row [9517,110] can therefore use the independently verified 110 visual; interpreting 9517 as a display ID would be incorrect.
- Exported private-assets/azeroth-mage/waterelemental: 593 vertices, 623 triangles, 124 original bones; seven source clips (Stand0, Walk4, Run5, Attack16, SpellCast32, Spawn127, Death1). Source timing preserved; cast/spawn are nonlooping. All 13 original texture definitions are retained as corrected BLP-decoded PNG/RGBA plus hash/archive metadata in validation.json/source-textures.
- Offline textured WaterElemental preview was inspected and matches the original elemental body. Structural validation passes finite values, exact animation lengths, indices and RGBA dimensions for both WaterElemental and Sheep.
- Runtime addition is a separate src/MageVisuals/MageCompanionVisual.cs. CreatureVisualBridge exposes Create(key, position, height), SetPosition, SetFacing, SetClip, Destroy and Shutdown. It uses original packs with no collider or AI creation. Water surfaces preserve source alpha with transparent Standard material.
- Reactive Sheep tracker reads AzerothAbilities.MageSpellBehaviors.IsSheep(Enemy). It replaces only the rendered visual, tracks native pooled enemy ID, disables the prior AzerothCreature child object so the existing EnemyVisuals LateUpdate cannot re-enable it, then restores prior active/renderer states when Polymorph ends. Native physics and AI remain owned by the existing game/ability adapter.
- Plugin.cs was intentionally not modified during this follow-up. Parent must call CreatureVisualBridge.Initialize() during plugin load and Shutdown() during unload. Release build succeeds without errors/warnings; integration/runtime execution remains parent's responsibility.
- Fidelity limit: companion mesh export currently uses first diffuse pass in a single atlas. WaterElemental's extra reflection layers, UV scrolling and embedded M2 particle emitters are retained in source inventory, but are not recreated by this visual bridge.
