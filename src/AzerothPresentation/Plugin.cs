using System.Reflection;
using System.Text.Json;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace AzerothPresentation;

[BepInPlugin("local.azerothmegabonk.presentation","Azeroth Menu and Loading Presentation","0.1.6")]
public sealed class Plugin:BasePlugin
{
    internal static PresentationRuntime Runtime;
    internal static string AssetRoot=>Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location),"assets","menu-loading");
    Harmony harmony;
    public override void Load()
    {
        Runtime=AddComponent<PresentationRuntime>();
        harmony=new Harmony("local.azerothmegabonk.presentation");harmony.PatchAll(typeof(Plugin).Assembly);
        if(PresentationProbe.Armed)AddComponent<PresentationProbe>();
        Log.LogInfo("Original Eastern Kingdom menu/loading artwork; native menu, preview and scene loader retained.");
    }
    public override bool Unload(){Runtime?.Shutdown();Runtime=null;harmony?.UnpatchSelf();return true;}
}

public static class PresentationBridge
{
    public static string Describe()=>Plugin.Runtime?.Describe()??"{\"Enabled\":false}";
    public static string NativeSceneJson()=>PresentationDiagnostics.SceneJson();
    public static bool SetEnabledForProbe(bool enabled)
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return false;
        Plugin.Runtime?.SetPresentation(enabled);return Plugin.Runtime!=null;
    }
}

[HarmonyPatch(typeof(LoadingScreen),"Start")]
static class NativeLoadingStart{static void Postfix(LoadingScreen __instance)=>Plugin.Runtime?.AttachLoading(__instance);}
[HarmonyPatch(typeof(LoadingScreen),"OnDisable")]
static class NativeLoadingDisable{static void Prefix(LoadingScreen __instance)=>Plugin.Runtime?.DetachLoading(__instance);}
[HarmonyPatch(typeof(LoadingScreen),"OnDestroy")]
static class NativeLoadingDestroy{static void Prefix(LoadingScreen __instance)=>Plugin.Runtime?.DetachLoading(__instance);}

