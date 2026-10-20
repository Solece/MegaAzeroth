using System.Text.Json;
using Assets.Scripts.Actors.Player;
using UnityEngine;
using NativeCameraArrow=Assets.Scripts.Camera.MinimapArrow;

namespace AzerothMegabonk.MageVisuals;

// Navigation markers belong to the native map system, not the replaced skin.
// Resolve native fields and component types; names are diagnostics only.
internal static class NativeMapMarkers
{
    static readonly List<(Transform Root,string Source)> roots=new();
    static Transform playerIcon;
    static Camera minimap;
    static MyPlayer player;
    internal static void Refresh(MyPlayer current,Transform body)
    {
        roots.Clear();player=current;
        playerIcon=current?.minimapCameraScript?.playerIcon;
        minimap=current?.minimapCameraScript?.minimapCamera??current?.minimapCamera;
        Add(playerIcon,"MinimapCamera.playerIcon");
        Add(current?.arrow,"MyPlayer.arrow");
        Add(current?.shootingArrow,"MyPlayer.shootingArrow");
        var nativeCamera=current?.minimapCameraScript;
        if(nativeCamera?.arrowDict!=null)
            foreach(var entry in nativeCamera.arrowDict)
            {
                var arrow=entry.Value;if(arrow==null)continue;
                Add(arrow.transform,"MinimapCamera.arrowDict.Value");
                Add(arrow.arrowRenderer?.transform,"MinimapCamera.arrowDict.Value.arrowRenderer");
            }
        CollectTypedRoots(body);
    }
    static void Add(Transform root,string source)
    {
        if(root!=null && !roots.Any(x=>x.Root==root))roots.Add((root,source));
    }
    static void CollectTypedRoots(Transform node)
    {
        if(node==null || node.name.StartsWith("Azeroth",StringComparison.Ordinal))return;
        if(node.GetComponent<MinimapPlayerIcon>()!=null)Add(node,"MinimapPlayerIcon");
        if(node.GetComponent<MinimapIcon>()!=null)Add(node,"MinimapIcon");
        var arrow=node.GetComponent<MinimapArrow>();
        if(arrow!=null){Add(node,"MinimapArrow");Add(arrow.meshRenderer?.transform,"MinimapArrow.meshRenderer");}
        var nativeArrow=node.GetComponent<NativeCameraArrow>();
        if(nativeArrow!=null){Add(node,"Assets.Scripts.Camera.MinimapArrow");Add(nativeArrow.arrowRenderer?.transform,"Assets.Scripts.Camera.MinimapArrow.arrowRenderer");}
        for(int i=0;i<node.childCount;i++)CollectTypedRoots(node.GetChild(i));
    }
    internal static bool IsProtected(Transform node)
    {
        for(var ancestor=node;ancestor!=null;ancestor=ancestor.parent)
            if(roots.Any(x=>x.Root!=null && x.Root==ancestor))return true;
        return false;
    }
    static void Renderers(Transform node,List<Renderer> result)
    {
        if(node==null)return;
        var r=node.GetComponent<Renderer>();if(r!=null)result.Add(r);
        for(int i=0;i<node.childCount;i++)Renderers(node.GetChild(i),result);
    }
    static string PathOf(Transform node)
    {
        var parts=new List<string>();for(var t=node;t!=null;t=t.parent)parts.Add(t.name);
        parts.Reverse();return string.Join("/",parts);
    }
    static float[] Position(Transform t)=>new[]{t.position.x,t.position.y,t.position.z};
    static bool Visible(Renderer r)=>r!=null && r.enabled && r.gameObject.activeInHierarchy && !r.forceRenderingOff;
    internal static void Describe(int hiddenMapRenderers)
    {
        var all=new List<Renderer>();foreach(var root in roots)if(root.Root!=null)Renderers(root.Root,all);
        var distinct=all.GroupBy(r=>r.Pointer).Select(g=>g.First()).ToArray();
        var iconRenderers=new List<Renderer>();Renderers(playerIcon,iconRenderers);
        var full=Resources.FindObjectsOfTypeAll<FullMap>().FirstOrDefault(m=>m!=null && m.gameObject.scene.IsValid());
        var fullCamera=full?.mapCamera;
        var masks=new {minimap=minimap?.cullingMask,fullMap=fullCamera?.cullingMask};
        MageVisualBridge.MapRenderersProtected=distinct.Length;
        MageVisualBridge.MapRenderersEnabled=distinct.Count(Visible);
        MageVisualBridge.MapRenderersHiddenByMage=hiddenMapRenderers;
        MageVisualBridge.PlayerMapArrowVisible=iconRenderers.Any(Visible);
        MageVisualBridge.PlayerMapArrowPath=playerIcon!=null?PathOf(playerIcon):"";
        MageVisualBridge.MapMarkersJson=JsonSerializer.Serialize(new{
            playerIcon=playerIcon!=null?new {path=PathOf(playerIcon),position=Position(playerIcon),yaw=playerIcon.eulerAngles.y,active=playerIcon.gameObject.activeInHierarchy}:null,
            player=player!=null?new {path=PathOf(player.transform),position=Position(player.transform)}:null,
            cameraMasks=masks,
            roots=roots.Where(x=>x.Root!=null).Select(x=>new{source=x.Source,path=PathOf(x.Root),layer=x.Root.gameObject.layer,layerName=LayerMask.LayerToName(x.Root.gameObject.layer)}).ToArray(),
            renderers=distinct.Select(r=>new{path=PathOf(r.transform),layer=r.gameObject.layer,layerName=LayerMask.LayerToName(r.gameObject.layer),enabled=r.enabled,active=r.gameObject.activeInHierarchy,forceRenderingOff=r.forceRenderingOff,minimapCulled=minimap==null || (minimap.cullingMask&(1<<r.gameObject.layer))==0,fullMapCulled=fullCamera==null || (fullCamera.cullingMask&(1<<r.gameObject.layer))==0}).ToArray(),
            hiddenByMage=hiddenMapRenderers
        });
    }
    internal static void Clear(){roots.Clear();playerIcon=null;minimap=null;player=null;}
}
