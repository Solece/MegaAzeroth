using System.Text.Json;
using BepInEx;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

// All actual starter equipment slots are baked against the source body's own
// attachment bone. No native Megabonk weapon renderer is reused.
internal static class SourceMageWeapon
{
    sealed class Part
    {
        internal string Folder,Role,ClassId,SourcePath;
        internal int ItemId,DisplayId,AttachmentId,AttachmentBone;
        internal Model Model;
    }
    sealed class Instance
    {
        internal Part Part;
        internal GameObject Visual;
        internal MeshFilter Filter;
        internal MeshRenderer Renderer;
        internal string Clip="";
        internal int Frame=-1;
    }
    static readonly Dictionary<string,Part[]> Parts=new();
    static readonly List<Instance> Current=new();
    static GameObject root;
    static int loads;
    static string currentClassId="mage";
    static IEnumerable<Part> AllParts(){foreach(var parts in Parts.Values)foreach(var part in parts)yield return part;}
    internal static bool AssetsValid=>(HeroAppearance.CurrentFormSpellId!=0 || Parts.Count>0) && AllParts().All(p=>p.Model!=null && p.Model.IsUsable());
    internal static object AssetHealth()=>Primary()?.Part.Model.Inspect();
    static Instance Primary()=>Current.FirstOrDefault(x=>currentClassId=="hunter"?x.Part.Role=="ranged":x.Part.Role=="mainhand")??Current.FirstOrDefault();
    static Part Load(string classId,string folder,string role,int expectedItem)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"model.json")));var source=doc.RootElement.GetProperty("source");
        int item=source.GetProperty("itemId").GetInt32();if(item<=0 || (expectedItem>0 && item!=expectedItem))throw new InvalidDataException("Source hero equipment identity mismatch");
        int attachment=source.GetProperty("attachmentId").GetInt32();int bone=source.GetProperty("attachmentBone").GetInt32();
        var result=new Part{ClassId=classId,Folder=folder,Role=role,ItemId=item,DisplayId=source.GetProperty("displayId").GetInt32(),AttachmentId=attachment,AttachmentBone=bone,SourcePath=source.GetProperty("model").GetString(),Model=Model.Load(folder)};loads++;return result;
    }
    static Part[] GetParts(string classId)
    {
        if(Parts.TryGetValue(classId,out var parts) && parts.All(p=>p.Model.IsUsable()))return parts;
        if(parts!=null)foreach(var p in parts)p.Model?.Release();
        var directory=Path.Combine(Paths.GameRootPath,"AzerothHeroAssets",classId);var manifest=Path.Combine(directory,"equipment.json");
        if(!File.Exists(manifest))
        {
            if(classId!="mage")throw new FileNotFoundException("Original hero equipment manifest is missing",manifest);
            parts=new[]{Load(classId,Path.Combine(Paths.GameRootPath,"AzerothMageAssets","human-male-weapon"),"mainhand",35)};
        }
        else
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(manifest));var result=new List<Part>();
            try
            {
                foreach(var row in doc.RootElement.EnumerateArray())
                {
                    string relative=row.GetProperty("folder").GetString();string folder=Path.GetFullPath(Path.Combine(directory,relative));
                    if(!folder.StartsWith(Path.GetFullPath(directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Hero equipment path leaves its source pack");
                    result.Add(Load(classId,folder,row.GetProperty("visualRole").GetString(),row.GetProperty("itemId").GetInt32()));
                }
                parts=result.ToArray();
            }
            catch{foreach(var part in result)part.Model.Release();throw;}
        }
        Parts[classId]=parts;return parts;
    }
    internal static void Bind(GameObject hero)
    {
        ClearInstance();root=hero;currentClassId=HeroAppearance.CurrentClassId;
        foreach(var part in GetParts(currentClassId))
        {
            var go=new GameObject("AzerothHero_SourceEquipment_"+part.ItemId);go.layer=hero.layer;go.transform.SetParent(hero.transform,false);
            var instance=new Instance{Part=part,Visual=go,Filter=go.AddComponent<MeshFilter>(),Renderer=go.AddComponent<MeshRenderer>()};
            instance.Filter.sharedMesh=part.Model.Template;instance.Renderer.sharedMaterial=part.Model.Material;Current.Add(instance);
        }
        Plugin.Logger.LogInfo($"Original {currentClassId} starter equipment attached: {string.Join(", ",Current.Select(p=>p.Part.ItemId+"/"+p.Part.DisplayId+"@"+p.Part.AttachmentId))}; source geometry/texture/bone frames, native weapons remain isolated.");
    }
    internal static void SetFrame(string name,int index)
    {
        if(root==null)return;
        if(Current.Any(x=>x.Visual==null || x.Filter==null || x.Renderer==null || !x.Part.Model.IsUsable(false))){var owner=root;Bind(owner);}
        foreach(var item in Current)
        {
            if(!item.Part.Model.Clips.TryGetValue(name,out var clip))throw new InvalidDataException("Source equipment clip is missing: "+item.Part.ClassId+"/"+item.Part.ItemId+"/"+name);
            int selected=Math.Clamp(index,0,clip.Count-1);if(clip.Meshes[selected]==null)throw new InvalidOperationException("Source equipment frame was unloaded");
            if(item.Clip!=name || item.Frame!=selected){item.Filter.sharedMesh=clip.Meshes[selected];item.Clip=name;item.Frame=selected;}
        }
    }
    internal static bool FrameMatches(string name,int index)=>HeroAppearance.CurrentFormSpellId!=0?Current.Count==0:Current.Count>0 && Current.All(x=>x.Visual!=null && x.Renderer!=null && x.Renderer.enabled && x.Clip==name && x.Frame==index);
    internal static string Describe()
    {
        var p=Primary();return JsonSerializer.Serialize(new{classId=currentClassId,sourceItemId=p?.Part.ItemId??0,sourceDisplayId=p?.Part.DisplayId??0,sourceModelPath=p?.Part.SourcePath,
            sourceAttachmentId=p?.Part.AttachmentId??0,sourceAttachmentBone=p?.Part.AttachmentBone??0,attached=Current.Count>0 && Current.All(x=>x.Visual!=null && x.Filter!=null && x.Renderer!=null),
            active=Current.Count>0 && Current.All(x=>x.Visual!=null && x.Visual.activeInHierarchy && x.Renderer!=null && x.Renderer.enabled),sourceAssetsValid=AssetsValid,sourceVertexCount=p?.Part.Model.VertexCount??0,modelLoads=loads,currentClip=p?.Clip,currentFrame=p?.Frame??-1,
            equipment=Current.Select(x=>new{itemId=x.Part.ItemId,displayId=x.Part.DisplayId,role=x.Part.Role,attachmentId=x.Part.AttachmentId,attachmentBone=x.Part.AttachmentBone,currentClip=x.Clip,currentFrame=x.Frame,sourceVertexCount=x.Part.Model.VertexCount}).ToArray(),
            visibleNativeWeaponRenderers=NativeWeaponVisuals.VisibleCount,enabledNativeWeaponRenderers=NativeWeaponVisuals.EnabledCount,nativeWeaponRendererCount=NativeWeaponVisuals.Count,nativeWeaponRenderers=NativeWeaponVisuals.Describe()});
    }
    internal static bool PrimeAllForProbe(){foreach(string id in HeroAppearance.Classes)GetParts(id);return AssetsValid;}
    internal static void ClearInstance()
    {
        foreach(var item in Current)if(item.Visual!=null){item.Visual.SetActive(false);Object.Destroy(item.Visual);}Current.Clear();root=null;
    }
    internal static void Shutdown(){ClearInstance();foreach(var p in AllParts())p.Model.Release();Parts.Clear();}
}
