using System.Text.Json;
using System.Runtime.InteropServices;
using Assets.Scripts.Actors.Enemies;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AzerothMegabonk.EnemyVisuals;

[BepInPlugin(Id, "Azeroth Megabonk - Enemy Visuals", "0.2.8")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.azerothmegabonk.enemyvisuals";
    internal static ManualLogSource Logger = null!;
    internal static readonly Dictionary<string, Model> Models = new();
    internal static readonly HashSet<string> FailedModels = new();
    internal static int Attached,ModelLoads,Recoveries;
    internal static readonly string[] ModelKeys={"boar","skeleton","murloc","wolf","kobold","gnoll","tarantula","ogre","infernal","lich","bonegolem","ghost","ghoul","zombie","scorpion","silithidwasp","ent","fleshgolem","doomguard","dragonspawncaster"};
    private static readonly Dictionary<string,float> RetryAt=new(),NextFailureLog=new();
    private Harmony harmony;
    public override void Load()
    {
        Logger = Log;
        ClassInjector.RegisterTypeInIl2Cpp<AzerothVisual>();
        ClassInjector.RegisterTypeInIl2Cpp<AzerothCorpseVisual>();
        harmony = new Harmony(Id);
        harmony.PatchAll(typeof(Plugin).Assembly);
        // Do not make graphics calls until the game has initialized its device.
        Log.LogInfo("Enemy visual module ready; models load on first native enemy spawn.");
    }
    internal static Model GetModel(string key,bool verifyFrames=false)
    {
        if (Models.TryGetValue(key,out var found) && found.IsUsable(verifyFrames))return found;
        if(RetryAt.TryGetValue(key,out var retry) && Time.unscaledTime<retry)return null;
        string folder=Path.Combine(Paths.GameRootPath,"AzerothAssets",key);
        if(!File.Exists(Path.Combine(folder,"model.json")))return null;
        try
        {
            found?.Release();Models.Remove(key);
            var model=Model.Load(folder);Models[key]=model;ModelLoads++;FailedModels.Remove(key);RetryAt.Remove(key);return model;
        }
        catch(Exception ex)
        {
            FailedModels.Add(key);RetryAt[key]=Time.unscaledTime+1;
            if(!NextFailureLog.TryGetValue(key,out var next) || Time.unscaledTime>=next)
            {NextFailureLog[key]=Time.unscaledTime+5;Logger.LogWarning($"Model {key} will retry after load/native-asset failure; native appearance remains available: {ex}");}
            return null;
        }
    }
    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        EnemyDeathPresentation.Shutdown();
        NativeGhostPaletteCache.Shutdown();
        foreach(var visual in Object.FindObjectsOfType<AzerothVisual>(true)) visual.RestoreNative();
        foreach(var model in Models.Values) model.Release();
        Models.Clear();FailedModels.Clear();RetryAt.Clear();NextFailureLog.Clear();return true;
    }
}

