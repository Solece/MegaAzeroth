using System.Reflection;
using System.Text.Json;
using BepInEx;
using UnityEngine;

namespace AzerothMegabonk.MageVisuals;

// Class-aware source packs share the already-tested renderer, native movement,
// animation clock, asset retention and offscreen portrait pipeline.
internal static class HeroAppearance
{
    internal static readonly string[] Classes={"warrior","paladin","hunter","rogue","priest","shaman","mage","warlock","druid"};
    static readonly Dictionary<string,Model> Models=new();
    static Type roster;
    static float nextRosterSearch;
    internal static string ProbeClassOverride;
    internal static int? ProbeFormOverride;
    internal static string CurrentClassId="mage";
    internal static int CurrentFormSpellId;
    internal static string CurrentVisualKey="mage";
    internal static GameObject ActiveVisual;
    internal static string ResolveClassId()
    {
        if(ProbeClassOverride!=null)return ProbeClassOverride;
        if(roster==null && Time.unscaledTime>=nextRosterSearch)
        {
            nextRosterSearch=Time.unscaledTime+1;
            roster=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHeroes.HeroBridge",false)).FirstOrDefault(t=>t!=null);
        }
        if(roster!=null)
            foreach(string name in new[]{"ActiveClassId","SelectedClassId"})
            {
                string id=roster.GetProperty(name,BindingFlags.Static|BindingFlags.Public)?.GetValue(null)?.ToString();
                if(id!=null && Classes.Contains(id))return id;
            }
        return "mage";
    }
    internal static bool HasActiveSourceClass
    {
        get
        {
            if(ProbeClassOverride!=null)return true;
            ResolveClassId();string active=roster?.GetProperty("ActiveClassId",BindingFlags.Static|BindingFlags.Public)?.GetValue(null)?.ToString();
            return active!=null && Classes.Contains(active);
        }
    }
    internal static int ResolveFormSpellId()
    {
        if(ResolveClassId()!="druid")return 0;
        int id=ProbeFormOverride??(roster==null?0:Convert.ToInt32(roster.GetProperty("ActiveFormSpellId",BindingFlags.Static|BindingFlags.Public)?.GetValue(null)??0));
        return id is 9634 or 768?id:0;
    }
    internal static string ResolveVisualKey()
    {
        string id=ResolveClassId();int form=ResolveFormSpellId();return form==0?id:id+":"+form;
    }
    internal static string Folder(string classId)
    {
        var folder=Path.Combine(Paths.GameRootPath,"AzerothHeroAssets",classId,"body");
        if(File.Exists(Path.Combine(folder,"model.json")))return folder;
        if(classId=="mage")return Path.Combine(Paths.GameRootPath,"AzerothMageAssets","human-male");
        throw new FileNotFoundException("Original source hero pack is missing; native appearance is retained",folder);
    }
    internal static Model GetModel(string classId)
    {
        if(Models.TryGetValue(classId,out var model) && model.IsUsable())return model;
        model?.Release();model=Model.Load(Folder(classId));Models[classId]=model;MageVisualBridge.ModelLoadCount++;return model;
    }
    internal static Model SelectCurrent()
    {
        var id=ResolveClassId();int form=ResolveFormSpellId();var model=form==0?GetModel(id):CreatureVisualBridge.GetModel(form==9634?"bear":"cat");CurrentClassId=id;CurrentFormSpellId=form;CurrentVisualKey=ResolveVisualKey();Plugin.Model=model;return model;
    }
    internal static float SourceHeightForNativeFit=>CurrentFormSpellId==0?Plugin.Model.Template.bounds.size.y:GetModel(CurrentClassId).Template.bounds.size.y;
    internal static float FormDisplayScale=>CurrentFormSpellId==768?.9f:1f;
    internal static bool AllCachedAssetsValid=>Models.Count>0 && Models.Values.All(m=>m.IsUsable());
    internal static object Diagnostics()=>new{currentClassId=CurrentClassId,resolvedClassId=ResolveClassId(),currentFormSpellId=CurrentFormSpellId,resolvedFormSpellId=ResolveFormSpellId(),currentVisualKey=CurrentVisualKey,probeClassOverride=ProbeClassOverride,
        cachedClasses=Models.Keys.ToArray(),allCachedAssetsValid=AllCachedAssetsValid,models=Models.Select(x=>new{classId=x.Key,health=x.Value.Inspect()}).ToArray()};
    internal static bool PrimeAll()
    {
        foreach(string id in Classes)GetModel(id);
        return Models.Count==Classes.Length && AllCachedAssetsValid;
    }
    internal static void Shutdown()
    {
        foreach(var model in Models.Values)model.Release();Models.Clear();Plugin.Model=null;ActiveVisual=null;
    }
}
