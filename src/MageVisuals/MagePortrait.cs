using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

public sealed class MagePortrait:MonoBehaviour
{
    public MagePortrait(IntPtr pointer):base(pointer){}
    private GameObject subject,rig;
    private Camera camera;
    private RenderTexture texture;
    private float next;
    private float retryAt,nextFailureLog;
    private static MagePortrait active;
    internal static object AssetHealth()
    {
        try{return new{valid=active!=null && active.camera!=null && active.texture!=null && active.texture.IsCreated(),textureAlive=active!=null && active.texture!=null,created=active!=null && active.texture!=null && active.texture.IsCreated(),retained=active!=null && active.texture!=null && (active.texture.hideFlags&HideFlags.DontUnloadUnusedAsset)!=0};}
        catch(Exception ex){return new{valid=false,error=ex.GetType().Name};}
    }
    void LateUpdate()
    {
        active=this;if(Time.unscaledTime<retryAt || Time.unscaledTime<next)return;next=Time.unscaledTime+.1f;
        try
        {
            if(!MageVisualBridge.IsAttached){MageVisualBridge.PortraitTexture=null;subject=null;return;}
            if(subject==null || !subject.activeInHierarchy)subject=GameObject.Find("AzerothMage_HumanMale");
            if(subject==null)return;
            var renderer=subject.GetComponent<MeshRenderer>();if(renderer==null)return;
            if(camera==null || texture==null){Release();Build();}
            if(!texture.IsCreated())texture.Create();
            if(camera.targetTexture!=texture)camera.targetTexture=texture;
            var bounds=renderer.bounds;float h=bounds.size.y;var target=bounds.center+Vector3.up*h*.33f;
            var forward=subject.transform.forward;
            camera.cullingMask=1<<subject.layer;
            camera.orthographicSize=h*.16f;camera.transform.position=target+forward*h*.7f;camera.transform.LookAt(target);
            camera.Render();MageVisualBridge.PortraitTexture=texture;
        }
        catch(Exception ex){retryAt=Time.unscaledTime+1;subject=null;Release();if(Time.unscaledTime>=nextFailureLog){nextFailureLog=Time.unscaledTime+5;Plugin.Logger.LogWarning("Mage portrait will retry after native scene/asset change: "+ex);}}
    }
    [HideFromIl2Cpp]
    void Build()
    {
        rig=new GameObject("AzerothMagePortraitCamera");Object.DontDestroyOnLoad(rig);
        camera=rig.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=Color.clear;camera.nearClipPlane=.01f;camera.farClipPlane=100;
        texture=Model.Retain(new RenderTexture(192,192,24,RenderTextureFormat.ARGB32));texture.Create();camera.targetTexture=texture;
    }
    [HideFromIl2Cpp]
    void Release()
    {
        MageVisualBridge.PortraitTexture=null;
        if(camera!=null)camera.targetTexture=null;
        if(texture!=null){texture.Release();Object.Destroy(texture);}if(rig!=null)Object.Destroy(rig);
        camera=null;texture=null;rig=null;
    }
    void OnDestroy(){if(active==this)active=null;Release();}
}