[HarmonyPatch(typeof(Enemy), nameof(Enemy.InitEnemy))]
internal static class EnemyInitPatch
{
    internal static string BossModel(string name)
    {
        if(name.Contains("skeleton") || name.Contains("calcium")) return "bonegolem";
        if(name.Contains("ghost") || name.Contains("pharaoh")) return "lich";
        if(name.Contains("golem")) return "infernal";
        if(name.Contains("scorpion") || name.Contains("spider")) return "tarantula";
        return "ogre";
    }
    static void Postfix(Enemy __instance)
    {
        try
        {
            if (__instance.renderer == null || __instance.enemyData == null) return;
            bool boss = __instance.IsBoss() || __instance.IsStageBoss() || __instance.IsFinalBoss();
            string original = __instance.enemyData.enemyName.ToString().ToLowerInvariant();
            string selected=EnemyVarietySelection.Resolve(__instance,boss);
            string key = EndlessGhostPresentation.Matches(__instance) ? "ghost" : selected ?? (boss ? BossModel(original) : original.Contains("skeleton") ? "skeleton" : original.Contains("goblintank") ? "gnoll" : original.Contains("goblin") ? "kobold" : original.Contains("spider") || original.Contains("scorpion") ? "tarantula" : original.Contains("frog") || original.Contains("slime") ? "murloc" : original.Contains("ghoul") || original.Contains("zombie") ? "wolf" : "boar");
            // New source packs fail visibly to the retained native body instead
            // of silently converting an explicit species choice into Boar/Ogre.
            var model = Plugin.GetModel(key) ?? (key=="ghost"||selected!=null?null:Plugin.GetModel(boss ? "ogre" : "boar"));
            if (model == null){EnemyVisualOwnership.Find(__instance)?.RestoreNative();return;}
            var visual = EnemyVisualOwnership.Find(__instance) ?? __instance.gameObject.GetComponent<AzerothVisual>() ?? __instance.gameObject.AddComponent<AzerothVisual>();
            visual.Bind(__instance, model, boss);
            if (++Plugin.Attached <= 8 || boss) Plugin.Logger.LogInfo($"Visual attached to native enemy {original}; hp={__instance.hp}, model={model.Name}, boss={boss}; collider and spawner retained. {visual.Describe()}");
        }
        catch (Exception ex) { Plugin.Logger.LogError($"Visual attachment failed; native enemy retained: {ex}"); }
    }
}

// Suppress score submission for the lab without changing the user's saved preferences.
[HarmonyPatch(typeof(SteamLeaderboardsManagerNew), nameof(SteamLeaderboardsManagerNew.QueueLeaderboardUpload))]
internal static class LabQueueGuard { static bool Prefix() => false; }
[HarmonyPatch(typeof(SteamLeaderboardsManagerNew), nameof(SteamLeaderboardsManagerNew.UploadLeaderboardScore))]
internal static class LabUploadGuard { static bool Prefix() => false; }