public sealed class PresentationRuntime:MonoBehaviour
{
    public PresentationRuntime(IntPtr p):base(p){}
    bool active=true,shutDown;string error="",lastError="";int errorCount;float retryAt;int menuScene=-1;
    SourceArt art;GameObject backdrop;Mesh mesh;Material material;MenuCamera cameraOwner;MenuMask mask;
    MenuDepthOfFieldState menuFocus;object lastFocusRestore;
    LoadingPresentation loading;int loadingAttached,loadingDetached;float lastProgress;
    void LateUpdate()
    {
        if(shutDown)return;
        try
        {
            if(!active){ReleaseMenu();ReleaseLoading();return;}
            if(art==null&&Time.realtimeSinceStartup<retryAt)return;
            var nativeLoading=Object.FindObjectOfType<LoadingScreen>();
            if(nativeLoading!=null&&nativeLoading.isActiveAndEnabled&&LoadingScreen.isLoading&&(loading==null||loading.Native!=nativeLoading))AttachLoading(nativeLoading);
            if(loading!=null){if(!loading.Alive){ReleaseLoading();}else{loading.Update();lastProgress=loading.Progress;PresentationProbe.ObserveLoadingFrame();}}
            var menu=Object.FindObjectOfType<MainMenu>();
            var owner=menu!=null&&menu.isActiveAndEnabled?menu.menuCamera:null;
            if(owner==null||owner.camera==null||!owner.camera.enabled||LoadingScreen.isLoading)
            {ReleaseMenu();error="";return;}
            EnsureArt();
            if(backdrop==null||cameraOwner!=owner||menuScene!=owner.gameObject.scene.handle)
            {ReleaseMenu();BindMenu(owner);}
            mask?.Apply();menuFocus?.Apply(owner.camera);PositionBackdrop(owner.camera);error="";
        }
        catch(Exception e){error=e.GetType().Name+": "+e.Message;lastError=error;errorCount++;ReleaseMenu();ReleaseLoading();retryAt=Time.realtimeSinceStartup+3;}
    }
    [HideFromIl2Cpp] void EnsureArt()
    {
        if(art!=null)return;
        art=new SourceArt(Plugin.AssetRoot);
    }
    [HideFromIl2Cpp] void BindMenu(MenuCamera owner)
    {
        cameraOwner=owner;menuScene=owner.gameObject.scene.handle;
        var go=new GameObject("AzerothPresentation_MenuBackdrop");backdrop=go;go.layer=owner.camera.gameObject.layer;
        mesh=SourceArt.Quad();go.AddComponent<MeshFilter>().sharedMesh=mesh;
        material=new Material(Shader.Find("Unlit/Texture")){mainTexture=art.Menu,name="AzerothPresentation_DarkenedSourceArt"};material.hideFlags=HideFlags.DontUnloadUnusedAsset;
        go.AddComponent<MeshRenderer>().sharedMaterial=material;
        mask=new MenuMask(owner.gameObject.scene.handle);
        menuFocus=new MenuDepthOfFieldState();menuFocus.Apply(owner.camera);
    }
    [HideFromIl2Cpp] void PositionBackdrop(Camera camera)
    {
        float distance=20;
        var visible=new List<Renderer>();
        foreach(var display in Object.FindObjectsOfType<CharacterDisplay>())if(display.gameObject.activeInHierarchy)foreach(var r in display.GetComponentsInChildren<Renderer>(true))visible.Add(r);
        foreach(var r in Object.FindObjectsOfType<Renderer>())if(r.name.StartsWith("AzerothMenuHero_",StringComparison.Ordinal)||r.name.StartsWith("AzerothMenuEquipment_",StringComparison.Ordinal))visible.Add(r);
        foreach(var renderer in visible)
        if(renderer!=null&&renderer.enabled&&!renderer.forceRenderingOff&&renderer.gameObject.activeInHierarchy)
        {
            var b=renderer.bounds;float z=Vector3.Dot(b.center-camera.transform.position,camera.transform.forward)+b.extents.magnitude+2;
            distance=Mathf.Max(distance,z);
        }
        distance=Mathf.Min(distance,camera.farClipPlane*.8f);
        backdrop.transform.position=camera.transform.position+camera.transform.forward*distance;
        backdrop.transform.rotation=camera.transform.rotation;
        float height=2*distance*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f);
        backdrop.transform.localScale=new Vector3(height*camera.aspect*1.06f,height*1.06f,1);
    }
    [HideFromIl2Cpp] internal void AttachLoading(LoadingScreen native)
    {
        if(!active||shutDown||native==null||loading?.Native==native||art==null&&Time.realtimeSinceStartup<retryAt)return;
        try{ReleaseMenu();ReleaseLoading();EnsureArt();loading=new LoadingPresentation(native,art);loadingAttached++;PresentationProbe.ObserveLoading(native);error="";}
        catch(Exception e){error="Loading attachment: "+e.Message;lastError=error;errorCount++;ReleaseLoading();}
    }
    [HideFromIl2Cpp] internal void DetachLoading(LoadingScreen native){if(loading!=null&&loading.Native==native)ReleaseLoading();}
    [HideFromIl2Cpp] void ReleaseMenu()
    {
        mask?.Restore();mask=null;if(menuFocus!=null)lastFocusRestore=menuFocus.Restore();menuFocus=null;if(backdrop!=null){backdrop.SetActive(false);Object.Destroy(backdrop);}backdrop=null;
        if(material!=null)Object.Destroy(material);material=null;if(mesh!=null)Object.Destroy(mesh);mesh=null;cameraOwner=null;menuScene=-1;
    }
    [HideFromIl2Cpp] void ReleaseLoading(){if(loading!=null){lastProgress=loading.Progress;loading.Dispose();loading=null;loadingDetached++;}}
    [HideFromIl2Cpp] public void SetPresentation(bool enabled){active=enabled;if(!active){ReleaseMenu();ReleaseLoading();}}
    [HideFromIl2Cpp] public void Shutdown(){if(shutDown)return;shutDown=true;ReleaseMenu();ReleaseLoading();art?.Dispose();art=null;}
    [HideFromIl2Cpp] public string Describe()=>JsonSerializer.Serialize(new{Enabled=active,ArtReady=art!=null,Error=error,LastError=lastError,ErrorCount=errorCount,SourceMenuTint=new[]{.4f,.38f,.36f,1},SourceMenuOverscan=1.06f,MenuActive=backdrop!=null&&backdrop.activeInHierarchy,MenuScene=menuScene,MenuMask=mask?.Describe(),MenuDepthOfField=menuFocus?.Snapshot(),LastDepthOfFieldRestore=lastFocusRestore,LoadingActive=loading?.Alive==true,LoadingAttached=loadingAttached,LoadingDetached=loadingDetached,NativeProgress=loading?.Progress??lastProgress,Loading=loading?.Describe(),OwnedMenuObjects=Object.FindObjectsOfType<Transform>().Count(t=>t.name=="AzerothPresentation_MenuBackdrop"),OwnedLoadingCanvases=Object.FindObjectsOfType<Canvas>().Count(c=>c.name=="AzerothPresentation_LoadingCanvas"),Scope="Original source art, menu tint and 106% overscan. Vignette gradient and loading frame placement remain presentation adaptations."});
    void OnDestroy()=>Shutdown();
}

