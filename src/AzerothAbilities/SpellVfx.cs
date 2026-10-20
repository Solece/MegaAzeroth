using System.Text.Json;
using System.Runtime.InteropServices;
using BepInEx;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object=UnityEngine.Object;
#pragma warning disable CS0649 // Data fields are assigned by System.Text.Json.

namespace AzerothAbilities;

internal static class SpellVfx
{
    internal static readonly JsonSerializerOptions JsonOptions=new(){PropertyNameCaseInsensitive=true,IncludeFields=true};
    private static string root;
    private static readonly Dictionary<string,Dictionary<string,List<string>>> map=new();
    private static readonly Dictionary<string,EffectAsset> assets=new();
    private static readonly Dictionary<string,Texture2D> textures=new();
    private static readonly Dictionary<string,(int Width,int Height)> sizes=new();
    private static readonly List<SourceSpellEffect> active=new();
    private static readonly HashSet<string> failed=new();
    private static readonly List<Material> instanceMaterials=new();
    internal static int Spawned,RecoveredTextures,RecoveredGeometry;
    internal static int Live{get{active.RemoveAll(fx=>!Alive(fx));return active.Count;}}
    // CLR dictionaries are not native Unity roots. The map loader calls
    // UnloadUnusedAssets between stages while no renderer uses these caches.
    internal static bool Alive(Object value)
    {
        try{return value!=null;}catch{return false;}
    }
    internal static T Keep<T>(T value) where T:Object
    {
        value.hideFlags|=HideFlags.DontUnloadUnusedAsset;return value;
    }
    internal static void DestroyMaterial(Material material)
    {
        instanceMaterials.Remove(material);if(Alive(material))Object.Destroy(material);
    }
    internal static void EnsureGeometry(EffectAsset asset)
    {
        if(asset.NativeGeometryValid)return;
        asset.Bake(root);RecoveredGeometry++;
    }
    internal static string ResourceSnapshot()
    {
        var meshes=new List<Mesh>();
        foreach(var effect in assets.Values)if(effect.Geometry!=null)foreach(var node in effect.Geometry.Nodes)if(node.Frames!=null)meshes.AddRange(node.Frames);
        var cachedTextures=textures.Values.ToArray();var materials=instanceMaterials.ToArray();
        bool Pinned(Object value)=>Alive(value) && (value.hideFlags&HideFlags.DontUnloadUnusedAsset)!=0;
        return JsonSerializer.Serialize(new{
            CachedEffects=assets.Count,CachedTextures=cachedTextures.Length,CachedMeshes=meshes.Count,
            InvalidTextures=cachedTextures.Count(t=>!Alive(t)),InvalidMeshes=meshes.Count(m=>!Alive(m)),
            PinnedTextures=cachedTextures.Count(t=>Pinned(t)),PinnedMeshes=meshes.Count(m=>Pinned(m)),
            ActiveMaterials=materials.Length,InvalidMaterials=materials.Count(m=>!Alive(m)),PinnedMaterials=materials.Count(m=>Pinned(m)),
            FailedEffects=failed.Count,ActiveEffects=Live,EffectAges=active.Where(fx=>Alive(fx)).Select(fx=>fx.Age).ToArray(),Spawned,RecoveredTextures,RecoveredGeometry});
    }
    internal static void PrimeAllAssets()
    {
        var keys=new HashSet<string>();
        foreach(var stages in map.Values)foreach(var stage in stages.Values)foreach(var key in stage)keys.Add(key);
        foreach(var key in keys)
        {
            var effect=Asset(key);var names=new HashSet<string>();
            foreach(var emitter in effect.Emitters)names.Add(emitter.Texture);
            foreach(var ribbon in effect.Ribbons)names.Add(ribbon.Texture);
            if(effect.Geometry!=null)foreach(var node in effect.Geometry.Nodes)names.Add(node.Texture);
            foreach(var name in names)Texture(name);
        }
    }
    internal static void Initialize()
    {
        root=Path.Combine(Paths.PluginPath,"AzerothAbilities","vfx");
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"manifest.json")));
        foreach(var t in json.RootElement.GetProperty("textures").EnumerateArray())sizes[t.GetProperty("key").GetString()]=(t.GetProperty("width").GetInt32(),t.GetProperty("height").GetInt32());
        foreach(var spell in json.RootElement.GetProperty("spells").EnumerateArray())
        {
            var stages=new Dictionary<string,List<string>>();
            foreach(var e in spell.GetProperty("effects").EnumerateArray())
            {
                var stage=e.GetProperty("stage").GetString();var key=e.GetProperty("key").GetString();
                if(!stages.TryGetValue(stage,out var keys))stages[stage]=keys=new();
                if(!keys.Contains(key))keys.Add(key);
            }
            map[spell.GetProperty("id").GetString()]=stages;
        }
        ClassInjector.RegisterTypeInIl2Cpp<SourceSpellEffect>();
        if(map.TryGetValue("frostbolt",out var frost))map["water_elemental"]=frost;
        Plugin.Current.Log.LogInfo($"Source Mage VFX ready: {map.Count} spell mappings, {sizes.Count} original textures; native M2 geometry/particle parameters adapted to Unity.");
    }
    internal static Texture2D Texture(string key)
    {
        if(textures.TryGetValue(key,out var found))
        {
            if(Alive(found))return found;
            textures.Remove(key);RecoveredTextures++;
        }
        var s=sizes[key];var bytes=File.ReadAllBytes(Path.Combine(root,key+".rgba"));
        if(bytes.Length!=s.Width*s.Height*4)throw new InvalidDataException("VFX RGBA size mismatch");
        var texture=Keep(new Texture2D(s.Width,s.Height,TextureFormat.RGBA32,false){name="AzerothVfxTexture_"+key});var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
        try{texture.LoadRawTextureData(pin.AddrOfPinnedObject(),bytes.Length);texture.Apply(false,true);}finally{pin.Free();}
        texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;textures[key]=texture;return texture;
    }
    internal static EffectAsset Asset(string key)
    {
        if(assets.TryGetValue(key,out var found)){EnsureGeometry(found);return found;}
        found=JsonSerializer.Deserialize<EffectAsset>(File.ReadAllText(Path.Combine(root,key+".json")),JsonOptions);
        found.Bake(root);assets[key]=found;return found;
    }
    internal static Material Material(string texture,int blend)
    {
        var shader=Shader.Find("Particles/Standard Unlit")??Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply")??Shader.Find("Sprites/Default");
        if(shader==null)throw new InvalidOperationException("Native particle shader unavailable");
        var m=Keep(new Material(shader){name="AzerothVfxMaterial_"+texture,mainTexture=Texture(texture),color=Color.white,renderQueue=3000});
        m.SetFloat("_Mode",blend==4?1:2);m.SetFloat("_ZWrite",0);m.SetFloat("_Cull",0);
        m.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend",(float)(blend==4?UnityEngine.Rendering.BlendMode.One:UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.EnableKeyword("_ALPHABLEND_ON");m.DisableKeyword("_ALPHATEST_ON");m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        instanceMaterials.Add(m);return m;
    }
    internal static void Play(string id,string stage,Vector3 position,float scale=1,float duration=0)
    {
        if(!AbilityBehaviour.RenderVisuals || !Plugin.Current.SpellVisuals.Value)return;
        Spawn(id,stage,position,scale,duration>0?duration:.65f,false);
    }
    internal static GameObject Projectile(string id,Vector3 position,float scale=1)=>Spawn(id,"missile",position,scale,0,true);
    private static GameObject Spawn(string id,string stage,Vector3 position,float scale,float life,bool loop)
    {
        if(!map.TryGetValue(id,out var stages) || Live>=64)return null;
        if(!stages.TryGetValue(stage,out var keys))
        {
            var fallback=stage is "area" or "areaKit"?new[]{"areaKit","impact","cast"}:stage=="aura"?new[]{"cast","impact"}:new[]{"impact","cast"};
            keys=fallback.Select(s=>stages.TryGetValue(s,out var x)?x:null).FirstOrDefault(x=>x!=null);
        }
        if(keys==null)return null;
        GameObject first=null;
        var group=loop?new GameObject("AzerothProjectileVfx_"+id):null;
        if(group!=null){group.transform.position=position;group.transform.localScale=Vector3.one*Mathf.Clamp(scale,.1f,10);}
        foreach(var key in keys)
        {
            if(failed.Contains(key))continue;
            try
            {
                var asset=Asset(key);var go=new GameObject("AzerothVfx_"+id+"_"+stage);
                if(group!=null)go.transform.SetParent(group.transform,false);
                else{go.transform.position=position;go.transform.localScale=Vector3.one*Mathf.Clamp(scale,.1f,10);}
                var fx=go.AddComponent<SourceSpellEffect>();fx.Init(asset,life,loop);active.Add(fx);Spawned++;
                first??=go;
            }
            catch(Exception ex){failed.Add(key);Plugin.Current.Log.LogError("Source spell VFX failed "+id+"/"+stage+": "+ex);}
        }
        if(group!=null && first==null){Object.Destroy(group);return null;}
        return group??first;
    }
    internal static void Release(GameObject go)
    {
        if(go==null)return;
        var effect=go.GetComponent<SourceSpellEffect>();
        if(effect!=null){effect.Stop();return;}
        foreach(var child in go.GetComponentsInChildren<SourceSpellEffect>())
        {child.Stop();child.transform.SetParent(null,true);}
        Object.Destroy(go);
    }
    internal static void Removed(SourceSpellEffect effect)=>active.Remove(effect);
    internal static void Clear()
    {
        // Clear scene instances only. Baked meshes and uploaded textures belong
        // to the plugin and remain available for the next native map stage.
        foreach(var fx in active.ToArray())if(Alive(fx))Object.Destroy(fx.gameObject);
        active.Clear();failed.Clear();
    }
}

