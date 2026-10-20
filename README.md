# Azeroth × Megabonk — local prototype

Work root: D:/Games/Saitan/AzerothMegabonkLab.

## Launch
Double-click Start-AzerothMegabonk.cmd when you want to play. It launches only the separate Megabonk-Lab copy. No game is launched by this README. The original Steam game folder is unchanged.

The initial prototype uses Mage. Its configuration is Megabonk-Lab/BepInEx/config/local.azerothmegabonk.abilities.cfg (SelectedClass = mage). Set none to use native Megabonk weapons instead.

## Implemented
- Native Megabonk movement, enemy AI, encounters and spawn schedule remain the host systems.
- Four tome slots are available immediately through a runtime hook; weapon slot limits and saved unlocks are not overwritten.
- Azeroth creature visuals loaded from the local installation: boar, skeleton, murloc, wolf, kobold, gnoll and optional tarantula. The visual mapping to Megabonk enemy families is an integration choice, not an original Azeroth encounter roster.
- WoW-style HUD using the original Azeroth interface textures: player frame, action bar and gryphons; actual native health, experience, gold, timer and books. The native HUD and encounter dialogs remain during this first visual-validation stage.
- Five Mage spells and source-backed per-rank damage/cooldown/target/radius formulas. Damage/cooldown constants and rank formulas were recovered from the installed Azeroth v0.102.3 executable and checked against it.

## Prototype limitations
This is not a complete Azeroth Survivors port. Other classes, the rest of the Mage spell pool, talents, racials, original critical/proc behavior, original spell VFX and minimap are not reproduced. Source rank formulas are retained; card/rarity selection uses the Megabonk upgrade screen. Spell VFX are temporary procedural shapes. Boss visuals remain native until attack/phase animation integration can be verified.

Headless runtime checks and screenshots are recorded in reports/. Full interactive HUD layout, sustained gameplay balance and broad boss/pool stress testing are not yet verified. Read MODLOG.md for the exact latest results.

## Files and rollback
- Source: src/; conversion scripts: scripts/azeroth-assets/ and scripts/prepare-runtime-textures.py.
- Native local assets: private-assets/ and Megabonk-Lab/AzerothAssets/. Keep these private.
- Main log: MODLOG.md; worker logs: ASSET_LOG.md, BOOKS_LOG.md, UI_LOG.md, ABILITIES_LOG.md.
- Runtime log: Megabonk-Lab/BepInEx/LogOutput.log; normal launch log: reports/megabonk-play.log.
- Save backup: um-data/backups/megabonk-before-lab/20261020-193811.zip; registry export: backups/Megabonk-PlayerPrefs-before.reg.
- To play unmodified, use Steam to launch the original Megabonk installation. Do not restore an old backup over newer progress unless you intend to.

Universal Modder 0.2.0 is installed in Codex. Its skills are available on the next turn. The local CLI wrapper is um.ps1.