sealed class MenuMask
{
    readonly int scene;readonly Dictionary<int,(Renderer Value,bool Hidden,string Path)> renderers=new();
    readonly HashSet<int> ambientRenderers=new();
    readonly Dictionary<int,(Terrain Value,bool Height,bool Trees)> terrains=new();
    readonly Dictionary<int,(Behaviour Value,bool Enabled)> behaviours=new();
    int staleRenderersPruned,staleTerrainsPruned,staleBehavioursPruned;
    internal MenuMask(int sceneId){scene=sceneId;Apply();}
    internal void Apply()
    {
        PruneDestroyed();
        foreach(var v in Object.FindObjectsOfType<MenuVisuals>())
        {
            if(v.gameObject.scene.handle!=scene)continue;
            if(v.terrain!=null){int id=v.terrain.GetInstanceID();if(!terrains.ContainsKey(id))terrains[id]=(v.terrain,v.terrain.drawHeightmap,v.terrain.drawTreesAndFoliage);v.terrain.drawHeightmap=false;v.terrain.drawTreesAndFoliage=false;}
            if(v.grassRenderer!=null){int id=v.grassRenderer.GetInstanceID();if(!behaviours.ContainsKey(id))behaviours[id]=(v.grassRenderer,v.grassRenderer.enabled);v.grassRenderer.enabled=false;}
            // Only the native component's explicit scenery fields; never its broad
            // general/dungeon ancestor, hero, chest, portal, lights or audio.
            foreach(var root in new[]{v.forestBg,v.desertBg,v.particles})if(root!=null&&root.scene.IsValid()&&root.scene.handle==scene)
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer.GetComponentInParent<CharacterDisplay>()!=null)continue;
                Hide(renderer,root==v.particles);
            }
        }
        // Positive native75356 scene evidence: these menu-only ambient entities
        // sit outside MenuVisuals forest/desert subtrees. Restrict exact observed
        // roots/paths and scene; never disable their AI, collider or GameObject.
        foreach(var renderer in Object.FindObjectsOfType<Renderer>())
        {
            if(renderer.gameObject.scene.handle!=scene||renderer.GetComponentInParent<CharacterDisplay>()!=null)continue;
            var root=renderer.transform.root;bool ent=root.name=="B_Ent2 (1)(Clone)";
            bool particle=false;
            for(var t=renderer.transform;t!=null;t=t.parent)
            {string path=PresentationDiagnostics.Path(t);if(path=="CameraParent/ForestParticles(Clone)"||path=="CameraParent/DesertParticles(Clone)"){particle=true;break;}}
            if(ent||particle)Hide(renderer,true);
        }
    }
    void Hide(Renderer renderer,bool ambient){int id=renderer.GetInstanceID();if(!renderers.ContainsKey(id))renderers[id]=(renderer,renderer.forceRenderingOff,PresentationDiagnostics.Path(renderer.transform));if(ambient)ambientRenderers.Add(id);renderer.forceRenderingOff=true;}
    static bool Alive(Component component){try{return component!=null&&component.transform!=null;}catch{return false;}}
    void PruneDestroyed()
    {
        foreach(var id in renderers.Where(x=>!Alive(x.Value.Value)).Select(x=>x.Key).ToArray()){renderers.Remove(id);ambientRenderers.Remove(id);staleRenderersPruned++;}
        foreach(var id in terrains.Where(x=>!Alive(x.Value.Value)).Select(x=>x.Key).ToArray()){terrains.Remove(id);staleTerrainsPruned++;}
        foreach(var id in behaviours.Where(x=>!Alive(x.Value.Value)).Select(x=>x.Key).ToArray()){behaviours.Remove(id);staleBehavioursPruned++;}
    }
    internal object Describe()
    {
        PruneDestroyed();
        return new{NativeTerrainObjects=terrains.Count,NativeSceneryRenderers=renderers.Count,NativeGrassBehaviours=behaviours.Count,NativeAmbientRenderers=ambientRenderers.Count,StaleRenderersPruned=staleRenderersPruned,StaleTerrainsPruned=staleTerrainsPruned,StaleBehavioursPruned=staleBehavioursPruned,MaskedRendererPaths=renderers.Values.Select(x=>x.Path).ToArray(),AmbientRendererPaths=renderers.Where(x=>ambientRenderers.Contains(x.Key)).Select(x=>x.Value.Path).ToArray()};
    }
    internal void Restore()
    {
        foreach(var row in renderers.Values)try{if(row.Value!=null)row.Value.forceRenderingOff=row.Hidden;}catch{}
        foreach(var row in terrains.Values)try{if(row.Value!=null){row.Value.drawHeightmap=row.Height;row.Value.drawTreesAndFoliage=row.Trees;}}catch{}
        foreach(var row in behaviours.Values)try{if(row.Value!=null)row.Value.enabled=row.Enabled;}catch{}
        renderers.Clear();terrains.Clear();behaviours.Clear();ambientRenderers.Clear();
    }
}