public static class SpellVfxDiagnostics
{
    // Read on the Unity thread; these properties inspect native resource state.
    public static string SnapshotJson=>SpellVfx.ResourceSnapshot();
    public static bool PrimeAllAssetsForProbe()
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return false;
        SpellVfx.PrimeAllAssets();return true;
    }
    public static bool PlayForProbe(string id,string stage,Vector3 position,float scale=1)
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return false;
        int count=SpellVfx.Spawned;SpellVfx.Play(id,stage,position,scale,1);return SpellVfx.Spawned>count;
    }
}

internal sealed class Curve
{
    public int Interpolation,GlobalSequence=-1;public int[] Times=Array.Empty<int>();public JsonElement Values;
    internal float At(float ms,float fallback=0,int component=0)
    {
        if(Values.ValueKind!=JsonValueKind.Array || Values.GetArrayLength()==0 || Times.Length==0)return fallback;
        int i=0;while(i+1<Times.Length && Times[i+1]<=ms)i++;
        float Value(int n){var v=Values[Math.Min(n,Values.GetArrayLength()-1)];return v.ValueKind==JsonValueKind.Array?v[Math.Min(component,v.GetArrayLength()-1)].GetSingle():v.GetSingle();}
        var a=Value(i);if(i+1>=Times.Length || Interpolation==0)return a;
        return Mathf.Lerp(a,Value(i+1),Mathf.Clamp01((ms-Times[i])/Math.Max(1,Times[i+1]-Times[i])));
    }
}
internal sealed class EmitterAsset
{
    public string Texture;public uint Flags;public int Blend,Shape,Rows=1,Cols=1;public float[] Position=Array.Empty<float>();
    public Dictionary<string,Curve> Tracks=new();public float Mid=.5f;public float[][] Colors=Array.Empty<float[]>();public float[] Sizes=Array.Empty<float>();public float Drag,Spin,TailLength;public Curve Enabled;
}
internal sealed class MeshAsset
{
    public string Texture;public int[] Triangles=Array.Empty<int>();public int Blend;public Curve Color,Alpha,Weight;
    internal Mesh[] Frames;
}
internal sealed class GeometryAsset
{
    public int VertexCount,FrameCount;public string File;public float Duration;public float[] Uv=Array.Empty<float>();public List<MeshAsset> Nodes=new();
}
internal sealed class EffectAsset
{
    public string Source;public float Duration;public int StartMs;public int[] Globals=Array.Empty<int>();public List<EmitterAsset> Emitters=new();public GeometryAsset Geometry;
    public List<RibbonAsset> Ribbons=new();
    internal bool NativeGeometryValid=>Geometry==null || Geometry.Nodes.All(node=>node.Frames!=null && node.Frames.Length==Geometry.FrameCount && node.Frames.All(mesh=>SpellVfx.Alive(mesh)));
    internal void Bake(string root)
    {
        if(Geometry==null)return;
        foreach(var node in Geometry.Nodes)if(node.Frames!=null)foreach(var frame in node.Frames)if(SpellVfx.Alive(frame))Object.Destroy(frame);
        var g=Geometry;var bytes=System.IO.File.ReadAllBytes(Path.Combine(root,g.File));
        if(bytes.Length!=g.VertexCount*g.FrameCount*12)throw new InvalidDataException("VFX frame size mismatch");
        var values=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,values,0,bytes.Length);
        var uv=new Vector2[g.VertexCount];for(int j=0;j<uv.Length;j++)uv[j]=new(g.Uv[j*2],g.Uv[j*2+1]);
        foreach(var node in g.Nodes)
        {
            node.Frames=new Mesh[g.FrameCount];
            for(int f=0;f<g.FrameCount;f++)
            {
                var verts=new Vector3[g.VertexCount];for(int j=0;j<verts.Length;j++){int p=(f*g.VertexCount+j)*3;verts[j]=new(values[p],values[p+1],values[p+2]);}
                var mesh=SpellVfx.Keep(new Mesh{name="AzerothSourceVfx_"+Source});mesh.vertices=verts;mesh.uv=uv;mesh.triangles=node.Triangles;mesh.RecalculateBounds();node.Frames[f]=mesh;
            }
        }
    }
    internal float Millis(float elapsed,Curve curve)
    {
        if(curve!=null && curve.GlobalSequence>=0 && curve.GlobalSequence<Globals.Length && Globals[curve.GlobalSequence]>0)return elapsed*1000%Globals[curve.GlobalSequence];
        return StartMs+elapsed*1000;
    }
}