internal sealed class Clip
{
    public string Name = ""; public float Duration; public int Count; public Mesh[] Meshes = null!;
    public int AnimationId,SequenceIndex,SourceFrequency=32767;
}
internal sealed partial class Model
{
    public string Name = "";
    public Mesh Template = null!;
    public Material Material = null!;
    public Texture2D Texture = null!;
    public Dictionary<string,Clip> Clips = new();
    public static Model Load(string folder)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"model.json")));
        var j = json.RootElement;
        float[] Floats(string n) => j.GetProperty(n).EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var p = Floats("positions"); var n = Floats("normals"); var uv = Floats("uv");
        int count = j.GetProperty("vertexCount").GetInt32();
        var verts = new Vector3[count]; var normals = new Vector3[count]; var uvs = new Vector2[count];
        for(int i=0;i<count;i++) { verts[i]=new(p[i*3],p[i*3+1],p[i*3+2]); normals[i]=new(n[i*3],n[i*3+1],n[i*3+2]); uvs[i]=new(uv[i*2],uv[i*2+1]); }
        var model = new Model { Name=j.GetProperty("name").GetString() ?? "Azeroth creature",Key=Path.GetFileName(folder),VertexCount=count };
        try
        {
        model.Template = Retain(new Mesh { name="Azeroth_"+model.Name });
        model.Template.vertices = verts; model.Template.normals=normals; model.Template.uv=uvs;
        model.Template.triangles=j.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        model.Template.RecalculateBounds();
        using var tm = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"texture.rgba.json")));
        int tw=tm.RootElement.GetProperty("width").GetInt32(), th=tm.RootElement.GetProperty("height").GetInt32();
        var texture = model.Texture = Retain(new Texture2D(tw,th,TextureFormat.RGBA32,false));
        byte[] raw=File.ReadAllBytes(Path.Combine(folder,"texture.rgba"));
        if(raw.Length!=tw*th*4) throw new InvalidDataException("RGBA texture size mismatch");
        var pin=GCHandle.Alloc(raw,GCHandleType.Pinned);
        try { texture.LoadRawTextureData(pin.AddrOfPinnedObject(),raw.Length);texture.Apply(false,true); } finally { pin.Free(); }
        texture.filterMode=FilterMode.Bilinear; texture.wrapMode=TextureWrapMode.Repeat;
        var shader = Shader.Find("Unlit/Transparent Cutout") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        if(shader==null) throw new InvalidOperationException("No cutout-compatible shader available");
        model.Material = Retain(new Material(shader) { mainTexture=texture, color=Color.white, enableInstancing=true });
        if(model.Material.HasProperty("_Cutoff")) model.Material.SetFloat("_Cutoff",0.5f);
        model.Material.SetFloat("_Mode",1); model.Material.EnableKeyword("_ALPHATEST_ON"); model.Material.renderQueue=2450;
        model.LoadGhostProfile(folder);
        model.LoadSourceProfile(folder,j);
        foreach(var c in j.GetProperty("animations").EnumerateArray())
        {
            var clip = new Clip { Name=c.GetProperty("name").GetString()!,Duration=c.GetProperty("duration").GetSingle(),Count=c.GetProperty("frameCount").GetInt32(),AnimationId=c.GetProperty("animationId").GetInt32(),SequenceIndex=c.GetProperty("sequenceIndex").GetInt32() };
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder,c.GetProperty("file").GetString()!));
            if(bytes.Length!=clip.Count*count*12) throw new InvalidDataException("Animation frame size mismatch");
            clip.Meshes=new Mesh[clip.Count];
            model.Clips[clip.Name]=clip;
            using var reader=new BinaryReader(new MemoryStream(bytes));
            for(int f=0;f<clip.Count;f++)
            {
                var frameVerts=new Vector3[count];
                for(int i=0;i<count;i++) frameVerts[i]=new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                var frameMesh=Retain(Object.Instantiate(model.Template)); frameMesh.name=$"{model.Name}_{clip.Name}_{f}";
                frameMesh.vertices=frameVerts; frameMesh.RecalculateNormals(); frameMesh.RecalculateBounds();
                clip.Meshes[f]=frameMesh;
            }
            model.Clips[clip.Name]=clip;
        }
        model.LoadDeathProfile(folder,j);
        Plugin.Logger.LogInfo($"Loaded {model.Name}: {count} vertices, {model.Clips.Count} original baked animation clips; shader {shader.name}.");
        return model;
        }
        catch { model.Release();throw; }
    }
    internal void Release()
    {
        foreach(var clip in Clips.Values)
            if(clip.Meshes!=null) foreach(var mesh in clip.Meshes) if(mesh!=null) Object.Destroy(mesh);
        if(Template!=null) Object.Destroy(Template);
        if(SourceMaterials!=null)foreach(var material in SourceMaterials)if(material!=null && material!=Material)Object.Destroy(material);
        if(GhostHiddenMaterial!=null)Object.Destroy(GhostHiddenMaterial);
        ReleaseSourceResources();
        if(Material!=null) Object.Destroy(Material);
        if(Texture!=null) Object.Destroy(Texture);
    }
}

