using System.Text.Json;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Menu.Shop;
using Assets.Scripts.Utility;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Random = UnityEngine.Random;

namespace AzerothAbilities;

internal sealed class NativeQualitySample
{
    public float Luck { get; init; }
    public int Samples { get; init; }
    public int[] Counts { get; init; }
    public float[] NativeWeights { get; init; }
    public double[] ObservedFractions => Counts.Select(n => (double)n / Samples).ToArray();
    public double ExpectedMeanQuality => NativeWeights.Select((w, i) => (double)w * (i + 1)).Sum();
    public double ObservedMeanQuality => Counts.Select((n, i) => (double)n * (i + 1)).Sum() / Samples;
    public bool DistributionMatchesNativeWeights => Counts.Sum() == Samples && NativeWeights.All(w => float.IsFinite(w) && w >= 0)
        && Math.Abs(NativeWeights.Sum() - 1) < .0001
        && Enumerable.Range(0, 5).All(i => Math.Abs(Counts[i] - Samples * NativeWeights[i])
            <= 6 * Math.Sqrt(Samples * NativeWeights[i] * (1 - NativeWeights[i])) + 3);
}

internal static class LuckCompatibility
{
    // Installed global-metadata.dat raw0x748388, used by native RVA0x42D2C0.
    // Keep these as observed IEEE754 values; production rolls call the game.
    internal static readonly float[] NativeBaseWeights = { 1.5f, .3f, .15f, .04f, .0085f };
    internal static readonly Dictionary<IntPtr, object> Cards = new();
    internal static int AcceptedNativeQualities, NewTemplateRolls, SourceBindings, SourceSelections, ValueMismatches;
    internal static object LastSelection;
    internal static string LastSampling = "{}";
    internal static float Luck => PlayerStats.HasStats() ? PlayerStats.GetStat(EStat.Luck) : 0;

    internal static ERarity ResolveQuality(UpgradeButton button)
    {
        var quality = ClassCombat.GenericActive?SourceQuality.Roll():button.rarity;
        if (ClassCombat.GenericActive)SourceQuality.Rolls++;
        else if (quality >= ERarity.Common && quality <= ERarity.Legendary) AcceptedNativeQualities++;
        else { quality = Rarity.GetUpgradeOfferRarity(Luck); NewTemplateRolls++; }
        if (quality < ERarity.Common || quality > ERarity.Legendary)
            throw new InvalidDataException("Native upgrade rarity returned outside its verified five-tier range");
        // New native weapon templates use ERarity.New and have its neutral frame.
        // Once reused for a source spell, show the same real quality used by its
        // source upgrade value. Native tome/item cards never enter this path.
        button.rarity = quality;
        var color = MyColorUtility.RarityToColor(quality);
        if (button.t_rarity != null) button.t_rarity.color = color;
        if (button.iconBorder != null) button.iconBorder.color = color;
        if (button.iconBackground != null) button.iconBackground.color = color;
        if (button.background != null) button.background.color = MyColorUtility.GetRarityColorBackground(quality);
        return quality;
    }

    internal static void Bound(UpgradeButton button, AbilityChoice choice, ERarity nativeBefore, ERarity quality)
    {
        SourceBindings++;
        bool upgrade = choice.Upgrade != null, matches = true;
        float expected = 0;
        int kind = -1;
        if (upgrade)
        {
            kind = int.Parse(choice.Upgrade.Id);
            var learned = AbilityRuntime.Spells.First(s => s.Definition.Id == choice.Spell.Id);
            expected = AbilityRuntime.Catalog.UpgradeRules.Delta(learned, kind, (int)quality - 1);
            matches = Math.Abs(choice.Upgrade.Value - expected) <= 1e-6f;
            if (!matches) ValueMismatches++;
        }
        Cards[button.Pointer] = new
        {
            Button = button.Pointer.ToInt64(), Spell = choice.Spell.Id, IsUpgrade = upgrade, UpgradeKind = kind,
            NativeTemplateRarity = nativeBefore.ToString(), Quality = quality.ToString(), QualityValue = (int)quality,
            SourceRarityIndex = (int)quality - 1, CurrentLuck = Luck,
            NativeQualityAccepted = !ClassCombat.GenericActive && nativeBefore >= ERarity.Common && nativeBefore <= ERarity.Legendary,
            RolledSourceQuality = ClassCombat.GenericActive,SourceWeights = ClassCombat.GenericActive?SourceQuality.Weights(Luck,SourceQuality.Level):null,
            RolledForNewTemplate = nativeBefore == ERarity.New, AppliedCardRarity = button.rarity.ToString(),
            DisplayedRarity = button.t_rarity?.text, SourceDelta = choice.Upgrade?.Value ?? 0,
            ExpectedSourceDelta = expected, SourceValueMatchesQuality = matches,
            SourceEvidence = choice.Upgrade?.SourceEvidence ?? choice.Spell.SourceOffset
        };
    }

