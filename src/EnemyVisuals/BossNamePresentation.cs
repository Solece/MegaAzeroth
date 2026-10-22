using System.Text.Json;
using Assets.Scripts.Actors.Enemies;
using BepInEx;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.Localization.Settings;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.EnemyVisuals;

// Names belong to an actual source visual and one native HUD binding. Neither
// EnemyData, its localized reference, enum, AI nor the enemy log is modified.
internal static class BossNamePresentation
{
    internal sealed class Entry
    {
        internal string Key,En,Ru;
        internal int DisplayId;
        internal JsonElement Evidence;
        internal string Name(string locale)=>locale.StartsWith("ru",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(Ru)?Ru:En;
    }
    sealed class Binding
    {
        internal TargetOfInterestPrefab Ui;
        internal long ActorPointer,DataPointer;
        internal uint ActorId;
        internal string NativeName="",Locale="",Written="",Key="";
        internal bool Applied;
        internal float NextSync;
    }
    static readonly Dictionary<int,Binding> Bindings=new();
    static readonly Dictionary<string,Entry> Catalog=new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<long,(uint Id,long Data,AzerothVisual Visual)> ProbeRestoredOwners=new();
    static bool enabled=true;
    static float nextCatalogCheck;
    static string catalogStamp="",error="";
    static int writes,restores,catalogLoads,failures;
    static string CatalogPath=>Path.Combine(Paths.GameRootPath,"AzerothAssets","boss-names.json");

