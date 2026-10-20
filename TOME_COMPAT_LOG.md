# Tome compatibility and combat pause repair

## 2026-10-21: source audit and implementation

Baseline: local checkout `4f72484`; Megabonk 1.0.69, Unity IL2CPP 2023.2.22f1.
No game launch or deployment was performed by this worker. Runtime verification is delegated to the root test runner.

### Native evidence

The installed `GameAssembly.dll` and its local Il2CppDumper signatures were inspected. Full disassembly remains in ignored `reports/megabonk-research/tome-compat/`.

- `WeaponUtility.GetAttackQuantity`, RVA `0x4346F0`: truncates the player `EStat.Projectiles` (16) toward zero, adds source/weapon quantity, clamps the final result to at least one. The old adapter used `RoundToInt` and launched overlapping projectiles from exactly the same position.
- `GetAttackSizeMultiplier` `0x4347A0`, `GetDuration` `0x435630` and `GetProjectileSpeed` `0x4359B0`: multiply the source/weapon base by the respective player stat (9, 10, 11).
- `GetCritChance` `0x434950`, `GetCritDamageMultiplier` `0x4349D0`, `DamageUtility.GetCritDamageMultiplier` `0x4702D0`: native critical chance/damage are separate from global damage. The shared ability adapter now performs the one native critical roll; the former MageTalents critical roll was removed to avoid a double roll/multiplier.
- `WeaponUtility.GetProjectileBounces` `0x435930`: adds integer-truncated player bounce bonus (45).
- `PlayerStatsNew.GetBaseValue` `0x445920` jump table was decoded: Projectiles and ProjectileBounces base0; DamageMultiplier/AttackSpeed/SizeMultiplier/DurationMultiplier/ProjectileSpeedMultiplier/CritDamage/KnockbackMultiplier base1; CritChance base0.01. `EffectDurationMultiplier` has base0 and is not the Duration tome's multiplier.
- `Enemy.DamageFromPlayerOther` `0x4AC650` enters native `Enemy.Damage` `0x4AC7B0`; the native path already applies elite damage and emits damage events. No duplicate elite multiplier or manual lifesteal heal was added. `PlayerHealth.OnEnemyDamaged` `0x429320` reads Lifesteal directly and does not require a positive projectile proc coefficient.
- `MyTime` (`Assets.Scripts.Utility`) exposes `paused`, `time`, `deltaTime`, `timeScale`. Native `PauseUi.OnEnable` `0x556C80` calls `MyTime.Pause`; `EncounterWindows.PopReward` `0x3E06D0` sets encounterInProgress and native pause. `RewardFinished` `0x3E09D0` clears/advances the encounter before native unpause. Unity `Time.time` alone is therefore the wrong combat clock for this game.

### Adapter behavior

| Native tome/stat | Source ability effect |
| --- | --- |
| Quantity / Projectiles | Integer native player bonus adds damaging projectiles/instant targets. Each projectile has its own simulation entry, visible launch offset and a short stagger. These are real extra hits, not decorative duplicates. Source AoE/cone/barrier/passive spells do not duplicate their whole area on this stat. |
| Damage / DamageMultiplier | Multiplies source base, source rank upgrades and talent modifiers once. Native hit processing remains responsible for elite/armor effects. |
| Cooldown / AttackSpeed | Divides source cooldown once; preserves the source cooldown floor and source upgrade rules. |
| Size / SizeMultiplier | Multiplies AoE/cone radius, projectile hit radius and its source VFX size. |
| ProjectileSpeed / ProjectileSpeedMultiplier | Multiplies hero and pet projectile movement speed once, including explicit source speed overrides. |
| Duration / DurationMultiplier | Extends source ground areas, barrier and source control-effect durations; extends projectile travel budget. Tick damage remains the source per-tick value, so a longer ground area genuinely continues ticking. |
| Precision / CritChance | Uses the same native critical-roll function and CritDamage stat for source hits; talents see that single result. |
| Bounce / ProjectileBounces | Adds actual homing ricochets to distinct living native enemies within the remaining projectile travel budget. This is the explicit cross-engine adapter behavior. |
| Blood / Lifesteal | Remains native via the damage event; no extra mod-side healing is applied. |
| Health, regeneration, armor, shield, thorns, evasion, movement, attraction, luck, XP, currencies, curse/enemy stats and mixed tomes | Native PlayerStats/player/world systems remain in charge. Mixed tomes contribute through their real native stat keys rather than through hardcoded tome names. |

