using System.Text.Json;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Camera;
using Assets.Scripts.Managers;
using Assets.Scripts.UI.InGame.FullMap;
using Il2CppInterop.Runtime.Attributes;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;
using NativeArrow=Assets.Scripts.Camera.MinimapArrow;

namespace AzerothMegabonk.AdventureFlow;

// Preserve native maps and their fog. This binds the navigation target only after
// generation and registers an ordinary native arrow when that target is discovered.
public sealed class NativeMapNavigation:MonoBehaviour
{
    public NativeMapNavigation(IntPtr pointer):base(pointer){}
    float next;
    static string stageKey;
    static bool bound;
    static IntPtr lastTarget;
    static bool reportedDiscovery;
    void LateUpdate()
    {
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+.25f;
        try{Refresh();}catch(Exception ex){Plugin.Current.Log.LogWarning("Native map navigation retry: "+ex.Message);}
    }
    [HideFromIl2Cpp]
    internal static void Refresh()
    {
        var player=MyPlayer.Instance;var mini=player?.minimapCameraScript;
        if(player==null || mini==null || MapController.currentMap==null || MapGenerationController.isGenerating)return;
        var key=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name+":"+MapController.GetStageIndex()+":"+mini.Pointer;
        if(stageKey!=key)
        {
            if(bound)mini.bossSpotted=false;
            stageKey=key;bound=true;lastTarget=IntPtr.Zero;reportedDiscovery=false;
        }
        if(MapController.IsFinalBossStage())return;
        // Native Start can run before procedural objects exist. Retry the native
        // finder after generation instead of remembering a permanently null target.
        if(mini.bossSpawner==null)mini.TryFindBossSpawner();
        var gate=mini.bossSpawner;if(gate==null || mini.arrowDict==null)return;
        if(lastTarget!=gate.Pointer)
        {lastTarget=gate.Pointer;Plugin.Note("Navigation target bound after native generation: "+Path(gate));}
        var stageBoss=gate.GetComponent<InteractableBossSpawner>();
        bool fought=stageBoss!=null && stageBoss.numBossesDefeated>0;
        bool fighting=stageBoss!=null && (stageBoss.bossEnemies?.Count??0)>0;
        bool portalReady=stageBoss!=null && stageBoss.numBossesDefeated>0 && stageBoss.portal!=null && stageBoss.portal.activeInHierarchy;
        bool usedFinal=gate.GetComponent<InteractableBossSpawnerFinal>()?.done??false;
        if(fighting || usedFinal || (fought && !portalReady))return;
        bool fogKnown=Resources.FindObjectsOfTypeAll<FullMap>().Any(m=>m.gameObject.scene.IsValid() && Fog(m,gate.position).Revealed);
        // bossSpotted is the native minimap's discovery bit. RGB is the native
        // full map's reveal value (alpha is always255 in this game).
        if(!mini.bossSpotted && !fogKnown && !portalReady)return;
        mini.bossSpotted=true;
        if(!mini.arrowDict.ContainsKey(gate))mini.AddArrow(gate,portalReady?mini.portalColor:mini.bossColor);
        if(!reportedDiscovery)
        {reportedDiscovery=true;Plugin.Note("Discovered gate navigation active: nativeBossSpotted="+mini.bossSpotted+",fullMapFog="+fogKnown+",nativeArrow="+mini.arrowDict.ContainsKey(gate)+",target="+Path(gate));}
    }
    internal readonly record struct FogSample(bool InBounds,int X,int Y,int Index,int Red,int Alpha,bool FullyRevealed)
    {public bool Revealed=>InBounds && (FullyRevealed || Red>0);}
    [HideFromIl2Cpp]
    internal static FogSample Fog(FullMap map,Vector3 world)
    {
        if(map==null || map.worldSize<=0 || map.textureSize<=0 || map.pixels==null)return new(false,-1,-1,-1,0,0,false);
        // Exact projection in native FullMap.QueueRevealFog at0x53572A.
        int size=map.textureSize;
        int x=(int)((world.x+map.worldSize*.5f)/map.worldSize*size);
        int y=(int)((world.z+map.worldSize*.5f)/map.worldSize*size);
        if(x<0 || x>=size || y<0 || y>=size)return new(false,x,y,-1,0,0,false);
        int index=y*size+x;if(index>=map.pixels.Length)return new(false,x,y,index,0,0,false);
        var pixel=map.pixels[index];bool full=map.fullyRevealed!=null && index<map.fullyRevealed.Length && map.fullyRevealed[index];
        return new(true,x,y,index,pixel.r,pixel.a,full);
    }
    internal static string Path(Transform value)
    {if(value==null)return "";var names=new List<string>();for(var t=value;t!=null;t=t.parent)names.Add(t.name);names.Reverse();return string.Join("/",names);}
}