public sealed class SourceSpellEffect:MonoBehaviour
{
    public SourceSpellEffect(IntPtr pointer):base(pointer){}
    private EffectAsset asset;private float began,life,stopAt=-1,next;private bool loop;
    public float Age=>Mathf.Max(0,CombatSimulation.Time-began);
    private readonly List<ParticleLayer> particles=new();private readonly List<GeometryLayer> geometry=new();
    private readonly List<RibbonLayer> ribbons=new();
    [HideFromIl2Cpp]
    internal void Init(EffectAsset source,float duration,bool repeat)
    {
        asset=source;began=CombatSimulation.Time;life=duration;loop=repeat;
        foreach(var e in asset.Emitters)particles.Add(new ParticleLayer(transform,e,asset));
        foreach(var r in asset.Ribbons)ribbons.Add(new RibbonLayer(transform,r,asset));
        if(asset.Geometry!=null)foreach(var m in asset.Geometry.Nodes)geometry.Add(new GeometryLayer(transform,m,asset));
    }
    [HideFromIl2Cpp]
    internal void Stop(){if(stopAt<0)stopAt=CombatSimulation.Time;}
    void Update()
    {
        if(asset==null || !CombatSimulation.CanSimulate)return;
        float now=CombatSimulation.Time;if(now<next)return;float dt=next==0?1f/30:Mathf.Clamp(now-next+1f/30,0,.1f);next=now+1f/30;
        float elapsed=now-began;
        if(!loop && stopAt<0 && elapsed>=life)Stop();
        if(stopAt>=0 && now-stopAt>3){Object.Destroy(gameObject);return;}
        float animation=(loop || life>asset.Duration)?elapsed%Math.Max(.1f,asset.Duration):elapsed;
        foreach(var layer in particles)layer.Tick(dt,elapsed,animation,stopAt<0);
        foreach(var layer in geometry)layer.Tick(animation,stopAt<0?1:Mathf.Max(0,1-(now-stopAt)/.35f));
        foreach(var layer in ribbons)layer.Tick(elapsed,animation,stopAt<0);
    }
    void OnDestroy(){foreach(var p in particles)p.Dispose();foreach(var g in geometry)g.Dispose();foreach(var r in ribbons)r.Dispose();SpellVfx.Removed(this);}
}

