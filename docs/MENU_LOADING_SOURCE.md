# Original Azeroth menu and loading visuals

Original version: 0.102.3 (f78e6fab), executable SHA256 `2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a`.

The original 1920x1080 Mage picker reference visibly uses the Eastern Kingdom loading artwork behind its native controls. This identity is also confirmed by a native source binding: function VA `14027e1d0..14027f02e` reads chosen-zone loading key at `+98`, formats `mpq://interface/glues/loadingscreens/<key>.blp` at `14027e44f`, loads that image and sets its source color to `(0.40,0.38,0.36,1)` at `14027e4bc`. The original node initialization uses Percent(-3) for two offsets and Percent(106) for width/height at `14027e28f/2a5`, producing slight overscan without replacing the full source UV by an aspect-cover crop. The original reference preserves the top-right Warcraft logo.

Elwynn's original loading key is `loadscreeneasternkingdom` (raw source registry `6b7d288`). LoadingScreens.dbc row4 names `Interface\Glues\LoadingScreens\LoadScreenEasternKingdom.blp`. The menuScene AnimatedZone/Black setting is a separate original option; its text is not evidence that this observed static artwork layer is absent.

Two subsequent native menu gradient color constants are black alpha0.8 (VA `14027e741`, raw `5da7e70`) and black alpha0.6 (`14027e859`, raw `5d4b3e0`). Exact gradient geometry/animation is not fully decoded. Native gold mote color `(1,.48,.11,.5)` is read at `14027eb8e`. A Unity adapter can use these verified colors while honestly documenting any simplified vignette or decorative animation.

The genuine loading frame is `Interface\Glues\LoadingBar\Loading-BarBorder.blp` (512x64) and fill is `Loading-BarFill.blp` (64x64), read by original native calls at VA `140d20e21` and `140d20f22`. Loading progress must follow the target game's actual asynchronous loading state; do not impose a fabricated timer or source-game loading duration. An original loading-screen capture has not been produced in this audit.

Export all five supplied original textures from owned MPQs:

```powershell
python export_menu_loading.py --repo "C:/MegaAzeroth" --data-dir "C:/OwnedAzeroth/Data" --output "C:/PrivateAssets/MenuLoading"
```

The exporter requires existing repository `scripts/azeroth-assets/enemy_fidelity_mpq.py`, `enemy_fidelity_blp.py`, NumPy, Pillow and mpyq. Source archive bytes and raw RGBA hashes are pinned by recipes; outputs are unchanged decoded PNG plus bottom-up RGBA32 and a width-height `.size` file. No original archive or executable is copied. Only scripts, texture paths, operations and hashes belong in a reproducible source handoff; publication policy for prepared data is decided separately by the user.
