# Azeroth ability runtime log

2026-10-20: Scope received from root. Original Azeroth v0.102.3 (f78e6fab) mage spells; retain original numeric config and upgrades where recoverable. Keep Megabonk movement/spawns and books. Never invent numbers and label them original.

Source-of-truth plan: use installed EXE + extracted original JSON/Lua. UI agent extracted original-en.json (2,632 keys), original spell IDs, icon assets; numeric damage/cooldown not recovered yet. Build manifest-driven runtime first. Any missing or unsupported numeric/formula field must fail closed with readable logging; keep native weapons unless an original Azeroth class is explicitly active and valid. Start one proven mage spell before expanding.

Megabonk API source: Il2CppDumper dump and actual BepInEx interop from game 1.0.69. Enemy.DamageFromPlayerOther is available; weapons disabled only while this runtime is active; upgrade panel must keep native book offers.

## Source recovery: confirmed evidence

- Original EXE SHA256: 2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a. Rust + Bevy0.18.1 identified from embedded source-path strings. Only read the file; no original process/input used by this agent.
- Real static ability table starts raw0x6b87ab0,112 rows of0x78 bytes. Fireball row raw0x6b899a0. Four Rust ptr/len slices at0x00/10/20/30: ID, Englishname, icon, ALLOWED UPGRADE KIND BYTES (the fourth slice initially looked like tags; confirmed by native enum dispatch). Words0x40 onward contain WoW ID,damage,cooldown,range,radius,targetcount,duration,tickcount,projectilespeed,etc.
- Fireball raw: damage50.5,cd1.3,range30,count1,speed24. Native damage0x14595DE6C..0x14595DEDC and cooldown0x145886711..0x14588672F apply source balance scale1.4 (float bits at raw0x5d640d0). Result70.7 damage/1.82sec matches root observed original Mage tooltip71/1.8 (rounded). Cooldown source floor0.25.
- Native cast-kind damage table raw0x7186414 scales selfAoE/groundAoE/cone by0.5; ArcaneExplosion25.2*1.4*0.5=17.64. Corrected manifest/runtime; never treated its unscaled25.2 as final damage.
- FireBlast and ArcaneMissiles use random-target cast-kind1; Fireball/Pyroblast kind0 nearest with boss/elite priority. Projectile art/collision is a Unity adaptation; not original VFX.
- Original upgrade value helper VA0x1458978C0; apply and rank increment VA0x14595AC8C. Proof files azeroth-upgrade-values.txt and azeroth-upgrade-math.txt.
- kind0 Damage: rarity common..legendary [.18,.25,.34,.46,.65]/(1+.5*numberOfPreviousDamagePicks); damageFactor *=1+delta.
- kind1 Cooldown: rarity [.07,.10,.13,.17,.23]*max(0,(cooldownFactor-.2)/.8); cooldownFactor -=delta.
- kind2 ExtraTargets: rarity[1,1,1,2,2], capped by12-extraTargets.
- kind3 Radius: rarity[.12,.18,.25,.35,.50]*max(0,(5-radiusFactor)/4); radiusFactor +=delta.
- Every upgrade rank++ also multiplies damageFactor by1+.04/(1+.125*max(newRank-2,0)), after applying the chosen effect. First common damage pick therefore1.18*1.04=1.2272 multiplier; repeat damage delta falls to.12. Implemented exact float arithmetic order.
- kind4 Duration recovered in native helper (rarity .15/.22/.30/.40/.60 *max(0,(6-durationFactor)/5)); NOT enabled because current5 spells have no duration upgrade in original table. Unsupported Frost/DoT/buff spell semantics remain omitted.

## Implementation and validation