internal sealed class RibbonAsset
{
    public string Texture;public int Blend;public float[] Position=Array.Empty<float>();public Curve Color,Alpha,Above,Below,Visible;public float EdgesPerSecond,Life;
}
internal sealed class RibbonLayer
{
    readonly GameObject go;readonly Transform owner;readonly Mesh mesh;readonly Material material;readonly RibbonAsset source;readonly EffectAsset asset;
    readonly List<(Vector3 Position,float Time)> trail=new();float nextPoint;
    internal RibbonLayer(Transform parent,RibbonAsset ribbon,EffectAsset model)
    {
        owner=parent;source=ribbon;asset=model;go=new GameObject("OriginalM2Ribbon");go.transform.SetParent(parent,false);
        mesh=SpellVfx.Keep(new Mesh{name="OriginalM2RibbonTrail"});mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;
        material=SpellVfx.Material(ribbon.Texture,ribbon.Blend);go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }
    internal void Tick(float elapsed,float animation,bool emitting)
    {
        float life=Mathf.Clamp(source.Life,.03f,3);
        while(trail.Count>0 && elapsed-trail[0].Time>life)trail.RemoveAt(0);
        if(emitting && elapsed>=nextPoint)
        {
            nextPoint=elapsed+1/Mathf.Clamp(source.EdgesPerSecond,15,90);
            trail.Add((owner.TransformPoint(new Vector3(source.Position[0],source.Position[1],source.Position[2])),elapsed));
        }
        var vertices=new Vector3[trail.Count*2];var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];var triangles=new int[Math.Max(0,trail.Count-1)*6];
        var camera=Camera.main;var cameraForward=camera!=null?camera.transform.forward:Vector3.forward;
        float above=source.Above?.At(asset.Millis(animation,source.Above),.1f)??.1f,below=source.Below?.At(asset.Millis(animation,source.Below),.1f)??.1f;
        var color=new Color(source.Color?.At(asset.Millis(animation,source.Color),1,0)??1,source.Color?.At(asset.Millis(animation,source.Color),1,1)??1,source.Color?.At(asset.Millis(animation,source.Color),1,2)??1,(source.Alpha?.At(asset.Millis(animation,source.Alpha),32767)??32767)/32767);
        for(int i=0;i<trail.Count;i++)
        {
            var p=trail[i];var direction=(i+1<trail.Count?trail[i+1].Position:p.Position+owner.forward)-p.Position;
            var side=Vector3.Cross(direction.normalized,cameraForward).normalized;if(side.sqrMagnitude<.001f)side=Vector3.up;
            vertices[i*2]=owner.InverseTransformPoint(p.Position+side*above*owner.lossyScale.x);vertices[i*2+1]=owner.InverseTransformPoint(p.Position-side*below*owner.lossyScale.x);
            float age=Mathf.Clamp01((elapsed-p.Time)/life);uv[i*2]=new(age,0);uv[i*2+1]=new(age,1);var c=color;c.a*=1-age;colors[i*2]=colors[i*2+1]=c;
            if(i+1<trail.Count){int k=i*6,v=i*2;triangles[k]=v;triangles[k+1]=v+1;triangles[k+2]=v+2;triangles[k+3]=v+1;triangles[k+4]=v+3;triangles[k+5]=v+2;}
        }
        mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;mesh.RecalculateBounds();
    }
    internal void Dispose(){if(SpellVfx.Alive(mesh))Object.Destroy(mesh);SpellVfx.DestroyMaterial(material);if(SpellVfx.Alive(go))Object.Destroy(go);}
}