    internal static void Selected(UpgradeButton button, AbilityChoice choice)
    {
        SourceSelections++;
        var learned = AbilityRuntime.Spells.FirstOrDefault(s => s.Definition.Id == choice.Spell.Id);
        LastSelection = new
        {
            Spell = choice.Spell.Id, UpgradeKind = choice.Upgrade?.Id,
            Delta = choice.Upgrade?.Value ?? 0, AppliedNativeRarity = button.rarity.ToString(), CurrentLuck = Luck,
            LearnedRank = learned?.Rank ?? 0, SourceCard = Cards.TryGetValue(button.Pointer, out var card) ? card : null
        };
    }

    internal static float[] Weights(float luck)
    {
        var native = new Il2CppStructArray<float>(NativeBaseWeights);
        Rarity.CalculateRarityWeights(native, luck);
        var result = new float[5]; for (int i = 0; i < 5; i++) result[i] = native[i]; return result;
    }

    internal static NativeQualitySample Sample(int count, float luck)
    {
        var bins = new int[5];
        for (int i = 0; i < count; i++)
        {
            int rarity = (int)Rarity.GetUpgradeOfferRarity(luck);
            if (rarity < 1 || rarity > 5) throw new InvalidDataException("Native quality sample left verified enum range");
            bins[rarity - 1]++;
        }
        var weights = Weights(luck);
        return new NativeQualitySample { Luck = luck, Samples = count, Counts = bins, NativeWeights = weights };
    }
}

/// <summary>Read on the Unity thread. Statistical mutation is bounded batch-test only.</summary>
public static class LuckCompatibilityDiagnostics
{
    public static float CurrentLuck => LuckCompatibility.Luck;
    public static string LastSamplingJson => LuckCompatibility.LastSampling;
    public static string SnapshotJson => JsonSerializer.Serialize(new
    {
        Mode = ClassCombat.GenericActive?"Recovered original Azeroth spell quality roulette; native Megabonk books unchanged":"Verified native Megabonk rarity adaptation; original Azeroth upgrade tier values",
        ExactAzerothDistributionRecovered = ClassCombat.GenericActive, CurrentLuck,
        CurrentSourceWeights=SourceQuality.Weights(CurrentLuck,SourceQuality.Level),SourceLevel=SourceQuality.Level,SourceQuality.Rolls,
        QualityNames = new[] { "Common", "Uncommon", "Rare", "Epic", "Legendary" },
        NativeBaseWeights = LuckCompatibility.NativeBaseWeights,
        CurrentNativeWeights = LuckCompatibility.Weights(CurrentLuck),
        LuckCompatibility.AcceptedNativeQualities, LuckCompatibility.NewTemplateRolls,
        LuckCompatibility.SourceBindings, LuckCompatibility.SourceSelections, LuckCompatibility.ValueMismatches,
        BoundCards = LuckCompatibility.Cards.Values.ToArray(), LuckCompatibility.LastSelection,
        SourceUpgradeTables = AbilityRuntime.Catalog?.UpgradeRules,
        NewSpellLimit = "Learning a spell retains original verified rank-one base values; unknown source initial-quality remapping is not invented"
    });

    public static bool SampleNativeQualitiesForProbe(int samples = 8192, float baselineLuck = 0, float boostedLuck = 3)
    {
        if (!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST") != "1" || !AbilityRuntime.Active
            || samples < 512 || samples > 16384 || !float.IsFinite(baselineLuck) || !float.IsFinite(boostedLuck)
            || baselineLuck < 0 || boostedLuck <= baselineLuck || boostedLuck > 100) return false;
        var saved = Random.state; NativeQualitySample baseline = null, boosted = null, current = null; bool restored;
        try
        {
            baseline = LuckCompatibility.Sample(samples, baselineLuck);
            Random.state = saved; boosted = LuckCompatibility.Sample(samples, boostedLuck);
            Random.state = saved; current = LuckCompatibility.Sample(samples, CurrentLuck);
        }
        finally { Random.state = saved; }
        restored = Random.state.Equals(saved);
        bool passed = restored && baseline.DistributionMatchesNativeWeights && boosted.DistributionMatchesNativeWeights
            && current.DistributionMatchesNativeWeights && boosted.ExpectedMeanQuality > baseline.ExpectedMeanQuality
            && boosted.ObservedMeanQuality > baseline.ObservedMeanQuality;
        LuckCompatibility.LastSampling = JsonSerializer.Serialize(new
        {
            Passed = passed, NativeMethod = "Rarity.GetUpgradeOfferRarity", SharedInitialRandomState = true, RandomStateRestored = restored,
            Baseline = baseline, Boosted = boosted, CurrentInventoryLuck = current
        });
        return passed;
    }
}
