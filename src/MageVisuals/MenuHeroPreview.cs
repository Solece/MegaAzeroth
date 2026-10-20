using System.Reflection;
using System.Text.Json;
using Assets.Scripts.Actors.Player;
using BepInEx;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

// This owner is separate from the battle visual and SourceMageWeapon singleton.
// Equipment frames are already baked in source-body coordinates by the exporter.
public sealed class MenuHeroPreview:MonoBehaviour
{
    public MenuHeroPreview(IntPtr pointer):base(pointer){}
    static MenuHeroPreview active;
    CharacterDisplay display;
    PlayerRenderer native;
    Renderer nativeBody;
    Transform anchor;
    GameObject subject;
    MeshFilter bodyFilter;
    MeshRenderer bodyRenderer;
    Model bodyModel;
    Clip idle;
    Type heroBridge;
    string classId="",lastFailure="";
    float scanAt,frameAt,startedAt,retryAt,logAt,hideScanAt,nativeHeight,scale;
    int frame=-1,binds,releases,modelLoads,failures;
    readonly List<SavedRenderer> hidden=new();
    readonly List<Gear> gear=new();
    sealed class SavedRenderer {internal Renderer Renderer;internal bool Enabled;}
    sealed class Gear
    {
        internal int ItemId,AttachmentId,Frame=-1;
        internal string Role;
        internal Model Model;
        internal Clip Idle;
        internal GameObject Visual;
        internal MeshFilter Filter;
        internal MeshRenderer Renderer;
    }

    void LateUpdate()
    {
        active=this;
        if(Plugin.Unloading || MyPlayer.Instance!=null&&MyPlayer.Instance.hasStarted)
        {if(subject!=null||hidden.Count>0)ReleaseOwned();return;}
        if(Time.unscaledTime<retryAt)return;
        try
        {
            if(!CharacterMenuOpen())
            {if(subject!=null||hidden.Count>0)ReleaseOwned();return;}
            if(display==null||!display.isActiveAndEnabled||!display.gameObject.activeInHierarchy||native==null||!native.gameObject.activeInHierarchy)
            {
                if(subject!=null||hidden.Count>0)ReleaseOwned();
                if(Time.unscaledTime<scanAt)return;
                scanAt=Time.unscaledTime+.25f;
                display=FindDisplay();if(display==null)return;
            }
            var selected=SelectedClassId();
            if(selected==null){if(subject!=null)ReleaseOwned();return;}
            var playerRenderer=display.playerRenderer;
            if(playerRenderer==null||playerRenderer.renderer==null)return;
            if(subject==null||native!=playerRenderer||nativeBody!=playerRenderer.renderer||classId!=selected||anchor==null||bodyFilter==null||bodyRenderer==null)
                Bind(display,selected);
            if(subject==null)return;
            if(Time.unscaledTime>=hideScanAt)
            {
                hideScanAt=Time.unscaledTime+.5f;
                if(!bodyModel.IsUsable(false))throw new InvalidOperationException("Menu source body assets were unloaded");
                HideNative(native.transform);
            }
            foreach(var saved in hidden)if(saved.Renderer!=null)saved.Renderer.enabled=false;
            if(Time.unscaledTime<frameAt)return;
            frameAt=Time.unscaledTime+1f/24;
            ApplyIdleFrame(Time.unscaledTime-startedAt);
        }
        catch(Exception ex)
        {
            failures++;lastFailure=ex.GetType().Name+": "+ex.Message;retryAt=Time.unscaledTime+1;
            ReleaseOwned();
            if(Time.unscaledTime>=logAt){logAt=Time.unscaledTime+5;Plugin.Logger.LogWarning("Source menu preview restored native appearance and will retry: "+ex);}
        }
    }