- src/AzerothAbilities: BepInEx6 IL2CPP net6 plugin + mage.json. Current five supported spells: Fireball,ArcaneMissiles,ArcaneExplosion,FireBlast,Pyroblast. Original numeric fields and upgrade kinds are extracted, not approximated.
- SelectedClass defaults none. Only explicit mage selection via config or HudBridge.SelectClass("mage") suppresses WeaponBase.Use. Invalid catalog or runtime exception disables custom casting and preserves/restores native weapon casting.
- UpgradeButton.SetUpgrade/SelectUpgrade patches reuse native weapon-offer cards as source-backed spell learning/upgrade choices. Native tome cards are not changed. Original upgrade value math is preserved; offer/card frequency and rarity are an adaptation of Megabonk, not yet original Azeroth RNG.
- Applying a custom choice closes the existing LevelupScreen through its native CloseLevelupScreen method. Source progress lives in runtime only, not game saves. A new PlayerInventory resets learned spells to original Mage starterFireball.
- Damage uses Enemy.DamageFromPlayerOther(DamageContainer), procCoefficient0 to avoid accidentally importing native weapon proc logic. Native movement/spawner untouched. Native passive/racial/crit behavior is NOT yet a faithful Azeroth replacement.
- Native damage/attack speed/projectile/radius multipliers adapt Megabonk books/items to custom spells. Final stat interactions, combat balance, target priority and collision need real gameplay verification. Five spells are a vertical slice, not a claim that all13 Mage spells/talents are implemented.
- Procedural Unity orb/pulse visuals are clearly marked and skipped in Application.isBatchMode. No game/window/input launched by this agent. User requested PC remain free; all work after that instruction was file reads/builds/console validation.
- Public HUD API AzerothAbilities.HudBridge has Active/ClassId/SpellIds/SpellRanks/CooldownsRemaining/CooldownsTotal plus CastCount/HitCount/DamageDealt and SelectClass.
- Build succeeded with0 warnings/errors. Generated interop Assembly-CSharp defines a conflicting NullableAttribute; source projects use Nullable=disable and regular class instead of C#record to avoid that compiler conflict. No changes made to game assemblies.
- Console source-validation passed: installed EXE SHA256, bit-for-bit matching combat fields for all5 spells, exact allowed-upgrade slices, all rarity lookup tables, original1.4 balance bits, Fireball71/1.8 displayed match, rank2damage1.2272, repeatdamage falloff, native targetcap12 and cooldownfactor floor0.2.
- Console output: reports/megabonk-research/ability-source-checks.txt. This proves numeric source matching and math, NOT successful gameplay/plugin load. Root owns headless/game smoke verification and deployment.

## Headless native verification checkpoint (root-owned execution)

Root started a no-input batch-mode native Megabonk laboratory run. I read its actual BepInEx/LogOutput.log: lines450-465 show Mage initialized Fireball70.7/1.82, real casts and multiple damage applications through DamageFromPlayerOther. First hit enemy0 requested70.700,hp7.625→0; elite-sized enemy9hp77.533→6.833 then next hit→0. No ability module exception in the observed excerpt. This is native gameplay evidence, stronger than source-only validation. It does not verify all5 spells or the visual card-selection flow.

Added public HudBridge.ApplyUpgradeForProbe(spellId,kind,rarity) for root's game-thread headless check. Kind0damage/1cd/2targets/3radius; original rarity0common..4legendary. Validates active class, learned spell and original allowed kinds, then invokes the same runtime Apply path as card selection. Added SourceDamages/SourceCooldowns arrays without native book multipliers for before/after assertions. Root will test Fireball common damage rank1→2:70.7→86.76304 and unchangednative tomes/slots.

Correct count:22 PASS in the standalone source-validation report. Current build0warnings/errors. Full ability raw extraction now names the fourth slice allowedUpgradeKinds and spans exactly112 source catalog rows, matching native lookup loop0x3480/0x78.

## 2026-10-20: Upgrade-card icon crash fix

- Replaced the remaining `ImageConversion.LoadImage` call in `AbilityOffers.Icon` with a pinned `LoadRawTextureData(IntPtr, length)` upload from the staged bottom-up `.rgba` and `.size` assets.
- Validates dimensions and byte length before allocating a native texture. Always frees the pin; destroys partially created textures if loading fails and logs the specific missing/invalid asset once per spell.
- Upgrade cards use Unity `RawImage.texture`, so there is no temporary Sprite to collect. The plugin cache keeps each uploaded Texture2D for reuse across card generations.
- All five Mage spell icons already have prepared 64 x 64 RGBA assets; no image decoding or game-asset conversion is performed in the live upgrade-card path.
- Source fix only at this checkpoint. Root process will build, deploy and exercise it in the background with the other crash fixes.
- Validation after selector integration: Release build succeeded with 0 warnings and 0 errors. All five prepared Mage icon buffers have the expected 16384 bytes (64 x 64 x 4). Runtime validation remains delegated to the root background test.