internal sealed class GeometryLayer
{
    readonly GameObject go;readonly MeshFilter filter;readonly Material material;readonly MeshAsset source;readonly EffectAsset asset;
    internal GeometryLayer(Transform parent,MeshAsset node,EffectAsset model)
    {
        source=node;asset=model;go=new GameObject("OriginalM2Geometry");go.transform.SetParent(parent,false);
        filter=go.AddComponent<MeshFilter>();material=SpellVfx.Material(node.Texture,node.Blend);go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }
    internal void Tick(float time,float fade)
    {
        // Normal rendering checks only the selected frame. Deep validation of
        // all cached animation meshes belongs to load/recovery and probes.
        if(source.Frames==null || source.Frames.Length!=asset.Geometry.FrameCount)SpellVfx.EnsureGeometry(asset);
        if(source.Frames==null || source.Frames.Length==0)return;
        int index=Mathf.Clamp((int)(time/Math.Max(.1f,asset.Geometry.Duration)*(source.Frames.Length-1)),0,source.Frames.Length-1);
        if(!SpellVfx.Alive(source.Frames[index]))SpellVfx.EnsureGeometry(asset);
        filter.sharedMesh=source.Frames[index];
        float alpha=(source.Alpha?.At(asset.Millis(time,source.Alpha),32767)??32767)/32767*(source.Weight?.At(asset.Millis(time,source.Weight),32767)??32767)/32767*fade;
        material.color=new Color(source.Color?.At(asset.Millis(time,source.Color),1,0)??1,source.Color?.At(asset.Millis(time,source.Color),1,1)??1,source.Color?.At(asset.Millis(time,source.Color),1,2)??1,Mathf.Clamp01(alpha));
    }
    internal void Dispose(){SpellVfx.DestroyMaterial(material);if(SpellVfx.Alive(go))Object.Destroy(go);}
}
internal sealed class ParticleLayer
{
    sealed class Particle{internal Vector3 Position,Velocity;internal float Age,Life,Spin,Size;}
    readonly Transform owner;readonly EmitterAsset source;readonly EffectAsset asset;readonly GameObject go;readonly Mesh mesh;readonly Material material;
    readonly List<Particle> live=new();readonly System.Random random=new();float budget;
    const int Capacity=192;
    internal ParticleLayer(Transform parent,EmitterAsset emitter,EffectAsset model)
    {
        owner=parent;source=emitter;asset=model;go=new GameObject("OriginalM2Emitter");go.transform.SetParent(parent,false);
        mesh=SpellVfx.Keep(new Mesh{name="OriginalM2ParticleBillboards"});mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;material=SpellVfx.Material(emitter.Texture,emitter.Blend);go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }
    float Random()=> (float)random.NextDouble();
    float Parameter(string key,float time,float fallback=0)=>source.Tracks.TryGetValue(key,out var curve)?curve.At(asset.Millis(time,curve),fallback):fallback;
    internal void Tick(float dt,float elapsed,float animation,bool emitting)
    {
        float rate=Mathf.Clamp(Parameter("rate",animation),0,1600),gravity=Parameter("gravity",animation);
        if(emitting && (source.Enabled==null || source.Enabled.Values.GetArrayLength()==0 || source.Enabled.At(asset.Millis(animation,source.Enabled),1)>0))budget+=rate*dt;
        int born=Math.Min(Capacity-live.Count,(int)budget);budget=Math.Min(1,budget-born);
        var basePosition=new Vector3(source.Position[0],source.Position[1],source.Position[2]);
        for(int i=0;i<born;i++)
        {
            float angle=Random()*Mathf.PI*2,polar=Random()*Parameter("vertical",animation,Mathf.PI),horizontal=Parameter("horizontal",animation,Mathf.PI*2);
            var direction=source.Shape==2?new Vector3(Mathf.Cos(angle)*Mathf.Sin(polar),Mathf.Cos(polar),Mathf.Sin(angle)*Mathf.Sin(polar)):new Vector3(Mathf.Sin(polar)*Mathf.Cos(horizontal*(Random()-.5f)),Mathf.Cos(polar),Mathf.Sin(polar)*Mathf.Sin(horizontal*(Random()-.5f)));
            var offset=new Vector3((Random()-.5f)*Parameter("width",animation),0,(Random()-.5f)*Parameter("length",animation));
            float speed=Parameter("speed",animation)*(1+(Random()*2-1)*Parameter("variation",animation));
            live.Add(new Particle{Position=owner.TransformPoint(basePosition+offset),Velocity=owner.TransformDirection(direction)*speed*owner.lossyScale.x,Age=0,Life=Mathf.Clamp(Parameter("life",animation,.4f),.03f,3),Spin=Random()*Mathf.PI*2,Size=.8f+Random()*.4f});
        }
        for(int i=live.Count-1;i>=0;i--)
        {
            var p=live[i];p.Age+=dt;if(p.Age>=p.Life){live.RemoveAt(i);continue;}
            p.Velocity+=Vector3.down*gravity*dt;p.Velocity*=Mathf.Exp(-Mathf.Max(0,source.Drag)*dt);p.Position+=p.Velocity*dt;
        }
        var camera=Camera.main;var right=camera!=null?camera.transform.right:Vector3.right;var up=camera!=null?camera.transform.up:Vector3.up;
        var vertices=new Vector3[live.Count*4];var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];var triangles=new int[live.Count*6];
        for(int i=0;i<live.Count;i++)
        {
            var p=live[i];float t=p.Age/p.Life,mid=Mathf.Clamp(source.Mid,.001f,.999f);int a=t<mid?0:1,b=a+1;float blend=t<mid?t/mid:(t-mid)/(1-mid);
            float size=Mathf.Lerp(source.Sizes[a],source.Sizes[b],blend)*p.Size*2*owner.lossyScale.x;
            var ca=source.Colors[a];var cb=source.Colors[b];var color=Color.Lerp(new(ca[0],ca[1],ca[2],ca[3]),new(cb[0],cb[1],cb[2],cb[3]),blend);
            var rr=right*size;var uu=up*size;int v=i*4;
            vertices[v]=owner.InverseTransformPoint(p.Position-rr-uu);vertices[v+1]=owner.InverseTransformPoint(p.Position+rr-uu);vertices[v+2]=owner.InverseTransformPoint(p.Position+rr+uu);vertices[v+3]=owner.InverseTransformPoint(p.Position-rr+uu);
            int cells=source.Rows*source.Cols,cell=Math.Min(cells-1,(int)(t*cells));float x=(float)(cell%source.Cols)/source.Cols,y=(float)(cell/source.Cols)/source.Rows,dx=1f/source.Cols,dy=1f/source.Rows;
            uv[v]=new(x,y);uv[v+1]=new(x+dx,y);uv[v+2]=new(x+dx,y+dy);uv[v+3]=new(x,y+dy);for(int j=0;j<4;j++)colors[v+j]=color;
            int k=i*6;triangles[k]=v;triangles[k+1]=v+1;triangles[k+2]=v+2;triangles[k+3]=v;triangles[k+4]=v+2;triangles[k+5]=v+3;
        }
        mesh.Clear();mesh.vertices=vertices;mesh.uv=uv;mesh.colors=colors;mesh.triangles=triangles;mesh.RecalculateBounds();
    }
    internal void Dispose(){if(SpellVfx.Alive(mesh))Object.Destroy(mesh);SpellVfx.DestroyMaterial(material);if(SpellVfx.Alive(go))Object.Destroy(go);}
}
