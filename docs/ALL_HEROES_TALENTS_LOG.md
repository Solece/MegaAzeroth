# All-class talent adapter audit

Date: 2026-10-21. Candidate kept in work/all-heroes-talents. Original game archives and executables were read only. No game launch, deployment, desktop input, Git operation, or global Windows configuration change was performed by this worker.

## Source authority

AzerothSurvivors v0.102.3 (f78e6fab), executable SHA256 2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a. SHA-pinned exporter recovers nine classes, 102 class spells (112 total including auxiliary/racials), 354 talents and 27 trees. Exact byte provenance is separate in the private exports/evidence directory; runtime catalogs retain original offsets and complete packed enum payloads.

## Candidate behavior

Talent catalog/profile selection follows HeroBridge.SelectedClassId before native inventory construction and preserves a separate XP/allocation for each class. Original normal progression remains 51 points, costs 80+15*n, total XP23205. The explicit temporary999 test budget can max every original rank without writing progression saves. Normal Druid Bear/Cat exclusion is enforced. The current Mage runtime is retained in LegacyTalentRuntime and switches only with the authoritative ClassCombat.GenericActive property; there is no independent generic-Mage switch.

The 28 observed source effect tags route to their respective source events: spell procs/counters, kill summons, shield bursts, impact splash, target-maxHP damage, cleave, repeated hits, source statuses, rolling DoTs, kill resets/explosions/spread, timed stats, healing/shields, death prevention, overheal shields, healing nova, chain falloff, echoes, forms, periodic casts, original starters, pets and filtered stat/spell modifiers. Missing ClassCombat methods produce bounded diagnostic failures rather than silently applying another effect.

Source global native stats and ClassCombat per-hit stats are applied once. Luck stays additive on a zero-based source stat. MaxHealth generic composition belongs to ClassCombat; this module skips its own legacy HP multiplier. Source talent DR is passed to ClassCombat, not applied a second time in IncomingDamagePatch.

## Native corrections verified from the installed source

- Counter event at effect+32 and rate limit at+8 are separate; Hunter Rapid Fire counts casts6 with rate0, Mage Hot Streak counts critical hits4 with rate2.
- Hit-status+8 is signed. 0xffffffff means vulnerability, not an invalid enum. Amount+12 and duration+16 are distinct; only the vulnerability amount scales by rank, whereas ordinary status chance scales.
- Talent DoT effect+16 is event0/2, not a tick count. VA145931CF0 calls roundf(duration), max1; all seven original durations4/6/12 yield one-second ticks. VA145932A94 rolls remaining damage into the new total. VA145884032..049 divides total damage and duration by tick count. First-tick consumer predicate remains unverified; the runtime currently schedules the first tick at one interval as an explicit timing adapter assumption.
- Source cleave radius is10.0: VA14592FC05 loads raw5d4b760 and VA145931AB3 passes it to target selection. The prior provisional8m value is removed.
- Source effect rank scaler VA1458DB210 preserves radii, caps, durations and event IDs; only documented amount/chance fields scale.
- Source event0 maps to the generic ordinary-hit notification, fixing Hunter Frenzy.
- Hit contexts use a per-enemy stack, with push/pop around native hits and generated talent damage. This prevents stale source spell attribution and restores context after nested procs. Diagnostics expose ActiveHitContexts.
- SourceStat25 is an additive global damage term EffectiveDR*Stat25/10 (VA14590B089..B096), not EffectiveDR/.1. SourceStat26 adds max(uncappedDR-cap,0)*Stat26. SourceStat28 is not collected by source stats rebuild and remains conditional per-hit handling in ClassCombat.

## Public verification API

MageTalents.TalentBridge: ClassId, SelectClass(string), CatalogJson, CatalogDiagnosticsJson, StateJson, Rank(string), CanSpend(string), CanRefund(string), Spend(string), Refund(string), Reset(), AvailablePoints, EarnedPoints, SpentPoints, TemporaryPointBudget. CanSpend/CanRefund operate on a clone. CatalogDiagnosticsJson reports all nine class/node/tree counts and explicitly marks NativeGameplayVerification=false.

MageTalents.TalentRuntime: UsesGenericRuntime, SourceStat(int), SourceSpellModifier(LearnedSpell,int) returning raw bonus, ChainNoFalloff, NotifySourceEvent(int,LearnedSpell,Enemy,float), NotifyHeal(float,float,string), NotifyShieldBreak(float,float,string), TryPreventDeath(DamageContainer), DiagnosticsJson. Source shield break dispatches event10 internally. Health-below event9 is dispatched only here in Tick. Source kill event6 is emitted by the native kill hook before DoT cleanup.

## Validation

1. Source byte validator: Passed=true, 10483 assertions. Independent checks include source SHA/bytes, strings, all112 source spell rows, allowed-upgrade normalization, signed statuses, tag9 event/tick fields and cleave radius.
2. Production candidate build: SDK10/net6 Release --no-incremental, 0 errors and0 warnings, referencing the separate all-heroes-abilities candidate DLL. Candidate output never overwrites the coordinator's build/deployment output.
3. Model harness: dotnet model-checks/bin/Release/net6.0/ModelChecks.dll, exit0, Passed=true,690 assertions. It compiles actual TalentCatalog.cs and verifies all354 ranks, normal51-point allocations,999 test budgets, XP, tier/refund constraints, pet filters, class/tree bridge JSON, trigger offsets, periodic auxiliary cooldown and the newly decoded statuses/DoTs/cleave.
4. Controlled negative harness: the same DLL with --verify-handled-failure, redirected stdout/stderr, exit1 with full InvalidOperationException stack trace in handled-failure.stderr.log. ModelChecks/WerFault process count after completion:0. This is an intentional handled test failure, not a game test.

Exact successful commands:

    & 'C:\Program Files\dotnet\dotnet.exe' build 'E:\CodexData\Files\2026-10-20\new-chat\work\all-heroes-talents\MageTalents.csproj' -c Release --no-incremental --nologo
    & 'C:\Program Files\dotnet\dotnet.exe' 'E:\CodexData\Files\2026-10-20\new-chat\work\all-heroes-talents\model-checks\bin\Release\net6.0\ModelChecks.dll' 1> model-checks-success.log 2> model-checks-success.stderr.log
    & 'C:\Program Files\dotnet\dotnet.exe' 'E:\CodexData\Files\2026-10-20\new-chat\work\all-heroes-talents\model-checks\bin\Release\net6.0\ModelChecks.dll' --verify-handled-failure 1> handled-failure.stdout.log 2> handled-failure.stderr.log

## Model-check incident and fix

Two earlier apphost ModelChecks.exe runs encountered unhandled managed exceptions: (1) sourceStatus0xffffffff was incorrectly deserialized as an unsigned Int32 value; (2) a model assertion incorrectly assumed every greedy51-point allocation must contain a blocked refund. The Windows crash dialog observed by the user came from this standalone checker, not from a launched game. Both causes were corrected. The harness now has UseAppHost=false and a top-level try/catch that writes full diagnostic stderr and exits1. It is invoked through dotnet DLL only. The controlled negative test above proves the failure is handled without an OS crash dialog. No WER, registry, desktop, or unrelated process configuration was changed.

## Material limitations

Compilation and source/model proofs are not native gameplay verification. The coordinator must verify actual class selection, profile persistence, all event families, statuses, pets/forms, kill filters, spell cards and native stat composition. Only the explicit generic-Mage probe may switch the current Mage runtime. Original source first-zone XP baseline is retained; no invented mapping from Megabonk maps to Azeroth's twelve zones is claimed.