public sealed partial class AzerothVisual : MonoBehaviour
{
    public AzerothVisual(IntPtr ptr) : base(ptr) { }
    private Enemy enemy;
    private Renderer original;
    private MeshRenderer replacement;
    private GameObject visual;
    private MeshFilter filter;
    private Model model;
    private float startTime, nextFrame;
    private int lastFrame=-1;
    private Vector3 previous;
    private string clipName="";
    private bool isBoss;
    private Bounds nativeRendererBounds, nativeColliderBounds;
    private float sourceHeight, referenceHeight, sizeMultiplier;
    private string scaleReference="";
    private float nativeFeetY,nativeGroundCheckY,groundY,groundSampleFeetY,floorCorrectionY,templateMinY,frameMinY,frameMaxY,nextGroundSample,retryAt,nextFailureLog;
    private bool groundFound,needsRebind;
    private float baseScale,nextAssetCheck;
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    internal void Bind(Enemy target,Model data,bool boss)
    {
        RegisterOwnership(target);
        deathCaptured=false;
        ResetGhostPalette();
        ResetSourcePresentation();
        sourceMaterialBoundAt=EnemyAnimationClock.Now;
        enabled=true;enemy=target; original=target.renderer; model=data; isBoss=boss;retryAt=0;needsRebind=false;nextGroundSample=0;groundFound=false;nextAssetCheck=Time.unscaledTime+1;
        if(visual!=null) { visual.SetActive(false);Object.Destroy(visual); }
        visual=new GameObject("AzerothCreature_"+data.Name);
        visual.layer=original.gameObject.layer;
        // Native model import rotations differ from the converted Unity Y-up assets.
        // Attach to the physics root, with a separate visual facing from native velocity.
        visual.transform.SetParent(target.transform,false);
        visual.transform.localPosition=Vector3.zero; visual.transform.localRotation=Quaternion.identity;
        var mesh=data.Template;
        float parentScale=Math.Max(.00001f,Math.Abs(target.transform.lossyScale.y));
        // Capture native geometry before hiding it. Native boss size already includes
        // rendererScale, elite scaling and the spawner's extraSizeMultiplier.
        nativeRendererBounds=original.bounds;
        nativeColliderBounds=nativeRendererBounds;
        if(target.collider!=null)
        {
            var c=target.collider;
            float worldHeight=(c.direction==1?c.height:c.radius*2)*Math.Abs(c.transform.lossyScale.y);
            nativeColliderBounds=new Bounds(c.transform.TransformPoint(c.center),new Vector3(c.radius*2,worldHeight,c.radius*2));
        }
        // Ordinary creatures use exactly two-thirds of the former collider-normalized
        // visual height. Bosses retain their original Megabonk rendered height.
        bool rendererValid=float.IsFinite(nativeRendererBounds.size.y) && nativeRendererBounds.size.y>.01f;
        var nativeBounds=boss && rendererValid ? nativeRendererBounds : nativeColliderBounds;
        scaleReference=boss && rendererValid ? "native-renderer" : "native-collider";
        referenceHeight=Math.Max(.01f,nativeBounds.size.y);
        sizeMultiplier=boss ? 1f : 2f/3f;
        float desired=referenceHeight*sizeMultiplier/parentScale;
        var feetWorld=EnemyVisualGround.NativeFeet(target,out nativeGroundCheckY);nativeFeetY=feetWorld.y;
        sourceHeight=Math.Max(.01f,mesh.bounds.size.y);
        float scale=desired/sourceHeight;baseScale=scale;templateMinY=mesh.bounds.min.y;
        visual.transform.localScale=Vector3.one*scale;
        visual.transform.localPosition=target.transform.InverseTransformPoint(feetWorld)-new Vector3(mesh.bounds.center.x*scale,mesh.bounds.min.y*scale,mesh.bounds.center.z*scale);
        filter=visual.AddComponent<MeshFilter>(); filter.sharedMesh=mesh;
        replacement=visual.AddComponent<MeshRenderer>(); replacement.sharedMaterials=data.RenderMaterials;
        PlaceAboveNativeFloor(mesh,true);
        if(Plugin.Attached<3 || boss) Plugin.Logger.LogInfo($"Visual scale evidence: boss={boss}, reference={scaleReference}, nativeRendererHeight={nativeRendererBounds.size.y}, nativeColliderHeight={nativeColliderBounds.size.y}, ratio={sizeMultiplier}, sourceHeight={sourceHeight}, localScale={scale}, visualBounds={replacement.bounds.size}, position={replacement.bounds.center}");
        SuppressNativeBodies();
        startTime=EnemyAnimationClock.Now; nextFrame=0; lastFrame=-1; clipName=""; previous=target.transform.position;
        SyncGhostPalette();
        SetSourceRenderFrame(mesh);
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    private void PlaceAboveNativeFloor(Mesh mesh,bool forceGround=false)
    {
        var feet=EnemyVisualGround.NativeFeet(enemy,out nativeGroundCheckY);nativeFeetY=feet.y;
        bool useTerrain=isBoss || model.Name.Equals("Tarantula",StringComparison.OrdinalIgnoreCase);
        if(useTerrain && (forceGround || Time.unscaledTime>=nextGroundSample))
        {
            nextGroundSample=Time.unscaledTime+(isBoss?.15f:.3f);groundSampleFeetY=feet.y;
            groundFound=EnemyVisualGround.TryGround(feet,out groundY);
        }
        // Do not reuse a terrain sample from the former side of a drop/step.
        if(groundFound && Math.Abs(feet.y-groundSampleFeetY)>2f)groundFound=false;
        if(!groundFound)groundY=feet.y;
        var bounds=mesh.bounds;frameMinY=bounds.min.y;frameMaxY=bounds.max.y;
        visual.transform.localPosition=enemy.transform.InverseTransformPoint(feet)-new Vector3(model.Template.bounds.center.x*baseScale,templateMinY*baseScale,model.Template.bounds.center.z*baseScale);
        float minY=replacement.bounds.min.y;
        float floor=groundFound?groundY:feet.y;floorCorrectionY=Math.Max(0,floor-minY);
        if(floorCorrectionY>0)visual.transform.position+=Vector3.up*floorCorrectionY;
        ApplyNativeSpawnOffset(feet.y);
    }
    void LateUpdate()
    {
        if(enemy==null || model==null || Time.unscaledTime<retryAt)return;
        try
        {
            bool checkAssets=Time.unscaledTime>=nextAssetCheck;if(checkAssets)nextAssetCheck=Time.unscaledTime+1;
            if(needsRebind || original==null || visual==null || replacement==null || filter==null || (checkAssets && !model.IsUsable(false)))
            {
                var live=Plugin.GetModel(model.Key,needsRebind);if(live==null)return;Bind(enemy,live,isBoss);
            }
            SuppressNativeBodies();
            if(enemy.hp<=0){replacement.enabled=false;return;}
            replacement.enabled=true;
            SyncGhostPalette();
            SyncSourceMaterials();
            float animationNow=EnemyAnimationClock.Now;
            if(!EnemyAnimationClock.CanAnimate){if(filter.sharedMesh!=null)PlaceAboveNativeFloor(filter.sharedMesh);return;}
            if(animationNow<nextFrame)return;nextFrame=animationNow+1f/24;
            var position=enemy.transform.position;
            if(enemy.rb!=null)
            {
                var velocity=enemy.rb.velocity;velocity.y=0;
                if(velocity.sqrMagnitude>.001f)visual.transform.rotation=Quaternion.LookRotation(velocity);
            }
            string name=(position-previous).sqrMagnitude>.00001f?"run":"idle";
            if(model.Name.Equals("Tarantula",StringComparison.OrdinalIgnoreCase) && name=="run")name="walk";
            previous=position;
            if(!model.Clips.TryGetValue(name,out var clip))return;
            if(clipName!=name){clipName=name;lastFrame=-1;startTime=EnemyAnimationClock.Now;}
            int frame=(int)(((animationNow-startTime)%clip.Duration)/clip.Duration*(clip.Count-1));
            var mesh=clip.Meshes[frame];if(mesh==null)throw new InvalidOperationException("Original enemy animation frame was unloaded: "+model.Key+"/"+name+"/"+frame);
            if(frame!=lastFrame){lastFrame=frame;filter.sharedMesh=mesh;}
            PlaceAboveNativeFloor(mesh);
            SetSourceRenderFrame(mesh);
            SyncGhostPalette();
        }
        catch(Exception ex)
        {
            needsRebind=true;retryAt=Time.unscaledTime+1;Plugin.Recoveries++;
            RestoreNativeBodies();if(original!=null)original.enabled=true;if(replacement!=null)replacement.enabled=false;
            if(Time.unscaledTime>=nextFailureLog){nextFailureLog=Time.unscaledTime+5;Plugin.Logger.LogWarning("Enemy source visual will retry after native scene/asset change: "+ex);}
        }
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    public string Describe()
    {
        float renderedHeight=replacement!=null ? replacement.bounds.size.y : 0;
        var bounds=replacement!=null?replacement.bounds:new Bounds();
        var camera=Camera.main;bool canVisible=EnemyVisualGround.CameraVisible(camera,replacement,out var viewport);
        return JsonSerializer.Serialize(new {
            animationClock=EnemyAnimationClock.Snapshot(),enemyName=enemy?.enemyData?.enemyName.ToString(),
            enemyId=enemy!=null ? enemy.id : 0,
            boss=isBoss, stageBoss=enemy!=null && enemy.IsStageBoss(), finalBoss=enemy!=null && enemy.IsFinalBoss(),
            model=model?.Name, scaleReference,
            nativeRendererHeight=nativeRendererBounds.size.y,
            nativeColliderHeight=nativeColliderBounds.size.y,
            referenceHeight, sizeMultiplier, sourceHeight,
            expectedInitialHeight=referenceHeight*sizeMultiplier,
            renderedHeight,nativeState=enemy!=null?enemy.state.ToString():"",nativeSpawnOffsetY,nativeFeetY,nativeGroundCheckY,nativeBottomY=enemy!=null?enemy.GetBottomPosition().y:0,groundLayerMask=EnemyVisualGround.GroundLayerMask,nativeRendererFeetY=nativeRendererBounds.min.y,nativeColliderFeetY=nativeColliderBounds.min.y,
            visualMinY=bounds.min.y,visualMaxY=bounds.max.y,groundY,groundFound,groundSampleFeetY,floorCorrectionY,templateMinY,frameMinY,frameMaxY,
            groundClearance=bounds.min.y-groundY,cameraViewport=new[]{viewport.x,viewport.y,viewport.z},canVisible,
            cameraName=camera!=null?camera.name:"",currentClip=clipName,currentFrame=lastFrame,modelAssetsValid=model!=null && model.IsUsable(false),
            currentLocalScale=visual!=null ? visual.transform.localScale.y : 0,
            nativeRendererHidden=original!=null && !original.enabled,
            visualActive=visual!=null && visual.activeInHierarchy,
            colliderEnabled=enemy?.collider!=null && enemy.collider.enabled
        });
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    public void RestoreNative()
    {
        enabled=false;
        ResetSourcePresentation();
        ReleaseOwnership();
        if(original!=null) original.enabled=true;
        if(visual!=null){visual.SetActive(false);Object.Destroy(visual);}
        original=null;enemy=null;filter=null;replacement=null;visual=null;
    }
    void OnDestroy() { ResetSourcePresentation();ReleaseOwnership();if(visual!=null)Object.Destroy(visual); }
}



/// <summary>Read-only runtime evidence for native spawn and size checks.</summary>
public static partial class EnemyVisualBridge
{
    public static string AnimationDiagnostics()=>JsonSerializer.Serialize(new{clock=EnemyAnimationClock.Snapshot(),enemies=DescribeAll()});
    public static bool AllCachedAssetsValid=>Plugin.Models.Count>0 && Plugin.Models.Values.All(m=>m.IsUsable());
    public static string AssetDiagnostics()=>JsonSerializer.Serialize(new{allValid=AllCachedAssetsValid,sourceCount=Plugin.Models.Count,expectedSourceCount=Plugin.ModelKeys.Length,allElevenPrimed=Plugin.Models.Count==Plugin.ModelKeys.Length && AllCachedAssetsValid,modelLoads=Plugin.ModelLoads,recoveries=Plugin.Recoveries,models=Plugin.Models.Select(x=>new{key=x.Key,health=x.Value.Inspect()}).ToArray(),failedKeys=Plugin.FailedModels.ToArray()});
    public static bool PrimeAllModelsForProbe()
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return false;
        foreach(var key in Plugin.ModelKeys)if(Plugin.GetModel(key)==null)return false;
        return Plugin.Models.Count==Plugin.ModelKeys.Length && AllCachedAssetsValid;
    }
    public const float OrdinarySizeMultiplier=2f/3f;
    public const float BossSizeMultiplier=1f;
    public static string Describe(Enemy enemy)
    {
        var visual=enemy!=null ? enemy.gameObject.GetComponent<AzerothVisual>() : null;
        return visual!=null ? visual.Describe() : "{\"attached\":false}";
    }
    public static string[] DescribeAll()
        => Object.FindObjectsOfType<AzerothVisual>(true).Select(v=>v.Describe()).ToArray();
}
