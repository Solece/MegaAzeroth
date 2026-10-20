# V2 integration journal — 2026-10-21

Base checkpoint:4f72484. Runtime backup:backups/elwynn-v2-20261021-014042. Separate v2 runtime:private-assets/elwynn-v2-test-runtime/Megabonk-Lab; its Mage assets are real independent files, no write-through junction to the live game.

Authorized scope: original Elwynn buildings with safe gates/portals; Elwynn on subsequent stages; single golem body; native spawn emergence/VFX; native tome effects and luck/rarity; original Azeroth XP; learned-spell pause inventory; native Megabonk timer; clean post-combat; no cast poses/looped slide; frozen simulation during native UI and frozen character animation during slow motion.

Source WMO validation passed84 source hashes/52 textures/3 buildings/16 groups/30280 render triangles/8019 collision triangles. Twelve original aperture rays passed; runtime safety additionally uses actual capsule overlap/sweeps.

First v2 probe PID68224 reached all eleven decoration groups, then native GenerateMap threw NullReferenceException. Map-wide override was null; the original game's empty serialized array shape was restored. Corrected probe PID59808 completed generation and native gameplay:59.39703m movement, four enemies, unchanged native capsule, source assets survived explicit cleanup. Seed31415:17 native-placed buildings examined, two retained, eleven rejected terrain/two rejected doors/one overlap/one reserved; zero door/reserved violations and a verified native route. Configured Elwynn stage clones:3. Live game and originals unchanged. Failed logs retained.

Remaining integration: native rendered building/door views; updated modules combined; real golem/spawn/motion/tome/pause/shop/levelup/HUD/postcombat checks; native full three-stage/boss/final route; record evidence and only then deploy/checkpoint.

### Follow-up authorized after the playable v2 handoff

The user requested a playable build first, then transferring all available Azeroth characters, models, portraits and icons into the standard Megabonk hero selection. This separate phase starts only after the current build is delivered.

### Combined feature and native route evidence

Feature PID49604 completed18 PASS/3 FAIL. Bounce fixture referenced an enum whose native TomeData is absent; pause fixture tried to open before native CanPause; merchant fixture omitted native merchant context. All failures were retained. Real screenshots also exposed a broad GameTimer protection branch masking the mask itself, plus source result portrait/row layout issues; HUD0.7.1/0.7.2 corrected these incrementally.

Feature PID25764 completed20 PASS/0 FAIL/1 SKIP. The skipped native Bounce tome is absent from all26 installed TomeData; this does not claim an unrun ricochet test passed. Quantity produced extra real projectiles and HP loss; Damage, ProjectileSpeed, Cooldown, Size, Duration and Precision affected real spell instances/hits. Native pause, level-up and the actual ShadyGuy merchant froze source clocks/projectiles/status/VFX and resumed them. Source and enemy animation clocks froze in native slow motion and resumed. Original golem23 had one source body, no visible native body; ownership regression preserved foreign FX and actors. Original slide loop/body/staff checks passed. Native XP,13 learned spells, ranks and4 native tomes were verified. Real upgrade cards and selection passed luck/value consistency; native totals/DPS were retained in post-combat.

Luck sampling8192 rolls per case: baseline Luck0 common6131/8192; boosted Luck3 common3197/8192; current real tome Luck0.4 common5579/8192. Distributions matched native calculated weights, RNG state restored. Original Azeroth tier values retained; exact original Azeroth RNG distribution is not claimed.

Adventure PID62052 first failed on the real native weapon grid exposed by the timer mask bug. PID68472 failed because the old capture asserted ground before the restored native teleport emergence finished. PID61692 had a correctly grounded Lich outside the test camera frustum after a random-terrain fixture teleport. The test now waits for native Teleporting to finish and temporarily aims only the real capture camera at the source body if needed, restoring rotation afterward; native projection/positions/colliders/AI are unchanged. Failed logs retained.

Adventure PID61008 PASS: two native stage boss kills, both native portals, three ordinary Elwynn stages and live Pharaoh3 in the original FinalBossMap. All13 spell/model/VFX caches survived explicit UnloadUnusedAssets on every scene. Same inventory and ranks,121 spent talents and4 book slots retained. Ordinary-stage buildings retained1/4/1; all three stages had NativeRouteVerified=true,DecoratedRouteClear=true,149 native interactables before/after,0 reserved violations and0 door violations. Native routes were280.37/649.14/898.86m. Real rendered views inspected for farm facade/door, source golem, spider, stage2 and final arena. Source portrait fixed; final result spell-row layout is being corrected after visual inspection (diagnostic counts alone were insufficient).

### Playable v2 and current UI hotfix installed

The playable v2 was installed with pack/DLL hash verification and handed to the user through Start-Elwynn-Mage.cmd. Their running game was never stopped. The latest scoped hotfix installs HUD0.7.6, Talents0.1.1 and the tested Elwynn probe assembly; backup and hashes are in reports/v2-ui-hotfix-manifest.json. Already running processes retain their loaded assemblies until the next launch.

Final feature PID51996:21 PASS/0 FAIL/1 SKIP, reports/feature-51996 in the isolated runtime. All13 learned spells fit in an adaptive single row without a scrollbar in pause, native upgrade/shop inventories and results. Native books/items retain their layout. Native gold is restored, all three native XP-strip graphics (fill/background/frame) remain hidden during gameplay and native panels, and the source portrait is below the native counter row. Native result damage/DPS text is preserved. Ignite, Presence of Mind, Splitting Ice and Water Elemental map to source names/icons through the actual AzerothMageTalents assembly. The four presentation fixtures use genuine native attributed damage and explicitly do not prove spontaneous proc behavior. The source Luck talent is additive, matching the recovered zero-based original stat; real runtime bonus0.39999998 disappears when its nodes are removed. Quality selection still uses native Megabonk probability weights in this checkpoint; exact Azeroth probability distribution belongs to the following class-transfer phase.

Batch-only input isolation stops the user's keys from controlling a hidden probe; normal gameplay is unaffected. Latest combat, pause and result frames inspected. The camera-converted levelup capture has a frozen world effect drawn over UI; its layout/ranks are independently checked, but this frame is not presented as a clean visual result. Failed and intermediate probes remain on disk.

Next phase is explicitly authorized: all9 original classes with skills/talents in native selection and the original-art talent tree UI. Unverified HeroRoster source and all-class work are excluded from this v2 checkpoint. No game assets, game binaries, runtime packs or saves are committed.
