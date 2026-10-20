# All-class source catalog extraction

Date: 2026-10-21. Source installation is read-only. No game launch, injection,
deployment, production C# edits, or Git operations were performed by this exporter.

## Pinned source

- Azeroth Survivors `v0.102.3 (f78e6fab)`.
- Executable SHA256: `2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a`.
- PE image base: `0x140000000`.
- Source: `D:/Games/Saitan/AzerothSurvivorsWindowsNew/AzerothSurvivors/AzerothSurvivors.exe`.

## Export and validation

`extract_catalogs.py` emits the nine classes, 102 class spells, ten auxiliary
spells, 354 talents in 27 original trees, and eight independent races. Data comes
from original static registries, not guesses from Warcraft spell names or DBC
defaults. Existing Mage balance scales and upgrade rules are preserved.

`exports/evidence/` stores bounded exact raw bytes, offsets, RVAs, VAs, and SHA256
for every source class/spell/talent/effect record. Runtime catalogs preserve all
original strings, numeric fields, packed discriminants, icons, original Spell.dbc
IDs, allowed upgrades, and exact descriptions. `source-semantic-enums.json`
separates known semantics and remaining limitations.

`validate_catalogs.py` independently checks source hashes/records, class
membership/starters, all spell f32 fields, integer fields, upgrade membership,
all effect payload words, talent placement/ranks, optional auto-weapon values,
and the previously verified Mage fields. Latest result: **10,440 assertions
passed**. This is source-data validation, not all-class gameplay verification.

## Verified registry layouts

- Visible class row: raw `0x6b867c0`, stride `0x90`, nine rows.
- Native class pointer is visible row minus `0x10`; lookup raw `0x71863a0` and
  Warrior fallback raw `0x6b867b0` establish this relationship.
- Spell table: raw `0x6b87ab0`, stride `0x78`, 112 rows. The class membership
  slices select 102 rows; the remainder are eight racials, Penance, Arcane Power.
- Talent tree registry: raw `0x717d018`, class blocks `0xc8`, three 64-byte tree
  descriptors per class. Talent row stride 104, effect stride 64.
- Race registry: raw `0x6b85640`, stride 88, eight rows; bonus slice stride eight.

## Important corrections and semantics

1. The 16-byte optional auto-weapon struct is the native class **prefix**, not
   the visible row tail. The visible tail overlaps the next class prefix.
   Correct original melee auto-swings: Warrior `27.5 / 1.6 sec / 6 yd`, Paladin
   `27 / 1.8 / 6`, Rogue `14 / 1.2 / 6`. All other classes have this system
   disabled. Hunter Auto Shot is a normal class spell. SourceStat16 affects only
   these separate auto-swings: `source-auto-weapon-stat16.txt`, native
   `VA145913D20..145913E75`. Earlier provisional Warrior/Hunter association was
   corrected before handoff and before any production changes.
2. Native class `+0x80` is baseHP; `+0x84` is base damage reduction.
   `+0x88` is retained as unresolved `sourceParameter88`, never claimed or
   applied as regeneration. `source-player-base-stats.txt` contains native
   consumers. Stats also have level/race/gear/talent factors; these are not
   flattened into invented class constants.
3. Packed effect upper bytes are payloads, not universal flags: tag3 school,
   tag11 death condition, tag15 stat. Low byte selects one of 28 observed tags.
4. Tag0 proc has chance at +4, internal cooldown seconds at +8, optional Rust
   spell string at +16, event at +32, event value at +36, filter at +40. An empty
   spell means repeat the triggering spell (Lightning Overload/Twin Totems).
   Tag1 has count at +4, **maxPerSecond at +8**, spell at +16, event at +32,
   filter at +40. Mage Hot Streak's event2 and maxPerSecond2 happen to coincide;
   treating +32 as maxPerSecond breaks other classes.
5. Filter4 is original cast kind. Native predicate compares `spell+0x75` against
   `filter+1`. Filter6 means incoming sourceKind2 (pets). School/cast-kind
   filters exclude pet damage. Evidence: `source-runtime-filter-predicate.txt`,
   native `VA1459342C0..1459344E9`, jump tables raw `0x71866d8/0x71866f4`.
6. Rank scaling is selective. Native `VA1458DB210..1458DB576` and jump table
   raw `0x7181848` scale amount/chance fields while preserving duration, radius,
   caps, counts and event fields. Each effect exports `rankScaleFields` and
   `rankScaleEvidence`. See `source-talent-rank-scaling.txt`.

## Luck proof

Source Luck has baseline zero and composes additively from race/gear/talent/
timed stat sources. Native stats rebuild `VA14590A850..14590B8FC` initializes
Luck to zero, adds race kind10, gear kind13, SourceStat12, and stores player
`+0x49c`. See `source-player-stat-rebuild.txt` and serialization proof.

Native original offer roulette is recovered in `source-offer-rarity.txt`:

```text
u = 1 + playerLuck + min(playerLevel * 0.01, 0.30) + extraLuck
weights = [60, 25*u, 10*u*u, 4*u*u*u, u*u*u*u]
```

Offer kind0 uses this directly at `VA145958960..1459589E9`; offer kind1 adds
0.5 extra Luck at `VA1459588AB..14595892C`. Do not assign the extra0.5 to spell
offers until the offer-kind classification is independently established.

## Pets and forms

Original pet descriptors include creature entry/display IDs and native values:
Wolf9510/165, Raptor9511/1959, Imp9512/4449, Felguard9514/5047,
SpiritWolf9516/776, Treant9515/2078, WaterElemental9517/110. Tag2 kill-summon
uses the pet pointer at +16; tag29 permanent summon uses +8. They are pointers,
not Rust strings. Duplicate summon effects create the original two companions.

Form effect25 stores a Spell.dbc ID, not a display ID. Independent native proof
from hero-visual agent: source `RVA5907119` dispatches Bear9634 to display2281
(Tauren2289), Cat768 to display892 (Tauren8571), while Shadow15473 retains the
hero model. No Moonkin effect occurs among the354 source talents.

## Remaining verification

Original catalog semantics and payloads are complete. New cross-engine event
adapters, native timing details, status behavior, pet attacks, class auto-swings,
talent exclusivity, and playable selection still require implementation and
in-game verification by the coordinator. Exported `sourceBehaviorPending`
markers are explicit; this exporter does not claim newly decoded behaviors
already work in the Megabonk runtime.

## Final native effect corrections and validation

Talent effect9 raw+16 is triggerEvent0/2, not ticks. Tick count is max1(roundf(duration)) at145931CF0; seven original durations4/6/12 mean one-second ticks. The same effect/DBC combines remaining damage into the replacement total at145932A94, and helper145884032 divides total/period by tickcount. Cleave tag6 radius10 is loaded at14592FC05 and passed at145931AB3. Signed status8 sentinel0xffffffff is vulnerability; amount+12 and duration+16 are independently exported. Source effect/rank validator now passes10483 assertions.

SourceStat25 proof: tableVA147187758/raw7186558 index25 targets14590B796; the final global damage rebuild multiplies effectiveDR by this accumulator at14590B089 and divides by10.0 at14590B096, then adds overcap*Stat26 and ordinary damage. Stat28 skips global rebuild and is handled contextually per hit. No inverse0.1 factor is used.

Extractor and validator now require explicit private output/input paths, preventing accidental publication of byte provenance beside tracked scripts.
