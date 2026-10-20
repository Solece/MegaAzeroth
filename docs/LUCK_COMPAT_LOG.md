# Luck and source spell-card quality compatibility

Date: 2026-10-21. This change uses the installed Megabonk rarity implementation with original Azeroth upgrade-tier values. It does **not** claim that the unrecovered original Azeroth card-quality distribution is identical.

## Source authority

- Installed AzerothSurvivors.exe SHA256: `2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a`.
- The installed source run-stats Lua text around raw0x6b8c62b explicitly associates Luck with rarer level-up upgrades. This establishes the intended role of Luck; it does not establish the source probability formula or initial spell-quality remapping.
- Original source upgrade tables remain unchanged: damage raw0x6b948f4 `[.18,.25,.34,.46,.65]`; cooldown raw0x6b94920 `[.07,.10,.13,.17,.23]`; targets raw0x6b94950 `[1,1,1,2,2]`; radius raw0x6b94980 `[.12,.18,.25,.35,.50]`; duration raw0x6b949b0 `[.15,.22,.30,.40,.60]`. These feed existing diminishing/cap formulas in `OriginalUpgradeRules.Delta` and `Apply`.
- Installed Megabonk GameAssembly.dll SHA256: `4350a7ae25ba7aec35677c213fdceabbb638676bb00ab59131d7d9c37b6e3d9e`.
- Installed global-metadata.dat SHA256: `17b506a77616b943b414b9a42d35e3acbdaf71eec5dc6c470fc4da338010d550`.
- `Rarity.GetUpgradeOfferRarity(float luck)` RVA0x42D2C0 allocates five rarity IDs and five weights, invokes `CalculateRarityWeights` RVA0x42D1B0, then samples the cumulative normalized weights through native Unity Random.Range. The exact installed IDs are1..5 at metadata raw0x73dec8; base weights are `[1.5,.3,.15,.04,.0085]` at metadata raw0x748388. The field payload hashes/names in Il2CppDumper identify the two arrays as `4F6ADDC9659D6FB90FE94B6688A79F2A1FA8D36EC43F8F3E1D9B6528C448A384` and `EA036D72C27ACD4F2F298E31BB7F30C27D694D1F4E2345BD13EE7F085C44CADB` respectively.
- `UpgradeButton.SetUpgrade` RVA0x55F890 uses `PlayerStats.GetStat(EStat.Luck)` for an already-owned upgradable at0x55F9A3..0x55F9B6, then stores the actual native quality at button+0xD8. An unowned native template uses ERarity.New without a quality roll. `EStat.Luck` is enum30, base0; a value1 means +100% on this native stat convention.
- Production selection calls the native rarity method itself. Diagnostic weights call native `CalculateRarityWeights` on a fresh array containing the verified initial weights. No inferred C# probability formula replaces the native implementation.

## Problem and final behavior

Previously, existing native template qualities already reached source upgrade values. The uncovered defect was that an unowned native weapon template had a separately rolled source quality but retained `ERarity.New` on its native field and colored frame. Its label and the strength of its actual source upgrade could disagree. Newly learned spells also hid quality behind a generic Azeroth rarity label.

`LuckCompatibility.ResolveQuality` preserves an existing Common..Legendary native roll exactly. For a New template only, it invokes the native rarity roll using the player's current real Luck, then assigns that same quality to the card field, source upgrade tier, rarity text, text/border/background colors and subsequent selection diagnostics. Source upgrade values continue using the untouched original five-tier tables and diminishing/cap rules. Choosing which spell/modifier is offered remains a separate uniform source-choice step; Luck changes card quality rather than secretly preferring a particular spell.

Native tome and item cards never enter this source weapon-template branch; their normal native quality generation, native modifier lists and selection path remain unchanged. Normal play receives no statistical probe or inventory grant.

Learning an unknown spell still grants its verified rank-one base spell values. The exact original Azeroth initial-quality-to-new-spell stat remapping has not been recovered, so no invented random bonus is applied. The quality display is now consistent, but a full 1:1 source new-spell rarity port remains outside the verified behavior. This limitation is reported explicitly in diagnostics.

## Runtime proof contract

Public type: `AzerothAbilities.LuckCompatibilityDiagnostics`.

- `CurrentLuck`: actual current `PlayerStats.GetStat(EStat.Luck)`.
- `SnapshotJson`: real current native weights, accepted existing native quality count, new-template roll count, source binding/selection counts, value mismatch count, bound card metadata, last applied selection and original source upgrade tables.
- Bound card metadata includes `NativeTemplateRarity`, `Quality`, `SourceRarityIndex`, `NativeQualityAccepted`, `RolledForNewTemplate`, `AppliedCardRarity`, `DisplayedRarity`, `SourceDelta`, `ExpectedSourceDelta`, `SourceValueMatchesQuality` and source offset/evidence. Selection is recorded after the real `AbilityRuntime.Apply` call and reports the learned rank.
- `SampleNativeQualitiesForProbe(int samples=8192, float baselineLuck=0, float boostedLuck=3)` is allowed only in an active Mage batch process with `AZEROTH_LAB_HEADLESS_TEST=1`. It samples the actual native rarity method at baseline, boosted and current inventory Luck, starts each stream from the same saved Unity Random state, and restores the original state in `finally`.
- Samples are bounded to512..16384 per stream; boosted Luck must be greater than baseline and at most100. `LastSamplingJson` records every bin, actual native weights, observed fractions/mean and expected mean. `Passed` requires restored RNG, normalized valid weights, each bin within six binomial standard deviations plus3 counts, and increased expected/observed mean quality at the boosted Luck. The same API can run again after a real native Luck-tome grant to report the changed actual inventory distribution.
- Root's feature probe owns real level-up UI generation, clicking a bound source upgrade and native tome selection. No fake card/value/counter injection is exposed by this module.

## Validation checkpoint

Static checks passed against the installed binaries: exact hashes, native ID/base-weight payloads, all five Azeroth tier tables within original float precision and original Luck-tooltip semantics. Scratch report: `work/luck-static-source-check.json` in the task workspace. An isolated net6 build of the complete ability sources and current loader references passed with zero warnings/errors; DLL output is in scratch only. No game launch, deployment or Git operation was performed by this subtask. Actual native distribution/selection results are to be appended by the coordinator after its real runtime probe.
