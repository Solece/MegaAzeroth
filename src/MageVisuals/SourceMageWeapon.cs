using System.Text.Json;
using BepInEx;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

internal static class SourceMageWeapon
{
    internal const int ItemId=35,DisplayId=472,AttachmentId=1;
    internal const string ModelPath=@"Item\ObjectComponents\Weapon\Stave_2H_Long_A_01.m2";
    static Model model;
    static GameObject root,visual;
    static MeshFilter filter;
    static MeshRenderer renderer;
    static int loads;
    static string clip="";
    static int frame=-1;
    static float nextFailureLog;
    internal static bool AssetsValid=>model!=null && model.IsUsable();
    internal static object AssetHealth()=>model?.Inspect();
    static Model GetModel()
    {
        if(model!=null && model.IsUsable())return model;
        var folder=Path.Combine(Paths.GameRootPath,"AzerothMageAssets","human-male-weapon");
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"model.json")));
        var source=doc.RootElement.GetProperty("source");
        if(source.GetProperty("itemId").GetInt32()!=ItemId || source.GetProperty("displayId").GetInt32()!=DisplayId || source.GetProperty("attachmentId").GetInt32()!=AttachmentId || source.GetProperty("model").GetString()!=ModelPath)
            throw new InvalidDataException("Mage starter weapon does not match installed source outfit evidence");
        model?.Release();model=Model.Load(folder);loads++;return model;
    }
    internal static void Bind(GameObject mage)
    {
        ClearInstance();root=mage;
        try
        {
            GetModel();visual=new GameObject("AzerothMage_SourceStarterStaff");visual.layer=mage.layer;visual.transform.SetParent(mage.transform,false);
            filter=visual.AddComponent<MeshFilter>();filter.sharedMesh=model.Template;
            renderer=visual.AddComponent<MeshRenderer>();renderer.sharedMaterial=model.Material;
            Plugin.Logger.LogInfo("Original Mage starter weapon attached: item35/display472, original HumanMale hand attachment1/bone104, 52 vertices; source staff mesh/texture, no native Megabonk weapon.");
        }
        catch(Exception ex){if(Time.unscaledTime>=nextFailureLog){nextFailureLog=Time.unscaledTime+5;Plugin.Logger.LogWarning("Original Mage starter weapon is unavailable; no substitute weapon is drawn: "+ex);}}
    }
    internal static void SetFrame(string name,int index)
    {
        if(root==null)return;
        if(model==null || visual==null || filter==null || renderer==null || !model.IsUsable(false)){var owner=root;Bind(owner);}
        if(model==null || filter==null || !model.Clips.TryGetValue(name,out var sequence))return;
        int selected=Math.Clamp(index,0,sequence.Count-1);
        if(sequence.Meshes[selected]==null){var owner=root;Bind(owner);return;}
        if(clip!=name || frame!=selected){filter.sharedMesh=sequence.Meshes[selected];clip=name;frame=selected;}
    }
    internal static string Describe()
    {
        var native=NativeWeaponVisuals.Describe();
        return JsonSerializer.Serialize(new{sourceItemId=ItemId,sourceDisplayId=DisplayId,sourceModelPath=ModelPath,sourceAttachmentId=AttachmentId,sourceAttachmentBone=104,attached=visual!=null && filter!=null && renderer!=null,active=visual!=null && visual.activeInHierarchy && renderer!=null && renderer.enabled,sourceAssetsValid=AssetsValid,sourceVertexCount=model?.VertexCount??0,modelLoads=loads,currentClip=clip,currentFrame=frame,visibleNativeWeaponRenderers=NativeWeaponVisuals.VisibleCount,enabledNativeWeaponRenderers=NativeWeaponVisuals.EnabledCount,nativeWeaponRendererCount=NativeWeaponVisuals.Count,nativeWeaponRenderers=native});
    }
    internal static void ClearInstance()
    {
        if(visual!=null){visual.SetActive(false);Object.Destroy(visual);}visual=null;root=null;filter=null;renderer=null;clip="";frame=-1;
    }
    internal static void Shutdown(){ClearInstance();model?.Release();model=null;}
}
