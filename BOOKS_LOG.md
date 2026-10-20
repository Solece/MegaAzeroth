# Book slot research log

2026-10-20: Read Universal Modder mod-any-game and Unity reference, Megabonk field note. Scope: four tome/book slots open at run start; no weapon slot unlock and no save edits. Exact types pending metadata/interop investigation. No game changes by this subtask.

- GitHub REST releases API blocked by shared-IP rate limit. Switch to direct official release URL.

## Confirmed native API (Megabonk 1.0.69 / build 21750826)

- Il2CppDumper v6.7.46 completed dump and DummyDll generation from original read-only GameAssembly/global-metadata. Metadata v29 / IL2CPP v29.1. The tool ends with a harmless Console.ReadKey exception after outputs complete when run without a console. Outputs are private in scripts/megabonk-research/Il2CppDumper; never redistribute.
- InventoryUtility namespace Assets.Scripts.Inventory__Items__Pickups.
- GetNumAvailableTomeSlots() RVA 0x420020 returns shop-level+2 ordinarily or 1 in a special mode; native CanUnlockTomes() compares currently owned tome count with this method's result. Therefore result means total capacity, not empty slots. Native disassembly in reports/megabonk-research/slots-native.txt.
- GetNumMaxTomeSlots() returns 4. Its native RVA 0x4203C0 is shared with GetNumMaxWeaponSlots(); DO NOT PATCH this method, as patching shared machine code can affect weapon capacity.
- Plugin patches only GetNumAvailableTomeSlots() with HarmonyPrefix returning 4 and skipping original. No saves, profile, shop or achievements written. Four slots are unlocked capacity, not four granted books. Native weapon slots remain unchanged.
- First build: src/BookSlots/bin/Release/net6.0/AzerothMegabonk.BookSlots.dll, net6.0, Release, zero warnings and zero errors. Root owns deployment and runtime verification. Not yet claimed verified in-game.
- Plugin GUID local.azerothmegabonk.bookslots. Runtime logs loaded policy and first actual capacity query; public source in src/BookSlots/Plugin.cs.
- Tools: official Il2CppDumper v6.7.46 direct release (GitHub REST was shared-IP rate-limited), capstone 5.0.9, pefile 2024.8.26 in lab .venv, ilspycmd 9.1.0.7988 in scripts/megabonk-research/tools.
- ilspycmd executable is local; use absolute path. It can decompile the generated interop wrappers to discover exact namespaces/signatures, but native bodies must be read in GameAssembly with capstone/Ghidra.

## Integration API passed to root

- Assets.Scripts.Actors.Enemies.Enemy.InitEnemy(uint id, EnemyData enemyData, Vector3 pos, int waveNumber, EEnemyFlag flag=0, bool canBeElite=true, float extraSizeMultiplier=1) postfix is a visual replacement hook, including pooled spawn reuse. Native RVA 0x4ADB20.
- Global EnemyRenderer.Set(EnemyData), native RVA 0x334E40; Enemy fields animatedMesh, renderer, enemyRenderer, rb, collider. Preserve original collision/spawn components.
- Enemy.DamageFromPlayerWeapon(DamageContainer) / DamageFromPlayerOther(DamageContainer). DamageContainer namespace Assets.Scripts.Actors; ctor(float procCoefficient,string damageSource), damage/direction/knockback/element fields.
- InventoryUtility.GetRandomUpgrades(): List<IUpgradable> offer generation hook; PlayerInventory.AddUpgrade(IUpgradable,List<StatModifier>,ERarity) is shared dispatch.
- Global UpgradePicker.SelectUpgrade(IUpgradable,List<StatModifier>,UpgradeButton,ERarity), ShuffleUpgrades(EEncounter).
- Assets.Scripts.Inventory__Items__Pickups.Weapons.WeaponBase.Disable()/Use() for native attack opt-out in the root ability integration.
- Global InventoryHud.Refresh() and tomeParent provide tome HUD integration. Per-type API extracts stored under reports/megabonk-research/*.txt.

## 2026-10-21: Mage tome compatibility and pause repair

- Source adapter now consumes native quantity by integer truncation, size, duration, projectile speed, damage, attack speed, criticals and bounce bonuses. Distinct real projectiles have separate visible launch positions/staggers; all their VFX layers follow one root.
- Critical rolls moved into the shared ability hit path, with the former MageTalents duplicate removed. Native lifesteal and elite damage are retained in native damage-event processing.
- Four-tome-slot patch remains unchanged. Source spell ranks/upgrades remain source-backed.
- Added shared combat clock and native pause gates for source abilities, VFX, talents and Water Elemental.
- Full native evidence, limits and probe contract: TOME_COMPAT_LOG.md and docs/tome-pause-probe-contract.md. Isolated Release compilation passes; runtime verification is pending the root background probe.