    [HideFromIl2Cpp]
    static bool CharacterMenuOpen()
    {
        foreach(var menu in Resources.FindObjectsOfTypeAll<CharacterMenu>())
            if(menu!=null&&menu.isActiveAndEnabled&&menu.gameObject.activeInHierarchy)return true;
        return false;
    }
    [HideFromIl2Cpp]
    static CharacterDisplay FindDisplay()
    {
        foreach(var value in Resources.FindObjectsOfTypeAll<CharacterDisplay>())
            if(value!=null&&value.isActiveAndEnabled&&value.gameObject.activeInHierarchy&&value.playerRenderer!=null&&value.playerRenderer.gameObject.activeInHierarchy)return value;
        return null;
    }
    [HideFromIl2Cpp]
    string SelectedClassId()
    {
        if(heroBridge==null)
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {heroBridge=assembly.GetType("AzerothHeroes.HeroBridge",false);if(heroBridge!=null)break;}
        // An old ActiveClassId may survive the last battle; menu selection owns
        // the preview and never mutates HeroAppearance's active battle identity.
        var id=heroBridge?.GetProperty("SelectedClassId",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) as string;
        return id!=null&&HeroAppearance.Classes.Contains(id)?id:null;
    }
    [HideFromIl2Cpp]
    static Transform SpinAnchor(PlayerRenderer renderer)
    {
        foreach(var spin in Resources.FindObjectsOfTypeAll<CharacterSpin>())
        {
            if(spin==null||!spin.isActiveAndEnabled||!spin.gameObject.activeInHierarchy||spin.playerRenderer==null)continue;
            var target=spin.playerRenderer;
            if(target==renderer.transform||target.IsChildOf(renderer.transform))return target;
        }
        return renderer.transform;
    }
    [HideFromIl2Cpp]
    void Bind(CharacterDisplay target,string selected)
    {
        ReleaseOwned();display=target;native=target.playerRenderer;nativeBody=native.renderer;anchor=SpinAnchor(native);
        var bounds=nativeBody.bounds;
        if(bounds.size.y<=.01f)throw new InvalidDataException("Native menu body has no measurable height");
        bodyModel=HeroAppearance.GetModel(selected);
        if(!bodyModel.Clips.TryGetValue("idle",out idle)||idle.Count<1||idle.Duration<=0||!idle.Loop)
            throw new InvalidDataException("Original source menu idle clip is missing: "+selected);
        var sourceBounds=idle.Meshes[0].bounds;
        nativeHeight=bounds.size.y;
        scale=nativeHeight/Mathf.Max(.0001f,Mathf.Abs(anchor.lossyScale.y))/Mathf.Max(.01f,sourceBounds.size.y);
        subject=new GameObject("AzerothMenuHero_"+selected);subject.layer=nativeBody.gameObject.layer;
        subject.transform.SetParent(anchor,false);subject.transform.localRotation=Quaternion.identity;subject.transform.localScale=Vector3.one*scale;
        var feet=bounds.center-Vector3.up*(nativeHeight*.5f);
        subject.transform.localPosition=anchor.InverseTransformPoint(feet)-new Vector3(sourceBounds.center.x,sourceBounds.min.y,sourceBounds.center.z)*scale;
        bodyFilter=subject.AddComponent<MeshFilter>();bodyFilter.sharedMesh=idle.Meshes[0];
        bodyRenderer=subject.AddComponent<MeshRenderer>();bodyRenderer.sharedMaterial=bodyModel.Material;
        classId=selected;
        LoadOwnEquipment(selected);
        startedAt=Time.unscaledTime;frameAt=0;frame=-1;
        ApplyIdleFrame(0);HideNative(native.transform);hideScanAt=Time.unscaledTime+.5f;binds++;
        Plugin.Logger.LogInfo($"Original {selected} menu preview attached: idle={idle.AnimationId}, frames={idle.Count}, gear={gear.Count}, nativeHeight={nativeHeight:F3}, sourceScale={scale:F3}, hiddenNativeRenderers={hidden.Count}; native spin and battle owners retained.");
    }
    [HideFromIl2Cpp]
    void LoadOwnEquipment(string selected)
    {
        var directory=Path.Combine(Paths.GameRootPath,"AzerothHeroAssets",selected);
        var manifest=Path.Combine(directory,"equipment.json");
        if(!File.Exists(manifest))throw new FileNotFoundException("Original source menu equipment manifest missing",manifest);
        var prefix=Path.GetFullPath(directory)+Path.DirectorySeparatorChar;
        using var document=JsonDocument.Parse(File.ReadAllText(manifest));
        foreach(var row in document.RootElement.EnumerateArray())
        {
            var folder=Path.GetFullPath(Path.Combine(directory,row.GetProperty("folder").GetString()));
            if(!folder.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Source menu equipment leaves its class folder");
            var item=new Gear{ItemId=row.GetProperty("itemId").GetInt32(),AttachmentId=row.GetProperty("attachmentId").GetInt32(),Role=row.GetProperty("visualRole").GetString()};
            gear.Add(item); // Retain ownership even if loading/validation throws.
            using var metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"model.json")));
            var source=metadata.RootElement.GetProperty("source");
            if(source.GetProperty("itemId").GetInt32()!=item.ItemId||source.GetProperty("attachmentId").GetInt32()!=item.AttachmentId)
                throw new InvalidDataException("Original source menu equipment identity mismatch");
            item.Model=Model.Load(folder);modelLoads++;
            if(!item.Model.Clips.TryGetValue("idle",out item.Idle)||item.Idle.AnimationId!=idle.AnimationId||item.Idle.Count!=idle.Count||Mathf.Abs(item.Idle.Duration-idle.Duration)>.0001f)
                throw new InvalidDataException("Original source body/equipment idle frame mismatch: "+selected+"/"+item.ItemId);
            item.Visual=new GameObject("AzerothMenuEquipment_"+item.ItemId);item.Visual.layer=subject.layer;
            item.Visual.transform.SetParent(subject.transform,false);
            // Exported attachment matrices already place these vertices in the
            // body coordinate system. A second attachment transform is incorrect.
            item.Filter=item.Visual.AddComponent<MeshFilter>();item.Filter.sharedMesh=item.Idle.Meshes[0];
            item.Renderer=item.Visual.AddComponent<MeshRenderer>();item.Renderer.sharedMaterial=item.Model.Material;
        }
    }
    [HideFromIl2Cpp]
    void ApplyIdleFrame(float elapsed)
    {
        int selected=Math.Clamp((int)((elapsed%idle.Duration)/idle.Duration*(idle.Count-1)),0,idle.Count-1);
        if(selected==frame)return;
        var mesh=idle.Meshes[selected];if(mesh==null)throw new InvalidOperationException("Original source menu idle frame unloaded");
        bodyFilter.sharedMesh=mesh;
        foreach(var item in gear)
        {
            if(item.Visual==null||item.Filter==null||item.Renderer==null||item.Model.Material==null||item.Idle.Meshes[selected]==null)
                throw new InvalidOperationException("Original source menu equipment was unloaded: "+item.ItemId);
            item.Filter.sharedMesh=item.Idle.Meshes[selected];item.Frame=selected;
        }
        frame=selected;
    }
    [HideFromIl2Cpp]
    void HideNative(Transform node)
    {
        if(node==null||node==subject.transform||node.name.StartsWith("Azeroth",StringComparison.Ordinal))return;
        var renderer=node.GetComponent<Renderer>();
        if(renderer!=null)
        {
            bool known=false;foreach(var saved in hidden)if(saved.Renderer==renderer){known=true;break;}
            if(!known)hidden.Add(new SavedRenderer{Renderer=renderer,Enabled=renderer.enabled});
            renderer.enabled=false;
        }
        for(int i=0;i<node.childCount;i++)HideNative(node.GetChild(i));
    }
    [HideFromIl2Cpp]
    void ReleaseOwned()
    {
        bool attached=subject!=null||hidden.Count>0||gear.Count>0;
        // Restore only the renderer states captured by this menu owner.
        foreach(var saved in hidden)if(saved.Renderer!=null)saved.Renderer.enabled=saved.Enabled;
        hidden.Clear();
        if(subject!=null){subject.SetActive(false);Object.Destroy(subject);}
        foreach(var item in gear)item.Model?.Release();gear.Clear();
        // Body assets belong to HeroAppearance's shared retained cache. This
        // owner must not release them or shut down battle equipment/portraits.
        subject=null;bodyFilter=null;bodyRenderer=null;bodyModel=null;idle=null;native=null;nativeBody=null;anchor=null;display=null;classId="";frame=-1;
        if(attached)releases++;
    }
    [HideFromIl2Cpp]
    internal static void Shutdown()
    {var owner=active;active=null;if(owner!=null){owner.ReleaseOwned();owner.enabled=false;}}
    [HideFromIl2Cpp]
    internal static string Diagnostics()
    {
        var owner=active;if(owner==null)return "{\"attached\":false,\"ownerPresent\":false}";
        var equipment=new List<object>();bool assets=owner.bodyModel!=null&&owner.bodyModel.IsUsable(false);
        foreach(var item in owner.gear)
        {
            bool valid=item.Model!=null&&item.Model.IsUsable(false)&&item.Visual!=null&&item.Renderer!=null&&item.Renderer.enabled;
            assets&=valid;
            equipment.Add(new{itemId=item.ItemId,attachmentId=item.AttachmentId,role=item.Role,frame=item.Frame,sourceAssetsValid=valid});
        }
        int enabledNative=0;foreach(var saved in owner.hidden)if(saved.Renderer!=null&&saved.Renderer.enabled)enabledNative++;
        var center=owner.bodyRenderer!=null?owner.bodyRenderer.bounds.center:Vector3.zero;
        return JsonSerializer.Serialize(new{ownerPresent=true,attached=owner.subject!=null&&owner.subject.activeInHierarchy,classId=owner.classId,
            selectedClassId=owner.SelectedClassId(),nativeHeight=owner.nativeHeight,sourceScale=owner.scale,sourceHeight=owner.bodyRenderer!=null?owner.bodyRenderer.bounds.size.y:0,
            sourceVertexCount=owner.bodyModel?.VertexCount??0,sourceHeadClosed=owner.bodyModel?.HeadClosed??false,sourceAssetsValid=assets,
            idleAnimationId=owner.idle?.AnimationId??-1,frame=owner.frame,frameCount=owner.idle?.Count??0,sourceLayer=owner.subject!=null?owner.subject.layer:-1,
            parentPath=owner.anchor!=null?owner.anchor.name:"",nativeRenderersHidden=owner.hidden.Count,nativeRenderersStillEnabled=enabledNative,
            worldCenter=new[]{center.x,center.y,center.z},equipment=equipment.ToArray(),binds=owner.binds,releases=owner.releases,equipmentModelLoads=owner.modelLoads,
            failures=owner.failures,lastFailure=owner.lastFailure,sharesBattleEquipmentOwner=false,mutatesBattleClassIdentity=false,usesNativeSpin=true});
    }
    void OnDisable(){ReleaseOwned();}
    void OnDestroy(){if(active==this)active=null;ReleaseOwned();}
}

public static class MenuHeroPreviewBridge
{
    public static string DiagnosticsJson=>MenuHeroPreview.Diagnostics();
    public static void Shutdown()=>MenuHeroPreview.Shutdown();
}
