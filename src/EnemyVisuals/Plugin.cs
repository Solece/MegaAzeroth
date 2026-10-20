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

[BepInPlugin(Id, "Azeroth Megabonk - Enemy Visuals", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.azerothmegabonk.enemyvisuals";
    internal static ManualLogSource Logger = null!;
    internal static readonly Dictionary<string, Model> Models = new();
    internal static readonly HashSet<string> FailedModels = new();
    internal static int Attached;
    private Harmony harmony;
    public override void Load()
    {
        Logger = Log;
        ClassInjector.RegisterTypeInIl2Cpp<AzerothVisual>();
        harmony = new Harmony(Id);
        harmony.PatchAll(typeof(Plugin).Assembly);
        // Do not make graphics calls until the game has initialized its device.
        Log.LogInfo("Enemy visual module ready; models load on first native enemy spawn.");
    }
    internal static Model GetModel(string key)
    {
        if (Models.TryGetValue(key, out var found)) return found;
        if (FailedModels.Contains(key)) return null;
        string folder = Path.Combine(Paths.GameRootPath, "AzerothAssets", key);
        if (!File.Exists(Path.Combine(folder,"model.json"))) return null;
        try { var model = Model.Load(folder); Models[key] = model; return model; }
        catch(Exception ex) { FailedModels.Add(key);Logger.LogError($"Model {key} disabled after load failure; native appearance remains available: {ex}");return null; }
    }
    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        foreach(var visual in Object.FindObjectsOfType<AzerothVisual>(true)) visual.RestoreNative();
        foreach(var model in Models.Values) model.Release();
        Models.Clear();FailedModels.Clear();return true;
    }
}

[HarmonyPatch(typeof(Enemy), nameof(Enemy.InitEnemy))]
internal static class EnemyInitPatch
{
    static void Postfix(Enemy __instance)
    {
        try
        {
            if (__instance.IsBoss() || __instance.IsStageBoss() || __instance.IsFinalBoss()) return;
            string original = __instance.enemyData.enemyName.ToString().ToLowerInvariant();
            string key = original.Contains("skeleton") ? "skeleton" : original.Contains("goblintank") ? "gnoll" : original.Contains("goblin") ? "kobold" : original.Contains("spider") || original.Contains("scorpion") ? "tarantula" : original.Contains("frog") || original.Contains("slime") ? "murloc" : original.Contains("ghoul") || original.Contains("zombie") ? "wolf" : "boar";
            var model = Plugin.GetModel(key) ?? Plugin.GetModel("boar");
            if (model == null) return;
            var visual = __instance.gameObject.GetComponent<AzerothVisual>() ?? __instance.gameObject.AddComponent<AzerothVisual>();
            visual.Bind(__instance, model);
            if (++Plugin.Attached <= 8) Plugin.Logger.LogInfo($"Visual attached to native enemy {original}; hp={__instance.hp}, model={model.Name}, collider and spawner retained.");
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
}
internal sealed class Model
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
        var model = new Model { Name=j.GetProperty("name").GetString() ?? "Azeroth creature" };
        try
        {
        model.Template = new Mesh { name="Azeroth_"+model.Name };
        model.Template.vertices = verts; model.Template.normals=normals; model.Template.uv=uvs;
        model.Template.triangles=j.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        model.Template.RecalculateBounds();
        using var tm = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"texture.rgba.json")));
        int tw=tm.RootElement.GetProperty("width").GetInt32(), th=tm.RootElement.GetProperty("height").GetInt32();
        var texture = model.Texture = new Texture2D(tw,th,TextureFormat.RGBA32,false);
        byte[] raw=File.ReadAllBytes(Path.Combine(folder,"texture.rgba"));
        if(raw.Length!=tw*th*4) throw new InvalidDataException("RGBA texture size mismatch");
        var pin=GCHandle.Alloc(raw,GCHandleType.Pinned);
        try { texture.LoadRawTextureData(pin.AddrOfPinnedObject(),raw.Length);texture.Apply(false,true); } finally { pin.Free(); }
        texture.filterMode=FilterMode.Bilinear; texture.wrapMode=TextureWrapMode.Repeat;
        var shader = Shader.Find("Unlit/Transparent Cutout") ?? Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
        if(shader==null) throw new InvalidOperationException("No cutout-compatible shader available");
        model.Material = new Material(shader) { mainTexture=texture, color=Color.white, enableInstancing=true };
        if(model.Material.HasProperty("_Cutoff")) model.Material.SetFloat("_Cutoff",0.5f);
        model.Material.SetFloat("_Mode",1); model.Material.EnableKeyword("_ALPHATEST_ON"); model.Material.renderQueue=2450;
        foreach(var c in j.GetProperty("animations").EnumerateArray())
        {
            var clip = new Clip { Name=c.GetProperty("name").GetString()!,Duration=c.GetProperty("duration").GetSingle(),Count=c.GetProperty("frameCount").GetInt32() };
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder,c.GetProperty("file").GetString()!));
            if(bytes.Length!=clip.Count*count*12) throw new InvalidDataException("Animation frame size mismatch");
            clip.Meshes=new Mesh[clip.Count];
            model.Clips[clip.Name]=clip;
            using var reader=new BinaryReader(new MemoryStream(bytes));
            for(int f=0;f<clip.Count;f++)
            {
                var frameVerts=new Vector3[count];
                for(int i=0;i<count;i++) frameVerts[i]=new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                var frameMesh=Object.Instantiate(model.Template); frameMesh.name=$"{model.Name}_{clip.Name}_{f}";
                frameMesh.vertices=frameVerts; frameMesh.RecalculateNormals(); frameMesh.RecalculateBounds();
                clip.Meshes[f]=frameMesh;
            }
            model.Clips[clip.Name]=clip;
        }
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
        if(Material!=null) Object.Destroy(Material);
        if(Texture!=null) Object.Destroy(Texture);
    }
}

