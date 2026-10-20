# Root background-probe contract

Call all APIs on the Unity thread. Mutating methods require Application.isBatchMode, AZEROTH_LAB_HEADLESS_TEST=1 and active Mage. Normal game input/windows are not needed.

Type: `AzerothAbilities.TomeCompatibilityDiagnostics`

- `SnapshotJson` includes native stats, true projectile positions/speed/range/size/launch delay, damaging/visible projectile counts, actual hit/critical events, spell cooldown remaining, ground-area ticks/status lifetimes and VFX ages.
- `NativeTomeCatalogJson` dumps loaded native TomeData stat keys, modifier types/values and actual offers for rarity0..5. Save this as evidence.
- `SetAutomaticCastingForProbe(false)` stops automatic source casts for controlled comparisons. It does not stop already launched projectiles or manual probe casts.
- `UseIsolatedTomeForProbe(string id,int levels=1,int rarity=1)` temporarily replaces the run's tome inventory and invokes the real native AddTome/GetUpgradeOffer/PlayerStats update route. Use `levels=0` for no-tome baseline. Other native item/character stats remain present. `RestoreOriginalTomesForProbe()` restores the original inventory and recalculates native stats.
- `GrantTomeForProbe(string id,int rarity=1,int levels=1)` grants actual native upgrades in the current inventory, respecting four available slots.
- `CastSpellForProbe(string id)` invokes the real source cast path. Capture before/after CreatedProjectiles and then advance simulation to observe actual hits; do not infer hits merely from VFX count.
- `LaunchSlowProjectileForProbe(string id="fireball",float speed=.25f)` makes one live source projectile slow enough for opening each native UI window. Native projectile-speed modifiers still apply.
- `ClearProjectilesForProbe()` disposes only source projectile instances between comparisons.

Suggested tests:

1. Snapshot no-tome baseline. Grant Quantity using real offers until native Projectiles crosses an integer. Cast Fireball/Arcane Missiles before/after; compare CreatedProjectiles and actual hit events. Fractional bonuses below the integer boundary intentionally do not add a projectile, matching native truncation.
2. Isolate Damage, Cooldown, Size, ProjectileSpeed, Duration, Precision and Bounce in turn. Compare the matching native stat and actual source result. Duration: use an existing ground-area spell; inspect Remaining ticks and continued hits. Precision: enough casts may be needed because the real native roll is probabilistic. Bounce: provide at least two native enemies, inspect ProjectileBounces and hit IDs.
3. LaunchSlowProjectile, record snapshot, call native PauseUi.Pause(); wait several Unity frames; snapshot must keep projectile coordinates/range budget, source CombatTime/casts, cooldown/status remaining and VFX ages unchanged. Resume through the native UI and assert movement continues.
4. Repeat the same flight test for LevelupScreen and native encounter item/shop selection, using the native EncounterWindows reward queue so encounterInProgress/MyTime.paused are exercised. Compare against a snapshot after the window opens. The UI clock may advance while source combat remains frozen.
5. RestoreOriginalTomesForProbe and SetAutomaticCastingForProbe(true) before leaving the test. Root must retain its test-only save guard.
