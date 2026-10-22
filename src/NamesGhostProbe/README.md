# Names and endless ghosts native regression

QA-only plugin, excluded from all production runtime archives. Build against the pinned prepared Megabonk 1.0.69 IL2CPP interop. Install only in a separate disposable runtime with the production modules and prepared assets.

Set AZEROTH_LAB_HEADLESS_TEST=1 and AZEROTH_LAB_RENDER_PROBE=1 for the owned process; pass -batchmode -force-d3d11 -azeroth-elwynn -azeroth-test-menu -azeroth-talents-max -azeroth-talent-points=999 -azeroth-names-ghosts-smoke and a private -logFile. The fixture disables only its own input, suppresses save writes through test-menu, restores its locale, writes ../reports/names-ghosts-PID and quits its own process. Never reuse a player's running instance.

Checks actual native spawned actors, actual TMP captions for seven model families in RU/EN, native-data preservation, restore/rebind and pooled HUD reuse; five actual endless ghost variants, renderer material/MPB RGB and alpha, transform scale, opaque body render captures, real Kill/corpse scale, ordinary mob and GhostKing exclusions. First six boss fixtures use ReleaseToPool because native HUD capacity is six. Capture death actor ID before native pool reuse.

Isolated mesh captures prove bounded actual rendering behavior, not full gameplay visibility or source-engine pixel equivalence. Earlier failed fixture runs and corrections are recorded in MODLOG.md.
