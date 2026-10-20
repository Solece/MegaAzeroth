# Complete Mage spell catalog and behavior

Date: 2026-10-20. Scope: retain original Azeroth Mage spells and upgrade values while adapting to Megabonk movement, collision, enemy actors and books. All operations in this agent were background file reads, source edits, console builds and source validation. No game process, window or input was started.

## Original source

Installed `AzerothSurvivors.exe` SHA256 `2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a`.

The native Mage class list is exactly 13 Rust string slices at raw `0x6b86348`, followed by Warlock. It associates Fireball, Frostbolt, Arcane Missiles, Frost Nova, Arcane Explosion, Blizzard, Flamestrike, Fire Blast, Cone of Cold, Blast Wave, Pyroblast, Ice Barrier, Polymorph. Their ability rows are raw `0x6b899a0` through `0x6b89f40`, stride `0x78`.

Every original damage, cooldown, range, radius, targets, duration, ticks, projectile speed, status kind/value/duration, cast kind and allowed-upgrade slice is retained. `mage.json` now includes all 13 spells. Original native cast-kind damage multipliers and global float1.4 scales remain.

## Runtime adapter

- Existing projectile, instant and self-AoE paths remain in Plugin.cs. Root integrates the separate MageSpellBehaviors hooks and original spell visual renderer.
- Ground AoE uses a dense nearby group among at most the original24 target candidates. Duration is `baseDuration*1.4*durationFactor`; total ticks are `round(duration/baseDuration*sourceTicks)`, first hit after0.25sec, subsequent ticks at `duration/totalTicks`. Each tick damages for the scaled source damage divided by the source tick count. Duration upgrades add damage ticks rather than reducing damage per tick. Native evidence `14596105F..1459611FE`.
- Cone of Cold uses the original dot-product threshold0.5, a120-degree total cone, and source range multiplied by the radius upgrade. Evidence `145982B12..B66`.
- Frostbolt, Cone of Cold, Blast Wave and Blizzard retain their original slow values/durations. Frost Nova roots enemies for the original3sec, bosses for one quarter. Slow is a postfix multiplier on native EnemyMovementRb.GetSpeed; it does not rewrite base enemy speed or replace the native spawner.
- Ice Barrier retains source45% max-health share,1.4 balance scale and square-root rank-damage scaling. Source shield duration15*1.4=21sec. Its temporary absorption pool remains separate from native book shield capacity. Native damage entry-point prefix consumes that pool; exact original armor/evade ordering differs between engines and needs gameplay verification.
- Polymorph is the original passive on-spell-hit proc, not an automatic damaging cast. Source12% chance, at most3 sheep,10sec lifetime,3sec cooldown floor before book haste. Bosses are immune. The next hit pops a sheep for15% maxHP, capped by the source115*1.4 rank-scaled damage, within the source7yd radius. Native evidence `14594AF80..B3EA` and `14593B7D7..BA70`; full original321-byte description at raw `0x7185321`.
- Sheep controls adapt harmless wandering through the native movement hook. Contact and new special attacks from affected enemies are suppressed. The exact source wandering algorithm, native guardian taxonomy, already-launched enemy projectiles and sheep appearance require integration/gameplay validation. Public `MageSpellBehaviors.IsSheep(Enemy)` allows the model renderer to show original sheep art.
- Original VFX callbacks use `VisualEvent(id,stage,position,scale,duration)`: ground areas use `area`, barrier uses `aura`, cone uses `cast`, sheep aura uses `aura`, sheep burst uses `impact`. Root owns SpellVfx and lifetime handling.

## Upgrades

Original damage/cooldown/targets/radius formulas remain unchanged. Added kind4 duration rarity values `[.15,.22,.30,.40,.60]`, diminishing as `max(0,(6-durationFactor)/5)` and applying additive durationFactor. All upgrades still grant the original diminishing hidden rank-damage bonus. Blizzard and Flamestrike expose duration upgrades exactly as the original allowed-kind slice does. Native duration helpers `145897911..937`, apply `14595AC3B..49`.

## Validation

Standalone source checks passed65 assertions: original executable hash, all13 class associations, all13 numeric/status/cast-kind records bit-for-bit, all13 allowed-upgrade slices, all5 upgrade-rarity tables, existing Fireball common rank2 result86.76304, duration upgrade/cap, original Polymorph constants and cone/area constants. Report: `reports/megabonk-research/mage-complete-source-checks.txt`. This proves source matching and formula checks, not gameplay success.

Root owns Plugin.cs hook integration, build/deployment and background native runtime tests. Source files are frozen at this checkpoint except fixes warranted by compilation or native test evidence.

## Remaining class-wide work

The13-spell block is the complete spell catalog, not the entire original Mage class. Original Mage has39 talents in3 trees of13 nodes: Arcane (`0x7175720..5e60`), Fire (`0x7176498..6bb8`), Frost (`0x71771b0..7890`). Talents include source starter replacements, Clearcasting, missile echoes, Ignite, Hot Streak, on-kill Fire explosions, Frostbite, Splitting Ice and Water Elemental. The root assigned their separate implementation to mage_talents_port. Racial passives, all original crit/proc ordering and original offer/card rarity distribution are still separate integration requirements. Never report a complete original class merely because13 spell rows load.
`n2026-10-20 follow-up: recovered exact native Water Elemental companion attack. Prior tentative0.55 damage /50 range interpretation disproved: actual baseDamage50, fixed range28, direct interval2.2, source spell116 speed28, slow30percent for2sec. Damage includes12percent per source player level after the first, global damage and PetDamage effect17; no player Frostbolt rank or1.4 base scale. Full proof saved reports/megabonk-research/water-elemental-source-proof.md and water-elemental-combat-disassembly.txt. Runtime implementation delegated to root/talent agent. No game process or input used.