    internal static void Initialize(){enabled=true;nextCatalogCheck=0;catalogStamp="";error="";}
    internal static bool IsBoss(Enemy enemy)=>enemy!=null&&(enemy.IsBoss()||enemy.IsStageBoss()||enemy.IsFinalBoss());
    static string LocaleCode()
    {
        // This is the native Unity Localization locale, not a stored preference
        // that may lag an in-session language change. No locale is set here.
        var locale=LocalizationSettings.SelectedLocale;
        return locale!=null?locale.Identifier.Code??"en":"en";
    }
    static bool Same(Binding b,Enemy e)=>e!=null&&e.Pointer.ToInt64()==b.ActorPointer&&e.id==b.ActorId&&e.enemyData!=null&&e.enemyData.Pointer.ToInt64()==b.DataPointer;
    static string NativeName(Enemy enemy,string fallback)
    {
        try{return enemy?.enemyData!=null?enemy.enemyData.GetName():fallback;}
        catch{return fallback;}
    }
    static void Report(Exception ex)
    {
        failures++;string next=ex.GetType().Name+": "+ex.Message;
        if(next!=error){error=next;Plugin.Logger?.LogWarning("Source boss caption retained/restored native text: "+next);}
    }
    static void LoadCatalog()
    {
        if(Time.unscaledTime<nextCatalogCheck)return;
        nextCatalogCheck=Time.unscaledTime+1;
        var info=new FileInfo(CatalogPath);
        string stamp=info.Exists?info.Length+":"+info.LastWriteTimeUtc.Ticks:"missing";
        if(stamp==catalogStamp)return;
        catalogStamp=stamp;
        try
        {
            if(!info.Exists){Catalog.Clear();return;}
            using var document=JsonDocument.Parse(File.ReadAllText(CatalogPath));
            var root=document.RootElement;
            if(root.GetProperty("format").GetString()!="azeroth-boss-names-v1")throw new InvalidDataException("Unsupported boss-name catalog format");
            var parsed=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
            foreach(var row in root.GetProperty("entries").EnumerateArray())
            {
                var e=new Entry{Key=row.GetProperty("modelKey").GetString(),En=row.GetProperty("en").GetString(),Ru=row.GetProperty("ru").GetString(),DisplayId=row.GetProperty("sourceDisplayId").GetInt32(),Evidence=row.GetProperty("evidence").Clone()};
                if(string.IsNullOrWhiteSpace(e.Key)||string.IsNullOrWhiteSpace(e.En)||e.Evidence.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Incomplete boss-name catalog entry");
                if(!parsed.TryAdd(e.Key,e))throw new InvalidDataException("Duplicate boss-name model key: "+e.Key);
            }
            Catalog.Clear();foreach(var pair in parsed)Catalog.Add(pair.Key,pair.Value);
            catalogLoads++;error="";
        }
        catch{Catalog.Clear();throw;}
    }
    internal static void BeforeSet(TargetOfInterestPrefab ui,Enemy next)
    {
        try
        {
            if(ui==null)return;
            if(Bindings.TryGetValue(ui.GetInstanceID(),out var b)&&!Same(b,next))Remove(ui,true);
        }
        catch(Exception e){Report(e);}
    }
    internal static void Sync(TargetOfInterestPrefab ui,bool force=false)
    {
        if(!enabled||ui==null)return;
        Binding b=null;
        try
        {
            int id=ui.GetInstanceID();var enemy=ui.enemy;
            if(!IsBoss(enemy)||ui.t_name==null){Remove(ui,true);return;}
            if(Bindings.TryGetValue(id,out b)&&!Same(b,enemy)){Remove(ui,true);b=null;}
            if(b==null)
            {
                b=new Binding{Ui=ui,ActorPointer=enemy.Pointer.ToInt64(),ActorId=enemy.id,DataPointer=enemy.enemyData.Pointer.ToInt64(),NativeName=NativeName(enemy,ui.t_name.text)};
                Bindings[id]=b;
            }
            if(!force&&Time.unscaledTime<b.NextSync)return;
            b.NextSync=Time.unscaledTime+.1f;
            LoadCatalog();string locale=LocaleCode();
            if(b.Locale!=locale){b.Locale=locale;b.NativeName=NativeName(enemy,b.NativeName);}
            var visual=EnemyVisualOwnership.Find(enemy);
            if(visual==null||!visual.TryBossNameModel(enemy,out var key)||!Catalog.TryGetValue(key,out var entry))
            {Restore(b);return;}
            string desired=entry.Name(locale);
            if(string.IsNullOrWhiteSpace(desired)){Restore(b);return;}
            if(ui.t_name.text!=desired){ui.t_name.text=desired;writes++;}
            b.Key=key;b.Written=desired;b.Applied=true;
        }
        catch(Exception e){if(b!=null)Restore(b);Report(e);}
    }
    static void Restore(Binding b)
    {
        if(!b.Applied)return;
        try
        {
            if(b.Ui!=null&&Same(b,b.Ui.enemy)&&b.Ui.t_name!=null&&b.Ui.t_name.text==b.Written)
            {
                b.NativeName=NativeName(b.Ui.enemy,b.NativeName);
                if(b.Ui.t_name.text!=b.NativeName){b.Ui.t_name.text=b.NativeName;restores++;}
            }
        }
        catch(Exception e){Report(e);}
        b.Applied=false;b.Key="";b.Written="";
    }
    internal static void Remove(TargetOfInterestPrefab ui,bool restore)
    {
        if(ui==null)return;
        int id=ui.GetInstanceID();if(!Bindings.TryGetValue(id,out var b))return;
        if(restore)Restore(b);Bindings.Remove(id);
    }
    internal static void ReleaseEnemy(Enemy enemy)
    {
        if(enemy==null)return;long pointer=enemy.Pointer.ToInt64();
        foreach(var pair in Bindings.Where(x=>x.Value.ActorPointer==pointer).ToArray())
        {Restore(pair.Value);Bindings.Remove(pair.Key);}
    }
    internal static void RefreshEnemy(Enemy enemy)
    {
        if(enemy==null)return;long pointer=enemy.Pointer.ToInt64();
        foreach(var b in Bindings.Values.Where(x=>x.ActorPointer==pointer).ToArray())Sync(b.Ui,true);
    }
    internal static void Shutdown()
    {
        enabled=false;foreach(var b in Bindings.Values.ToArray())Restore(b);Bindings.Clear();Catalog.Clear();ProbeRestoredOwners.Clear();catalogStamp="";
    }
    internal static void RememberProbeOwner(Enemy enemy,AzerothVisual visual)
        =>ProbeRestoredOwners[enemy.Pointer.ToInt64()]=(enemy.id,enemy.enemyData.Pointer.ToInt64(),visual);
    internal static AzerothVisual TakeProbeOwner(Enemy enemy)
    {
        long pointer=enemy.Pointer.ToInt64();
        if(!ProbeRestoredOwners.Remove(pointer,out var saved))return EnemyVisualOwnership.Find(enemy);
        return saved.Id==enemy.id&&enemy.enemyData!=null&&saved.Data==enemy.enemyData.Pointer.ToInt64()?saved.Visual:null;
    }
    internal static string Diagnostics()
    {
        var rows=new List<object>();
        foreach(var ui in Resources.FindObjectsOfTypeAll<TargetOfInterestPrefab>())
        {
            try
            {
                if(ui==null||!ui.gameObject.scene.IsValid())continue;
                var enemy=ui.enemy;var text=ui.t_name;Bindings.TryGetValue(ui.GetInstanceID(),out var binding);
                string key=null;var visual=EnemyVisualOwnership.Find(enemy);if(visual!=null)visual.TryBossNameModel(enemy,out key);
                Catalog.TryGetValue(key??"",out var entry);
                float group=ui.canvasGroup!=null?ui.canvasGroup.alpha:1;
                float alpha=text!=null?text.color.a*text.canvasRenderer.GetColor().a*text.canvasRenderer.GetInheritedAlpha():0;
                bool visible=text!=null&&text.isActiveAndEnabled&&text.gameObject.activeInHierarchy&&text.canvas!=null&&text.canvas.isActiveAndEnabled&&!text.canvasRenderer.cull&&group>0&&alpha>0;
                rows.Add(new{UiId=ui.GetInstanceID(),EnemyPointer=enemy!=null?enemy.Pointer.ToInt64():0,EnemyId=enemy!=null?enemy.id:0,NativeDataPointer=enemy?.enemyData!=null?enemy.enemyData.Pointer.ToInt64():0,NativeEnum=enemy?.enemyData!=null?(int)enemy.enemyData.enemyName:-1,IsBoss=IsBoss(enemy),ActualModelKey=key,NativeName=NativeName(enemy,""),TmpText=text!=null?text.text:null,TextVisible=visible,CanvasGroupAlpha=group,TextAlpha=alpha,RenderedCharacters=text?.textInfo!=null?text.textInfo.characterCount:0,Applied=binding?.Applied??false,Locale=binding?.Locale,ExpectedText=entry!=null?entry.Name(LocaleCode()):null,SourceDisplayId=entry?.DisplayId??0,Evidence=entry!=null?(object)entry.Evidence:null});
            }
            catch(Exception e){rows.Add(new{DiagnosticError=e.GetType().Name+": "+e.Message});}
        }
        return JsonSerializer.Serialize(new{Enabled=enabled,CatalogPath,CatalogCount=Catalog.Count,CatalogLoads=catalogLoads,BindingCount=Bindings.Count,Writes=writes,Restores=restores,Failures=failures,Error=error,Rows=rows,Policy="Source visual family caption only on actual boss HUD; native EnemyData, AI, enum and enemy logs unchanged."});
    }
}

[HarmonyPatch(typeof(TargetOfInterestPrefab),nameof(TargetOfInterestPrefab.SetEnemy))]
internal static class SourceBossNameSet
{
    static void Prefix(TargetOfInterestPrefab __instance,Enemy __0)=>BossNamePresentation.BeforeSet(__instance,__0);
    static void Postfix(TargetOfInterestPrefab __instance)=>BossNamePresentation.Sync(__instance,true);
}
[HarmonyPatch(typeof(TargetOfInterestPrefab),nameof(TargetOfInterestPrefab.Update))]
internal static class SourceBossNameUpdate{static void Postfix(TargetOfInterestPrefab __instance)=>BossNamePresentation.Sync(__instance);}
[HarmonyPatch(typeof(TargetOfInterestPrefab),nameof(TargetOfInterestPrefab.OnDestroy))]
internal static class SourceBossNameDestroy{static void Prefix(TargetOfInterestPrefab __instance)=>BossNamePresentation.Remove(__instance,false);}

public sealed partial class AzerothVisual
{
    [HideFromIl2Cpp] internal bool TryBossNameModel(Enemy target,out string key)
    {
        key=null;
        if(!enabled||target==null||enemy==null||enemy.Pointer!=target.Pointer||model==null||visual==null||!visual.activeInHierarchy||replacement==null||!replacement.enabled||!model.IsUsable(false))return false;
        key=model.Key;return !string.IsNullOrEmpty(key);
    }
    [HideFromIl2Cpp] internal bool RebindBossNameVisualForProbe(Enemy target)
    {
        if(target==null||model==null||!model.IsUsable(false))return false;
        Bind(target,model,BossNamePresentation.IsBoss(target));return TryBossNameModel(target,out _);
    }
}

public static partial class EnemyVisualBridge
{
    public static string BossNameDiagnosticsJson=>BossNamePresentation.Diagnostics();
    public static bool RestoreVisualForProbe(Enemy enemy)
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return false;
        var visual=EnemyVisualOwnership.Find(enemy);if(visual==null||enemy.enemyData==null)return false;
        BossNamePresentation.RememberProbeOwner(enemy,visual);visual.RestoreNative();return true;
    }
    public static bool RebindVisualForProbe(Enemy enemy)
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1"||enemy==null)return false;
        // Retain the canonical managed controller across RestoreNative. Asking
        // IL2CPP GetComponent for a new wrapper can lose that controller's model.
        var visual=BossNamePresentation.TakeProbeOwner(enemy);return visual!=null&&visual.RebindBossNameVisualForProbe(enemy);
    }
}