Compatibility limit: source Mage hits currently have base knockback0. A native KnockbackMultiplier multiplies that zero and does not create a new impulse. No arbitrary source knockback was fabricated. The source AoE/barrier/passive target rules also remain intact; Quantity applies to abilities which actually launch projectiles or choose finite instant targets.

### Pause contract

`CombatSimulation` is shared by AzerothAbilities and MageTalents. It advances once per Unity frame using native `MyTime.deltaTime` only when the run is playing and none of the native pause/encounter/levelup/inventory/cutscene gates are active. An explicit Unity/native zero time scale also blocks it.

- Source projectile movement, launch staggers, cooldowns, ground DoT ticks, status expiration and barrier lifetime all use this clock.
- Source M2 particle/geometry/ribbon effects use this same clock and skip rendering updates while simulation is paused.
- Talent rate limits, ignite ticks, free-cast scheduling and Water Elemental movement/casts use the same contract. UI refresh and input still use the UI clock.
- All missile VFX layers are now parented under one moving root per damaging projectile. Previously only the first layer followed the projectile.
- HudBridge cooldown remaining and descriptions report current source ranks plus effective native book/talent values.

### Validation checkpoint

Release compilation of the isolated E: source copies, including MageTalents -> AzerothAbilities project reference, passes with 0 warnings and 0 errors. This proves compilation only. Actual native tome grants, visible flights, pause/resume and damage events still require the root background probe before the repair is called runtime-verified.

## 2026-10-21: feature run 49604 evidence and probe fixture repair

The first actual isolated feature run recorded 18 PASS, 3 FAIL, 0 SKIP under `private-assets/elwynn-v2-test-runtime/reports/feature-49604`. Runtime evidence confirms real native Quantity upgrades produce extra source projectiles, hits and native HP loss. Damage, projectile speed, cooldown intervals, size, duration ticks and native critical hits also passed. Native levelup freezes source flight, cooldown/status/area clocks and VFX ages; native source animation, ownership/pooled spawn, Luck/card consistency, four-book inventory, XP and native result values passed. Failed fixture cases below are not reported as production passes.

- Installed `native-tome-catalog.json` has 26 actual TomeData definitions and no `ProjectileBounces` row. ETome.Bounce=20 exists only in the enum in this installed build. The feature test now searches real loaded content by stat and explicitly skips ricochet-book validation when the tome is absent. No fake alias or fabricated tome was added. The generic native ProjectileBounces adapter remains supported.
- Native pause admission rejects an active encounter or the one-frame EncounterWindows.closedEncounterThisFrame guard (`HasEncounter` RVA0x3DFCB0, `LateUpdate` RVA0x3DFD90). Recovery no longer invokes an inactive LevelupScreen. Pause opening now retries only real PauseUi.Pause after native CanPause permits it, and saves full native admission evidence and phase-specific timeout details. The production simulation gate was not weakened.
- Direct AddEncounter(ShadyGuy) was an invalid fixture: native UpgradePicker.ShuffleUpgrades dereferences InteractableShadyGuy.currentlyInteracting and its native item/price lists. The test now uses a real loaded merchant (or a real loaded native merchant prefab), native FindItems initialization and native Interact, which establishes its own context before queuing the encounter. The native Interact returns false on successful opening, so success is checked through its actual context and rendered native item cards instead. No items/prices/static flags are fabricated.
- Native source tooltip capture now waits 0.65 realtime seconds across Unity frames after real ToolTipObject hover admission. The previous same-frame screenshot preceded the native hover delay.

Only FeatureProbe.cs changed for this fixture repair. No runtime DLL deployment, process launch, native gameplay patch or production simulation change was performed. E: isolated Release build passed with 0 warnings/errors before the final tooltip wait adjustment; a subsequent final compile is recorded by the root runner. All repaired runtime cases still require the next actual root background run.
Final isolated E: Release compilation, including the 0.65-second native tooltip wait, completed successfully with 0 warnings and 0 errors. The source patch is now applied in D:; runtime deployment remains owned by the root runner.