static class PresentationDiagnostics
{
    internal static string Path(Transform t){var names=new List<string>();for(;t!=null;t=t.parent)names.Add(t.name);names.Reverse();return string.Join("/",names);}
    internal static string SceneJson()=>JsonSerializer.Serialize(new{Scene=SceneManager.GetActiveScene().name,Menu=Object.FindObjectsOfType<MainMenu>().Select(x=>new{Path=Path(x.transform),MenuCamera=x.menuCamera!=null?Path(x.menuCamera.transform):"",Main=x.tabMenu?.activeSelf,Characters=x.tabCharacters?.activeSelf,Maps=x.tabMaps?.activeSelf}),Cameras=Object.FindObjectsOfType<Camera>().Select(c=>new{Path=Path(c.transform),c.enabled,c.cullingMask,c.fieldOfView,c.depth,c.nearClipPlane,c.farClipPlane}),MenuVisuals=Object.FindObjectsOfType<MenuVisuals>().Select(v=>new{Path=Path(v.transform),Terrain=v.terrain!=null?Path(v.terrain.transform):"",Forest=v.forestBg!=null?Path(v.forestBg.transform):"",Desert=v.desertBg!=null?Path(v.desertBg.transform):"",General=v.general!=null?Path(v.general.transform):""}),Renderers=Object.FindObjectsOfType<Renderer>().Select(r=>new{Path=Path(r.transform),r.enabled,r.forceRenderingOff,Layer=r.gameObject.layer,Materials=r.sharedMaterials.Select(m=>m!=null?m.name:"").ToArray()}),Loading=Object.FindObjectsOfType<LoadingScreen>().Select(l=>new{Path=Path(l.transform),Bar=l.loadingBar!=null?Path(l.loadingBar):"",NativeScaleX=l.loadingBar?.localScale.x,BarIsRect=l.loadingBar?.TryCast<RectTransform>()!=null})});
}