public sealed class AzerothVisual : MonoBehaviour
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
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    internal void Bind(Enemy target,Model data)
    {
        enemy=target; original=target.renderer; model=data;
        if(visual!=null) { visual.SetActive(false);Object.Destroy(visual); }
        visual=new GameObject("AzerothCreature_"+data.Name);
        visual.layer=original.gameObject.layer;
        // Inherit native facing and renderer offsets; preserve physics root and hitboxes.
        visual.transform.SetParent(original.transform,false);
        visual.transform.localPosition=Vector3.zero; visual.transform.localRotation=Quaternion.identity;
        var mesh=data.Template;
        float desired=1f;
        var oldFilter=original.GetComponent<MeshFilter>();
        float parentScale=Math.Max(.00001f,Math.Abs(original.transform.lossyScale.y));
        var nativeBounds=original.bounds;
        if(target.collider!=null)
        {
            var c=target.collider;
            float worldHeight=(c.direction==1?c.height:c.radius*2)*Math.Abs(c.transform.lossyScale.y);
            nativeBounds=new Bounds(c.transform.TransformPoint(c.center),new Vector3(c.radius*2,worldHeight,c.radius*2));
        }
        desired=Math.Max(.1f,nativeBounds.size.y)/parentScale;
        float nativeBottom=(nativeBounds.min.y-original.transform.position.y)/parentScale;
        float height=Math.Max(.01f,mesh.bounds.size.y);
        float scale=Math.Max(.1f,desired)/height;
        visual.transform.localScale=Vector3.one*scale;
        visual.transform.localPosition=new Vector3(0,nativeBottom-mesh.bounds.min.y*scale,0);
        filter=visual.AddComponent<MeshFilter>(); filter.sharedMesh=mesh;
        replacement=visual.AddComponent<MeshRenderer>(); replacement.sharedMaterial=data.Material;
        if(Plugin.Attached<3) Plugin.Logger.LogInfo($"Visual scale evidence: parentScale={parentScale}, collider={nativeBounds.size}, sourceHeight={height}, localScale={scale}, visualBounds={replacement.bounds.size}, position={replacement.bounds.center}");
        original.enabled=false;
        startTime=Time.time; nextFrame=0; lastFrame=-1; clipName=""; previous=target.transform.position;
    }
    void LateUpdate()
    {
        if(enemy==null || original==null || replacement==null || filter==null || model==null) return;
        original.enabled=false;
        if(enemy.hp<=0) { replacement.enabled=false; return; }
        replacement.enabled=true;
        if(Time.time<nextFrame) return;
        nextFrame=Time.time+1f/24;
        var position=enemy.transform.position;
        string name=(position-previous).sqrMagnitude>.00001f?"run":"idle";
        if(model.Name.Equals("Tarantula",StringComparison.OrdinalIgnoreCase) && name=="run") name="walk";
        previous=position;
        if(!model.Clips.TryGetValue(name,out var clip)) return;
        if(clipName!=name){clipName=name;lastFrame=-1;startTime=Time.time;}
        int frame=(int)(((Time.time-startTime)%clip.Duration)/clip.Duration*(clip.Count-1));
        if(frame==lastFrame) return;
        lastFrame=frame;
        filter.sharedMesh=clip.Meshes[frame];
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    public void RestoreNative()
    {
        enabled=false;
        if(original!=null) original.enabled=true;
        if(visual!=null){visual.SetActive(false);Object.Destroy(visual);}
        original=null;enemy=null;filter=null;replacement=null;visual=null;
    }
    void OnDestroy() { if(original!=null)original.enabled=true;if(visual!=null)Object.Destroy(visual); }
}