public static class NavigationBridge
{
    static bool ProbeAllowed=>Application.isBatchMode && Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1";
    public static string Describe()
    {
        NativeMapNavigation.Refresh();
        var player=MyPlayer.Instance;var mini=player?.minimapCameraScript;var gate=mini?.bossSpawner;
        var arrows=new List<object>();
        if(mini?.arrowDict!=null)
        {
            foreach(var entry in mini.arrowDict)
            {
                var arrow=entry.Value;if(arrow==null)continue;
                var direction=entry.Key==null?Vector3.zero:entry.Key.position-player.transform.position;direction.y=0;
                var radial=arrow.transform.position-mini.transform.position;radial.y=0;
                arrows.Add(new{target=NativeMapNavigation.Path(entry.Key),active=arrow.gameObject.activeInHierarchy,rendererEnabled=arrow.arrowRenderer?.enabled,layer=arrow.gameObject.layer,cameraLayerVisible=mini.minimapCamera!=null && (mini.minimapCamera.cullingMask&(1<<arrow.gameObject.layer))!=0,directionDot=direction.sqrMagnitude>0 && radial.sqrMagnitude>0?Vector3.Dot(direction.normalized,radial.normalized):0});
            }
        }
        var maps=new List<object>();
        foreach(var map in Resources.FindObjectsOfTypeAll<FullMap>())
        {
            if(!map.gameObject.scene.IsValid())continue;
            var sample=gate==null?default:NativeMapNavigation.Fog(map,gate.position);
            maps.Add(new{name=map.name,worldSize=map.worldSize,textureSize=map.textureSize,gatePixel=sample,gateRevealed=sample.Revealed,cameraEnabled=map.mapCamera?.enabled,cameraMask=map.mapCamera?.cullingMask,mapsOpen=map.mapsOpen});
        }
        return JsonSerializer.Serialize(new{scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,stage=MapController.GetStageIndex(),playerIcon=NativeMapNavigation.Path(mini?.playerIcon),playerIconActive=mini?.playerIcon?.gameObject.activeInHierarchy,miniCameraEnabled=mini?.minimapCamera?.enabled,miniCameraMask=mini?.minimapCamera?.cullingMask,gate=NativeMapNavigation.Path(gate),gateDistance=gate!=null && player!=null?Vector3.Distance(gate.position,player.transform.position):-1,nativeBossSpotted=mini?.bossSpotted,nativeArrowRegistered=gate!=null && mini.arrowDict!=null && mini.arrowDict.ContainsKey(gate),nativeArrowReuses=NativeArrowReusePatch.ReusedArrows,nativeArrowDeadReplacements=NativeArrowReusePatch.ReplacedDeadArrows,arrows,maps});
    }
    // Reveal one normal native110-world-unit fog circle around the gate. This is
    // a batch-only diagnostic, not a global reveal or a gameplay discovery override.
    public static bool RevealGateForProbe()
    {
        if(!ProbeAllowed)return false;
        NativeMapNavigation.Refresh();
        var player=MyPlayer.Instance;var mini=player?.minimapCameraScript;var gate=mini?.bossSpawner;
        var map=Resources.FindObjectsOfTypeAll<FullMap>().FirstOrDefault(m=>m.gameObject.scene.IsValid() && m.fogTexture!=null && m.pixels!=null);
        if(player==null || mini==null || gate==null || map==null)return false;
        var before=NativeMapNavigation.Fog(map,gate.position);
        bool beforeArrow=mini.arrowDict!=null && mini.arrowDict.ContainsKey(gate);
        var last=map.lastPos;
        // QueueRevealFog throttles against actual player movement. Only that
        // temporary throttle is bypassed; all native projection/radius/feathering runs.
        map.lastPos=player.transform.position+Vector3.one*10;
        try{map.QueueRevealFog(gate.position);map.RevealFog();}finally{map.lastPos=last;}
        NativeMapNavigation.Refresh();mini.UpdateArrows();
        var after=NativeMapNavigation.Fog(map,gate.position);
        bool afterArrow=mini.arrowDict!=null && mini.arrowDict.ContainsKey(gate);
        Plugin.Note($"Navigation probe native limited fog reveal: index={before.Index},RGB={before.Red}->{after.Red},alpha={before.Alpha}->{after.Alpha},radius={map.revealRadius},arrow={beforeArrow}->{afterArrow}; "+Describe());
        return after.Revealed && mini.bossSpotted && afterArrow;
    }
    public static bool SetFullMapVisibleForProbe(bool visible)
    {
        if(!ProbeAllowed)return false;
        var pause=UiManager.Instance?.pause;if(pause==null)return false;
        if(visible){if(!pause.IsPaused())pause.Pause();pause.GoToMap();}else pause.Resume();
        return pause.map!=null && pause.map.activeInHierarchy==visible;
    }
}
// Fog navigation and the original boss-completed callback can discover the same
// portal in adjacent native update phases. Reuse that native indicator instead
// of letting AddArrow instantiate a second object and Dictionary.Add the same key.
[HarmonyPatch(typeof(MinimapCamera),nameof(MinimapCamera.AddArrow))]
internal static class NativeArrowReusePatch
{
    internal static int ReusedArrows,ReplacedDeadArrows;
    private static bool Prefix(MinimapCamera __instance,Transform target,Color color)
    {
        if(target==null || __instance.arrowDict==null || !__instance.arrowDict.ContainsKey(target))return true;
        NativeArrow arrow=__instance.arrowDict[target];
        if(arrow==null || arrow.arrowRenderer==null)
        {
            __instance.arrowDict.Remove(target);
            if(arrow!=null)Object.Destroy(arrow.gameObject);
            ReplacedDeadArrows++;return true;
        }
        // This is Assets.Scripts.Camera.MinimapArrow.Set, the same native method
        // whose target/material-color assignment is inlined by native AddArrow.
        // Preserve OnBossSpawnerCompleted and its portal color transition.
        arrow.Set(target,color);
        arrow.gameObject.SetActive(true);
        ReusedArrows++;return false;
    }
}