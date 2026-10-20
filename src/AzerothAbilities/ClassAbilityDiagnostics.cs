using System.Text.Json;
using UnityEngine;

namespace AzerothAbilities;

public static class ClassAbilityDiagnostics
{
    public static string ActiveClassId=>ClassCombat.ClassId;
    public static bool UsesGenericRuntime=>ClassCombat.GenericActive;
    public static string DamageSourcesJson=>DamageSourceRegistry.Json;
    public static string ActiveSourceCatalogJson=>JsonSerializer.Serialize(AbilityRuntime.Catalog);
    public static string TelemetryJson=>JsonSerializer.Serialize(new{ClassCombat.ClassId,AbilityRuntime.CastsBySpell,AbilityRuntime.HitsBySpell,AbilityRuntime.DamageBySpell});
    public static string SnapshotJson=>ClassCombat.SnapshotJson;
    public static string DispatchCoverageJson=>JsonSerializer.Serialize(new{
        SourceClasses=ClassCatalogs.All.Count,SourceClassSpells=ClassCatalogs.All.Values.Sum(c=>c.Spells.Count),
        RuntimeVerified=false,
        Note="This catalog records implemented dispatch paths; actual Unity/native gameplay must be verified by an explicitly armed root integration run.",
        SelectionAdapter="Source numeric values and event semantics; Unity boss/elite/distance/coverage targeting is a cross-engine adapter. Original source priority comparator is not yet proven identical.",
        CastKinds=ClassCatalogs.AllSpells().Select(s=>s.CastKindId).Distinct().OrderBy(k=>k).ToArray(),
        StatusKinds=ClassCatalogs.AllSpells().Select(s=>s.StatusKind).Distinct().OrderBy(k=>k).ToArray(),
        Spells=ClassCatalogs.AllSpells().Select(s=>new{s.Id,s.SourceSpellId,s.SourceKind,s.Kind,s.CastKindId,s.StatusKind,s.SourceTargetingWord,Passive=s.SourcePassive?.Kind,Implemented=true,RuntimeVerified=false}).ToArray()
    });
    public static bool LearnAllForProbe()
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1"||!AbilityRuntime.Active)return false;
        foreach(var definition in AbilityRuntime.Catalog.Spells)if(!AbilityRuntime.Spells.Any(s=>s.Definition.Id==definition.Id))AbilityRuntime.Spells.Add(new LearnedSpell(definition));return true;
    }
    public static bool ClearEffectsForProbe()
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1"||!AbilityRuntime.Active)return false;
        foreach(var projectile in AbilityRuntime.Projectiles)projectile.Dispose();
        AbilityRuntime.Projectiles.Clear();MageSpellBehaviors.Clear();SpellVfx.Clear();ClassCombat.Reset();
        return true;
    }
    public static bool EnableGenericMageForProbe(bool enabled)
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return false;
        Plugin.Current.GenericMage.Value=enabled;ClassCatalogs.Refresh();return true;
    }
}
