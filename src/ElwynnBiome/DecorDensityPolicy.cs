using System.Globalization;

namespace AzerothMegabonk.ElwynnBiome;

// This switch belongs to the explicitly requested test configuration. Normal
// runs and unsupported/ambiguous flag values always retain the original amount.
internal sealed class DecorDensitySettings
{
    internal float Multiplier { get; }
    internal bool TestMode { get; }
    internal string[] RequestedValues { get; }
    internal string Reason { get; }
    internal DecorDensitySettings(float multiplier, bool testMode, string[] requestedValues, string reason)
    { Multiplier = multiplier; TestMode = testMode; RequestedValues = requestedValues; Reason = reason; }
    internal bool Active => Multiplier == 1.2f;
}

internal static class DecorDensityPolicy
{
    internal const string Flag = "-azeroth-decor-density=";
    internal static DecorDensitySettings Parse(IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        bool test = args.Contains("-azeroth-test-menu", StringComparer.Ordinal) ||
                    args.Contains("-azeroth-test-battle", StringComparer.Ordinal);
        var requested = args.Where(x => x.StartsWith(Flag, StringComparison.Ordinal)).Select(x => x[Flag.Length..]).ToArray();
        if (requested.Length == 0) return new(1f, test, requested, "NoDensityFlag");
        if (!test) return new(1f, false, requested, "TestMenuOrBattleRequired");
        if (requested.Length != 1) return new(1f, true, requested, "AmbiguousDensityFlags");
        if (!float.TryParse(requested[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float density) || density != 1.2f)
            return new(1f, true, requested, "OnlyExplicitTestDensity1.2Supported");
        return new(1.2f, true, requested, "ExplicitTestDensity1.2");
    }
}
