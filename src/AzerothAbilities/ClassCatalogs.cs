using System.Reflection;
using System.Text.Json;

namespace AzerothAbilities;

internal static class ClassCatalogs
{
    internal static readonly string[] ClassIds={"warrior","paladin","hunter","rogue","priest","shaman","mage","warlock","druid"};
    internal static readonly Dictionary<string,SpellCatalog> All=new(StringComparer.Ordinal);
    internal static readonly Dictionary<string,SpellDefinition> Auxiliary=new(StringComparer.Ordinal);
    internal static readonly Dictionary<int,SourceRace> Races=new();
    static Type hero;
    internal static Type Hero=>hero??=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHeroes.HeroBridge",false)).FirstOrDefault(t=>t!=null);
    internal static bool HasHeroRoster=>Hero!=null;
    static string preparingClass;
    internal static string ActiveClass=>preparingClass??(HasHeroRoster?(string)(Hero.GetProperty("ActiveClassId")?.GetValue(null)??""):Plugin.Current?.SelectedClass.Value??"none");
    internal static int ActiveRaceId
    {
        get
        {
            if(!HasHeroRoster)return 0;
            if(preparingClass!=null)
            {string race=(string)(Hero.GetProperty("SelectedRaceId")?.GetValue(null)??"");return race switch{"human"=>1,"orc"=>2,"dwarf"=>3,"night_elf"=>4,_=>0};}
            return (int)(Hero.GetProperty("SourceRaceId")?.GetValue(null)??0);
        }
    }
    internal static void PrepareNativeInventory()
    {
        string id=HasHeroRoster?(string)(Hero.GetProperty("SelectedClassId")?.GetValue(null)??""):Plugin.Current?.SelectedClass.Value??"";
        if(!ClassIds.Contains(id))return;preparingClass=id;Refresh();
    }
    internal static void FinishNativeInventory()
    {
        if(HasHeroRoster && (string)(Hero.GetProperty("ActiveClassId")?.GetValue(null)??"")==preparingClass)preparingClass=null;
    }
    internal static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true};
    internal static void Load(string directory)
    {
        foreach(var id in ClassIds)All[id]=SpellCatalog.Load(Path.Combine(directory,id+".json"));
        foreach(var pair in JsonSerializer.Deserialize<Dictionary<string,SpellDefinition>>(File.ReadAllText(Path.Combine(directory,"auxiliary-spells.json")),Options))Auxiliary[pair.Key]=pair.Value;
        using var classes=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"classes.json")));
        foreach(var row in classes.RootElement.GetProperty("races").EnumerateArray())
        {
            var race=new SourceRace{Id=row.GetProperty("sourceRaceId").GetInt32(),Name=row.GetProperty("name").GetString(),RacialSpell=row.GetProperty("racialSpell").GetString()};
            foreach(var bonus in row.GetProperty("bonuses").EnumerateArray())race.Bonuses[bonus.GetProperty("kind").GetInt32()]=bonus.GetProperty("value").GetSingle();
            Races[race.Id]=race;
        }
        if(All.Count!=9 || All.Values.Sum(c=>c.Spells.Count)!=102)throw new InvalidDataException("Source catalogs must contain exactly nine classes and 102 class spells");
    }
    internal static bool Refresh()
    {
        string id=ActiveClass;if(!ClassIds.Contains(id))return false;
        bool legacy=id=="mage" && Plugin.Current.GenericMage.Value==false;
        var wanted=legacy?Plugin.Current.LegacyMage:All[id];
        if(ReferenceEquals(AbilityRuntime.Catalog,wanted))return false;
        AbilityRuntime.Clear();AbilityRuntime.Initialize(wanted);AbilityRuntime.ResetRun();return true;
    }
    internal static List<SpellDefinition> AllSpells(){var result=new List<SpellDefinition>();foreach(var catalog in All.Values)result.AddRange(catalog.Spells);return result;}
    internal static SpellDefinition Definition(string id)=>AbilityRuntime.Catalog?.Spells.FirstOrDefault(s=>s.Id==id)??(Auxiliary.TryGetValue(id,out var found)?found:null);
}
internal sealed class SourceRace
{
    internal int Id;internal string Name,RacialSpell;
    internal readonly Dictionary<int,float> Bonuses=new();
    internal float Bonus(int kind)=>Bonuses.TryGetValue(kind,out float value)?value:0;
}
