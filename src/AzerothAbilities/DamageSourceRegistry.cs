using System.Text.Json;

namespace AzerothAbilities;

internal static class DamageSourceRegistry
{
    internal sealed class Source
    {public string SourceId{get;init;}public string Name{get;init;}public string Icon{get;init;}public int Rank{get;init;}public int SourceKind{get;init;}}
    static readonly Dictionary<string,Source> sources=new();
    internal static void Register(string id,LearnedSpell spell)
    {
        if(spell==null)return;
        string icon=spell.Definition.Icon;
        if(string.IsNullOrEmpty(icon))icon=AbilityRuntime.Catalog?.SourceClass?.Icon??"";
        if(string.IsNullOrEmpty(icon))icon="INV_Staff_13"; // Original source Mage class icon for legacy-only synthetic sources.
        sources[id]=new Source{SourceId=id,Name=string.IsNullOrEmpty(spell.Definition.Name)?spell.Definition.Id:spell.Definition.Name,Icon=icon,Rank=spell.Rank,SourceKind=spell.Definition.SourceKind};
    }
    internal static void Clear()=>sources.Clear();
    internal static string Json=>JsonSerializer.Serialize(sources.Values.OrderBy(s=>s.SourceId).ToArray());
}
