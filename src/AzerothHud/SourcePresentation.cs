using System.Reflection;
namespace AzerothHud;
static class SourcePresentation
{
    internal static string ClassId="mage";
    static readonly Dictionary<string,string> Names=new(){["warrior"]="Воин",["paladin"]="Паладин",["hunter"]="Охотник",["rogue"]="Разбойник",["priest"]="Жрец",["shaman"]="Шаман",["mage"]="Маг",["warlock"]="Чернокнижник",["druid"]="Друид"};
    internal static bool IsSourceClass=>Names.ContainsKey(ClassId);
    internal static string ClassName=>Names.TryGetValue(ClassId,out var name)?name:ClassId;
    internal static UnityEngine.Texture StaticPortrait
    {
        get
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHeroes.HeroBridge",false)).FirstOrDefault(t=>t!=null);
            return type?.GetMethod("GetClassPortrait",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{ClassId}) as UnityEngine.Texture;
        }
    }
    internal static bool PortraitReady
    {
        get
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHeroes.HeroVisualBridge",false)).FirstOrDefault(t=>t!=null);
            return type==null?ClassId=="mage":type.GetProperty("ActiveClassId")?.GetValue(null) as string==ClassId && type.GetProperty("IsAttached")?.GetValue(null) is true;
        }
    }
}
