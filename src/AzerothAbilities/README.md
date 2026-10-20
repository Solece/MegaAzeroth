# Azeroth Mage ability slice

Build with `dotnet build AzerothAbilities.csproj -c Release`. Deploy `AzerothAbilities.dll` and `mage.json` together in one laboratory BepInEx plugin subfolder.

Set `[Abilities] SelectedClass = mage` in `local.azerothmegabonk.abilities.cfg` for the Mage prototype, or call `AzerothAbilities.HudBridge.SelectClass("mage")` on the game thread. The default `none` leaves native weapons active. Switch back to `none` to restore native casting.

Five original source-backed spells and the original damage/cooldown/target/radius upgrade formulas are implemented. Native book cards remain native. Movement and spawn hooks are untouched. Spell visuals are procedural, and this is a cross-engine reimplementation with adapted offer frequency/rarity and book/item stat integration. It is not the complete Azeroth Mage class: frost/DoT/buffs, talent trees, racial abilities, original crit/proc rules and original character/VFX assets remain unsupported.

See `../../ABILITIES_LOG.md`, `mage.json`, and `../../reports/megabonk-research/ability-source-checks.txt` for exact source offsets, evidence, limitations and validation. Runtime/play verification remains root's responsibility; do not present successful compilation as gameplay verification. Modded runs must remain offline with leaderboard uploads disabled.
