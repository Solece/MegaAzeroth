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
    internal const int EffectCapacity=64,ReservedAreaCapacity=4;
    private static int peakActive;
    private static bool injectingBudgetProbe;
    private static readonly Dictionary<string,int> requests=new(),drops=new(),fallbacks=new(),spawnedStages=new(),evictions=new();
    private static void Count(Dictionary<string,int> counters,string key)=>counters[key]=counters.TryGetValue(key,out int value)?value+1:1;
    internal static string RendererSnapshot()=>JsonSerializer.Serialize(new{EffectCapacity,ReservedAreaCapacity,PeakActive=peakActive,Requested=requests,Dropped=drops,Fallbacks=fallbacks,Evictions=evictions,SpawnedStages=spawnedStages,Effects=active.Where(Alive).Select(fx=>fx.RenderSnapshot()).ToArray()});
    internal static string BindingAudit()=>JsonSerializer.Serialize(map.Select(spell=>new{Id=spell.Key,Stages=spell.Value.Select(stage=>new{Stage=stage.Key,Models=stage.Value.Select(key=>new{Key=key,Source=Asset(key).Source,Emitters=Asset(key).Emitters.Count,Ribbons=Asset(key).Ribbons.Count,GeometryNodes=Asset(key).Geometry?.Nodes.Count??0,BoneAttachments=Asset(key).BonePoses?.Bones.Length??0}).ToArray()}).ToArray()}).ToArray());
    internal static int Spawned,RecoveredTextures,RecoveredGeometry;
    internal static int Live{get{active.RemoveAll(fx=>!Alive(fx));return active.Count;}}
    internal static int NonAreaLive{get{_ = Live;return active.Count(fx=>!fx.IsArea);}}
    private static bool Admit(string id,string stage)
    {
        bool area=stage is "area" or "areaKit";int count=Live;
        if(!area&&NonAreaLive>=EffectCapacity-ReservedAreaCapacity)
        {Count(drops,"area-reserve:"+id+"/"+stage);return false;}
        if(count<EffectCapacity)return true;
        if(area)
        {
            var victim=active.Where(fx=>fx.PureTransient).OrderByDescending(fx=>fx.Age).FirstOrDefault();
            if(victim!=null)
            {
                Count(evictions,"area-admission:"+id+"/"+stage+"<-"+victim.Identity);
                // Disable immediately: native Destroy is deferred to frame end.
                // These source cast/impact instances own no gameplay object.
                victim.gameObject.SetActive(false);active.Remove(victim);Object.Destroy(victim.gameObject);return true;
            }
        }
        Count(drops,"active-effect-cap:"+id+"/"+stage);return false;
    }
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
            FailedEffects=failed.Count,ActiveEffects=Live,EffectAges=active.Where(fx=>Alive(fx)).Select(fx=>fx.Age).ToArray(),Spawned,RecoveredTextures,RecoveredGeometry,
            EffectCapacity,ReservedAreaCapacity,PeakActive=peakActive,NonAreaEffects=NonAreaLive,Requested=requests,Dropped=drops,Fallbacks=fallbacks,Evictions=evictions,SpawnedStages=spawnedStages});
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
        var allHeroes=Path.Combine(Paths.GameRootPath,"AzerothHeroAssets","vfx","assets");
        root=File.Exists(Path.Combine(allHeroes,"manifest.json"))?allHeroes:Path.Combine(Paths.PluginPath,"AzerothAbilities","vfx");
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"manifest.json")));
        foreach(var t in json.RootElement.GetProperty("textures").EnumerateArray())sizes[t.GetProperty("key").GetString()]=(t.GetProperty("width").GetInt32(),t.GetProperty("height").GetInt32());
        foreach(var spell in json.RootElement.GetProperty("spells").EnumerateArray())
        {
            var stages=new Dictionary<string,List<string>>();
            foreach(var e in spell.GetProperty("effects").EnumerateArray())
            {
                // MD20/v256 SpellVisualKit fields13/14 are sound/camera shake,
                // not SpellVisualEffectName IDs. Accept only attachment slots.
                if(e.TryGetProperty("slot",out var slot)&&slot.ValueKind==JsonValueKind.Number&&slot.GetInt32()>=13)continue;
                var stage=e.GetProperty("stage").GetString();var key=e.GetProperty("key").GetString();
                if(!stages.TryGetValue(stage,out var keys))stages[stage]=keys=new();
                if(!keys.Contains(key))keys.Add(key);
            }
            map[spell.GetProperty("id").GetString()]=stages;
        }
        ClassInjector.RegisterTypeInIl2Cpp<SourceSpellEffect>();
        if(map.TryGetValue("frostbolt",out var frost)){map["water_elemental"]=frost;map["pet_water_elemental"]=frost;}
        if(map.TryGetValue("fireball",out var fire))map["pet_imp"]=fire;
        Plugin.Current.Log.LogInfo($"Source class VFX ready: {map.Count} spell mappings, {sizes.Count} original textures; native M2 geometry/particle parameters adapted to Unity.");
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
    internal static Material Material(string texture,int blend,int materialFlags=20)
    {
        var shader=Shader.Find("Particles/Standard Unlit")??Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply")??Shader.Find("Sprites/Default");
        if(shader==null)throw new InvalidOperationException("Native particle shader unavailable");
        var m=Keep(new Material(shader){name="AzerothVfxMaterial_"+texture,mainTexture=Texture(texture),color=Color.white,renderQueue=3000});
        bool opaque=blend==0,cutout=blend==1;
        m.SetFloat("_Mode",opaque?0:cutout?1:2);
        m.SetFloat("_ZWrite",(opaque||cutout)&&(materialFlags&16)==0?1:0);
        m.SetFloat("_Cull",(materialFlags&4)!=0?0:(float)UnityEngine.Rendering.CullMode.Back);
        m.SetFloat("_SrcBlend",(float)(opaque||cutout?UnityEngine.Rendering.BlendMode.One:UnityEngine.Rendering.BlendMode.SrcAlpha));
        m.SetFloat("_DstBlend",(float)(opaque||cutout?UnityEngine.Rendering.BlendMode.Zero:blend==4?UnityEngine.Rendering.BlendMode.One:UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_Cutoff",.5f);m.renderQueue=opaque?2000:cutout?2450:3000;
        if(cutout)m.EnableKeyword("_ALPHATEST_ON");else m.DisableKeyword("_ALPHATEST_ON");
        if(!opaque&&!cutout)m.EnableKeyword("_ALPHABLEND_ON");else m.DisableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        instanceMaterials.Add(m);return m;
    }
    internal static void Play(string id,string stage,Vector3 position,float scale=1,float duration=0)
    {
        if(!AbilityBehaviour.RenderVisuals || !Plugin.Current.SpellVisuals.Value)return;
        Spawn(id,stage,position,scale,duration>0?duration:.65f,false);
    }
    internal static void PlaySourceOneShot(string id,string stage,Vector3 position,float scale=1)
    {
        if(!AbilityBehaviour.RenderVisuals || !Plugin.Current.SpellVisuals.Value)return;
        // Source clips can differ within one stage. For example Consecration's
        // base burst is1.067s while its hand model is1s; no generic cast cutoff.
        Spawn(id,stage,position,scale,0,false,true);
    }
    internal static GameObject Projectile(string id,Vector3 position,float scale=1)=>Spawn(id,"missile",position,scale,0,true);
    private static GameObject Spawn(string id,string stage,Vector3 position,float scale,float life,bool loop,bool originalOneShotDuration=false)
    {
        string request=id+"/"+stage;Count(requests,request);
        if(!map.TryGetValue(id,out var stages)){Count(drops,"unmapped-spell:"+request);return null;}
        if(!stages.TryGetValue(stage,out var keys))
        {
            var fallback=stage is "area" or "areaKit"?new[]{"areaKit","impact","cast"}:stage=="aura"?new[]{"cast","impact"}:new[]{"impact","cast"};
            keys=fallback.Select(s=>stages.TryGetValue(s,out var x)?x:null).FirstOrDefault(x=>x!=null);
            if(keys!=null)Count(fallbacks,request);
        }
        if(keys==null){Count(drops,"unmapped-stage:"+request);return null;}
        Vector3 requestedPosition=position;string anchor="requested-position";
        if(stage is "area" or "areaKit")
        {
            var manager=GameManager.Instance;int mask=manager!=null?manager.whatIsGround.value:0;
            if(mask!=0&&Physics.Raycast(position+Vector3.up*100,Vector3.down,out var hit,240,mask,QueryTriggerInteraction.Ignore))
            {position=hit.point+Vector3.up*.02f;anchor="native-ground-mask:"+mask;}
            else anchor="native-ground-query-missed";
        }
        // Consecration's original dynamicobject uses the actual radius/8;
        // the generic visual cap would diverge again above gameplay radius80.
        float effectScale=Mathf.Clamp(scale,.1f,10);
        if(id=="consecration"&&stage=="area")
        {
            if(!float.IsFinite(scale)||scale<=0){Count(drops,"invalid-consecration-area-scale:"+request);return null;}
            effectScale=scale;
        }
        GameObject first=null;
        var group=loop?new GameObject("AzerothProjectileVfx_"+id):null;
        if(group!=null){group.transform.position=position;group.transform.localScale=Vector3.one*effectScale;}
        foreach(var key in keys)
        {
            if(failed.Contains(key)){Count(drops,"previous-asset-error:"+request);continue;}
            if(!Admit(id,stage))continue;
            try
            {
                var asset=Asset(key);var go=new GameObject("AzerothVfx_"+id+"_"+stage);
                if(group!=null)go.transform.SetParent(group.transform,false);
                else{go.transform.position=position;go.transform.localScale=Vector3.one*effectScale;}
                var fx=go.AddComponent<SourceSpellEffect>();fx.Init(asset,originalOneShotDuration?asset.Duration:life,loop,id,stage,key,requestedPosition,anchor,injectingBudgetProbe,originalOneShotDuration);active.Add(fx);peakActive=Math.Max(peakActive,active.Count);Spawned++;Count(spawnedStages,request);
                first??=go;
            }
            catch(Exception ex){Count(drops,"asset-error:"+request);failed.Add(key);Plugin.Current.Log.LogError("Source spell VFX failed "+id+"/"+stage+": "+ex);}
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
    internal static string FillTransientBudgetProbe()
    {
        int before=Live,createdBefore=Spawned;var player=Assets.Scripts.Actors.Player.MyPlayer.Instance;
        if(player==null)return JsonSerializer.Serialize(new{SyntheticRenderLoad=true,Error="Native player is absent"});
        injectingBudgetProbe=true;
        try
        {
            // One original4-vertex source mesh per instance, no invented sprite
            // and no native damage/casts/items. It remains a synthetic load.
            for(int attempt=0;attempt<EffectCapacity&&NonAreaLive<EffectCapacity-ReservedAreaCapacity&&Live<EffectCapacity;attempt++)
            {
                int old=Spawned;var offset=new Vector3((attempt%5-2)*.5f,1,(attempt/5%5-2)*.5f);
                Spawn("demo_shout","impact",player.transform.position+offset,1,3,false);if(Spawned==old)break;
            }
        }
        finally{injectingBudgetProbe=false;}
        return JsonSerializer.Serialize(new{SyntheticRenderLoad=true,Source="Spells\\DemoralizingShout_Impact_Head.m2",DurationSeconds=3,BeforeActive=before,AfterActive=Live,Created=Spawned-createdBefore,
            EffectCapacity,ReservedAreaCapacity,NonAreaEffects=NonAreaLive,ReachedTransientBudget=NonAreaLive==EffectCapacity-ReservedAreaCapacity,Snapshot=JsonSerializer.Deserialize<JsonElement>(ResourceSnapshot())});
    }
    internal static string ClearTransientBudgetProbe()
    {
        int cleared=0;
        foreach(var fx in active.Where(fx=>Alive(fx)&&fx.SyntheticBudgetLoad).ToArray())
        {fx.gameObject.SetActive(false);active.Remove(fx);Object.Destroy(fx.gameObject);cleared++;}
        return JsonSerializer.Serialize(new{SyntheticRenderLoad=true,Cleared=cleared,AfterActive=Live});
    }
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
    public static string RendererSnapshotJson=>SpellVfx.RendererSnapshot();
    public static string BindingAuditJson=>SpellVfx.BindingAudit();
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

public static class VfxBudgetDiagnostics
{
    static bool Allowed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&AbilityBehaviour.RenderVisuals;
    public static string FillTransientBudgetForProbe()=>Allowed?SpellVfx.FillTransientBudgetProbe():"{\"Denied\":\"Requires rendered batch probe with HEADLESS_TEST1\"}";
    public static string ClearTransientBudgetForProbe()=>Allowed?SpellVfx.ClearTransientBudgetProbe():"{\"Denied\":\"Requires rendered batch probe with HEADLESS_TEST1\"}";
    public static string SnapshotJson=>SpellVfx.ResourceSnapshot();
}

internal sealed class Curve
{
    public int Interpolation,GlobalSequence=-1;public int[] Times=Array.Empty<int>();public int[][] Ranges=Array.Empty<int[]>();public JsonElement Values;
    internal float At(float ms,float fallback=0,int component=0)
    {
        if(Values.ValueKind!=JsonValueKind.Array || Values.GetArrayLength()==0 || Times.Length==0)return fallback;
        int start=0,end=Math.Min(Times.Length,Values.GetArrayLength())-1;
        if(GlobalSequence<0 && Ranges.Length>0 && Ranges[0].Length>=2){start=Ranges[0][0];end=Math.Min(end,Ranges[0][1]);}
        if(start<0||start>end||start>=Times.Length)return fallback;
        int i=start;while(i<end && Times[i+1]<=ms)i++;
        float Value(int n){var v=Values[Math.Min(n,Values.GetArrayLength()-1)];return v.ValueKind==JsonValueKind.Array?v[Math.Min(component,v.GetArrayLength()-1)].GetSingle():v.GetSingle();}
        var a=Value(i);if(i>=end || Interpolation==0)return a;
        return Mathf.Lerp(a,Value(i+1),Mathf.Clamp01((ms-Times[i])/Math.Max(1,Times[i+1]-Times[i])));
    }
}
internal sealed class EmitterAsset
{
    public string Texture;public uint Flags;public int Bone=-1,Blend,Shape,Rows=1,Cols=1;public float[] Position=Array.Empty<float>();
    public Dictionary<string,Curve> Tracks=new();public float Mid=.5f;public float[][] Colors=Array.Empty<float[]>();public float[] Sizes=Array.Empty<float>();public float Drag,Spin,TailLength;public Curve Enabled;
}
internal sealed class MeshAsset
{
    public string Texture;public int[] Triangles=Array.Empty<int>();public int Blend,MaterialFlags=20;public Curve Color,Alpha,Weight;
    internal Mesh[] Frames;
}
internal sealed class GeometryAsset
{
    public int VertexCount,FrameCount;public string File;public float Duration;public float[] Uv=Array.Empty<float>();public List<MeshAsset> Nodes=new();
}
internal sealed class EffectAsset
{
    public string Source;public float Duration;public int StartMs;public int[] Globals=Array.Empty<int>();public List<EmitterAsset> Emitters=new();public GeometryAsset Geometry;
    public List<RibbonAsset> Ribbons=new();public BonePoseAsset BonePoses;
    internal bool NativeGeometryValid=>Geometry==null || Geometry.Nodes.All(node=>node.Frames!=null && node.Frames.Length==Geometry.FrameCount && node.Frames.All(mesh=>SpellVfx.Alive(mesh)));
    internal void Bake(string root)
    {
        BonePoses?.Bake(root);
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
    internal Matrix4x4 Attachment(int bone,float elapsed)=>BonePoses?.At(bone,elapsed)??Matrix4x4.identity;
    internal float Millis(float animation,Curve curve,float elapsed=-1)
    {
        if(curve!=null && curve.GlobalSequence>=0 && curve.GlobalSequence<Globals.Length)return Globals[curve.GlobalSequence]>0?(elapsed>=0?elapsed:animation)*1000%Globals[curve.GlobalSequence]:0;
        return StartMs+animation*1000;
    }
}

internal sealed class BonePoseAsset
{
    public string File;public int FrameCount;public float Duration;public int[] Bones=Array.Empty<int>();
    private Matrix4x4[][] frames;
    internal void Bake(string root)
    {
        if(frames!=null)return;
        var bytes=System.IO.File.ReadAllBytes(Path.Combine(root,File));
        if(FrameCount<=0||bytes.Length!=FrameCount*Bones.Length*64)throw new InvalidDataException("VFX bone frame size mismatch");
        var values=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,values,0,bytes.Length);frames=new Matrix4x4[FrameCount][];
        for(int f=0;f<FrameCount;f++)
        {
            frames[f]=new Matrix4x4[Bones.Length];
            for(int b=0;b<Bones.Length;b++)
            {
                var matrix=Matrix4x4.identity;int at=(f*Bones.Length+b)*16;
                for(int row=0;row<4;row++)for(int column=0;column<4;column++)matrix[row,column]=values[at+row*4+column];
                frames[f][b]=matrix;
            }
        }
    }
    internal Matrix4x4 At(int bone,float time)
    {
        int index=Array.IndexOf(Bones,bone);if(index<0||frames==null)return Matrix4x4.identity;
        int frame=Mathf.Clamp((int)(time/Mathf.Max(.1f,Duration)*(FrameCount-1)),0,FrameCount-1);return frames[frame][index];
    }
}

public sealed class SourceSpellEffect:MonoBehaviour
{
    public SourceSpellEffect(IntPtr pointer):base(pointer){}
    private EffectAsset asset;private float began,life,stopAt=-1,next,lastAnimation;private bool loop,syntheticBudgetLoad,originalOneShotDuration;private string sourceId,sourceStage,sourceKey,anchor;private Vector3 requestedPosition;
    public float Age=>Mathf.Max(0,CombatSimulation.Time-began);
    internal bool IsArea=>sourceStage is "area" or "areaKit";
    internal bool PureTransient=>!loop&&sourceStage is "cast" or "impact" or "precast";
    internal bool SyntheticBudgetLoad=>syntheticBudgetLoad;
    internal string Identity=>sourceId+"/"+sourceStage;
    private readonly List<ParticleLayer> particles=new();private readonly List<GeometryLayer> geometry=new();
    private readonly List<RibbonLayer> ribbons=new();
    [HideFromIl2Cpp]
    internal void Init(EffectAsset source,float duration,bool repeat,string id,string stage,string key,Vector3 requested,string anchoring,bool synthetic,bool sourceDuration=false)
    {
        asset=source;began=CombatSimulation.Time;life=duration;loop=repeat;sourceId=id;sourceStage=stage;sourceKey=key;requestedPosition=requested;anchor=anchoring;syntheticBudgetLoad=synthetic;originalOneShotDuration=sourceDuration;
        foreach(var e in asset.Emitters)particles.Add(new ParticleLayer(transform,e,asset));
        foreach(var r in asset.Ribbons)ribbons.Add(new RibbonLayer(transform,r,asset));
        if(asset.Geometry!=null)foreach(var m in asset.Geometry.Nodes)geometry.Add(new GeometryLayer(transform,m,asset));
    }
    [HideFromIl2Cpp]
    internal void Stop(){if(stopAt<0)stopAt=CombatSimulation.Time;}
    [HideFromIl2Cpp]
    internal object RenderSnapshot()
    {
        var renderers=GetComponentsInChildren<MeshRenderer>(true);
        var position=transform.position;
        var localScale=transform.localScale;var worldScale=transform.lossyScale;
        return new{Id=sourceId,Stage=sourceStage,Key=sourceKey,Source=asset?.Source,Age,AnimationTime=lastAnimation,Stopped=stopAt>=0,SyntheticBudgetLoad=syntheticBudgetLoad,Anchor=anchor,RequestedPosition=new[]{requestedPosition.x,requestedPosition.y,requestedPosition.z},Position=new[]{position.x,position.y,position.z},
            LifeSeconds=life,SourceDurationSeconds=asset?.Duration??0,OriginalOneShotDuration=originalOneShotDuration,LocalScale=new[]{localScale.x,localScale.y,localScale.z},WorldScale=new[]{worldScale.x,worldScale.y,worldScale.z},
            BoneAttachments=asset?.BonePoses?.Bones.Length??0,ParticleLayers=particles.Select(p=>p.Snapshot()).ToArray(),
            RibbonLayers=ribbons.Select(r=>r.Snapshot()).ToArray(),
            Renderers=renderers.Select(r=>{
                var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;var material=r.sharedMaterial;var bounds=r.bounds;var color=material!=null?material.color:Color.clear;
                float Value(string property)=>material!=null&&material.HasProperty(property)?material.GetFloat(property):-1;
                return new{Name=r.gameObject.name,Enabled=r.enabled,Active=r.gameObject.activeInHierarchy,IsVisible=r.isVisible,
                    VertexCount=mesh!=null?mesh.vertexCount:0,IndexCount=mesh!=null&&mesh.subMeshCount>0?mesh.GetIndexCount(0):0,
                    BoundsCenter=new[]{bounds.center.x,bounds.center.y,bounds.center.z},BoundsSize=new[]{bounds.size.x,bounds.size.y,bounds.size.z},
                    Shader=material?.shader?.name,Texture=material?.mainTexture?.name,Alpha=color.a,Color=new[]{color.r,color.g,color.b,color.a},
                    SrcBlend=Value("_SrcBlend"),DstBlend=Value("_DstBlend"),ZWrite=Value("_ZWrite"),Cull=Value("_Cull")};
            }).ToArray()};
    }
    void Update()
    {
        if(asset==null || !CombatSimulation.CanSimulate)return;
        float now=CombatSimulation.Time;if(now<next)return;float dt=next==0?1f/30:Mathf.Clamp(now-next+1f/30,0,.1f);next=now+1f/30;
        float elapsed=now-began;
        if(!loop && stopAt<0 && elapsed>=life)Stop();
        if(stopAt>=0 && now-stopAt>3){Object.Destroy(gameObject);return;}
        float animation=(loop || life>asset.Duration)?elapsed%Math.Max(.1f,asset.Duration):elapsed;lastAnimation=animation;
        foreach(var layer in particles)layer.Tick(dt,elapsed,animation,stopAt<0);
        foreach(var layer in geometry)layer.Tick(animation,stopAt<0?1:Mathf.Max(0,1-(now-stopAt)/.35f),elapsed);
        foreach(var layer in ribbons)layer.Tick(elapsed,animation,stopAt<0);
    }
    void OnDestroy(){foreach(var p in particles)p.Dispose();foreach(var g in geometry)g.Dispose();foreach(var r in ribbons)r.Dispose();SpellVfx.Removed(this);}
}

internal sealed class RibbonAsset
{
    public string Texture;public int Bone=-1,Blend,MaterialFlags=20;public float[] Position=Array.Empty<float>();public Curve Color,Alpha,Above,Below,Visible;public float EdgesPerSecond,Life;
}
internal sealed class RibbonLayer
{
    readonly GameObject go;readonly Transform owner;readonly Mesh mesh;readonly Material material;readonly RibbonAsset source;readonly EffectAsset asset;
    readonly List<(Vector3 Position,float Time)> trail=new();float nextPoint;
    internal RibbonLayer(Transform parent,RibbonAsset ribbon,EffectAsset model)
    {
        owner=parent;source=ribbon;asset=model;go=new GameObject("OriginalM2Ribbon");go.transform.SetParent(parent,false);
        mesh=SpellVfx.Keep(new Mesh{name="OriginalM2RibbonTrail"});mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;
        material=SpellVfx.Material(ribbon.Texture,ribbon.Blend,ribbon.MaterialFlags);go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }
    internal object Snapshot()=>new{source.Bone,Points=trail.Count,LastPoint=trail.Count>0?new[]{trail[^1].Position.x,trail[^1].Position.y,trail[^1].Position.z}:Array.Empty<float>()};
    internal void Tick(float elapsed,float animation,bool emitting)
    {
        float life=Mathf.Clamp(source.Life,.03f,3);
        while(trail.Count>0 && elapsed-trail[0].Time>life)trail.RemoveAt(0);
        bool visible=source.Visible==null||source.Visible.Values.ValueKind!=JsonValueKind.Array||source.Visible.Values.GetArrayLength()==0||source.Visible.At(asset.Millis(animation,source.Visible,elapsed),1)>0;
        if(emitting && visible && elapsed>=nextPoint)
        {
            nextPoint=elapsed+1/Mathf.Clamp(source.EdgesPerSecond,15,90);
            var point=asset.Attachment(source.Bone,animation).MultiplyPoint3x4(new Vector3(source.Position[0],source.Position[1],source.Position[2]));
            trail.Add((owner.TransformPoint(point),elapsed));
        }
        var vertices=new Vector3[trail.Count*2];var uv=new Vector2[vertices.Length];var colors=new Color[vertices.Length];var triangles=new int[Math.Max(0,trail.Count-1)*6];
        var camera=Camera.main;var cameraForward=camera!=null?camera.transform.forward:Vector3.forward;
        float above=source.Above?.At(asset.Millis(animation,source.Above,elapsed),.1f)??.1f,below=source.Below?.At(asset.Millis(animation,source.Below,elapsed),.1f)??.1f;
        var color=new Color(source.Color?.At(asset.Millis(animation,source.Color,elapsed),1,0)??1,source.Color?.At(asset.Millis(animation,source.Color,elapsed),1,1)??1,source.Color?.At(asset.Millis(animation,source.Color,elapsed),1,2)??1,(source.Alpha?.At(asset.Millis(animation,source.Alpha,elapsed),32767)??32767)/32767);
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
    readonly GameObject go;readonly MeshFilter filter;readonly MeshRenderer renderer;readonly Material material;readonly MeshAsset source;readonly EffectAsset asset;
    internal GeometryLayer(Transform parent,MeshAsset node,EffectAsset model)
    {
        source=node;asset=model;go=new GameObject("OriginalM2Geometry");go.transform.SetParent(parent,false);
        filter=go.AddComponent<MeshFilter>();material=SpellVfx.Material(node.Texture,node.Blend,node.MaterialFlags);renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
    }
    internal void Tick(float time,float fade,float elapsed)
    {
        // Normal rendering checks only the selected frame. Deep validation of
        // all cached animation meshes belongs to load/recovery and probes.
        if(source.Frames==null || source.Frames.Length!=asset.Geometry.FrameCount)SpellVfx.EnsureGeometry(asset);
        if(source.Frames==null || source.Frames.Length==0)return;
        int index=Mathf.Clamp((int)(time/Math.Max(.1f,asset.Geometry.Duration)*(source.Frames.Length-1)),0,source.Frames.Length-1);
        if(!SpellVfx.Alive(source.Frames[index]))SpellVfx.EnsureGeometry(asset);
        filter.sharedMesh=source.Frames[index];
        float alpha=(source.Alpha?.At(asset.Millis(time,source.Alpha,elapsed),32767)??32767)/32767*(source.Weight?.At(asset.Millis(time,source.Weight,elapsed),32767)??32767)/32767*fade;
        renderer.enabled=alpha>0;
        material.color=new Color(source.Color?.At(asset.Millis(time,source.Color,elapsed),1,0)??1,source.Color?.At(asset.Millis(time,source.Color,elapsed),1,1)??1,source.Color?.At(asset.Millis(time,source.Color,elapsed),1,2)??1,Mathf.Clamp01(alpha));
    }
    internal void Dispose(){SpellVfx.DestroyMaterial(material);if(SpellVfx.Alive(go))Object.Destroy(go);}
}
internal sealed class ParticleLayer
{
    sealed class Particle{internal Vector3 Position,Velocity;internal float Age,Life,Spin,Size;}
    readonly Transform owner;readonly EmitterAsset source;readonly EffectAsset asset;readonly GameObject go;readonly Mesh mesh;readonly Material material;
    readonly List<Particle> live=new();readonly System.Random random=new();OriginalParticleEmission emission;float clockElapsed;Vector3 lastEmitter;
    int Capacity=>(source.Flags&0x8000)!=0?256:192;
    internal ParticleLayer(Transform parent,EmitterAsset emitter,EffectAsset model)
    {
        owner=parent;source=emitter;asset=model;go=new GameObject("OriginalM2Emitter");go.transform.SetParent(parent,false);
        mesh=SpellVfx.Keep(new Mesh{name="OriginalM2ParticleBillboards"});mesh.MarkDynamic();go.AddComponent<MeshFilter>().sharedMesh=mesh;material=SpellVfx.Material(emitter.Texture,emitter.Blend);go.AddComponent<MeshRenderer>().sharedMaterial=material;
    }
    internal object Snapshot()
    {
        var min=lastEmitter;var max=lastEmitter;float sumY=0,maxVertical=0;
        if(live.Count>0){min=live[0].Position;max=min;foreach(var p in live){min=Vector3.Min(min,p.Position);max=Vector3.Max(max,p.Position);sumY+=p.Position.y;maxVertical=Mathf.Max(maxVertical,Mathf.Abs(p.Position.y-lastEmitter.y));}}
        return new{source.Bone,source.Blend,source.Shape,Flags=source.Flags,LiveParticles=live.Count,Capacity,
            LastEmitterPosition=new[]{lastEmitter.x,lastEmitter.y,lastEmitter.z},
            ParticlePositions=live.Take(12).Select(p=>new[]{p.Position.x,p.Position.y,p.Position.z}).ToArray(),
            AllParticleCentersMinimum=new[]{min.x,min.y,min.z},AllParticleCentersMaximum=new[]{max.x,max.y,max.z},
            MeanParticleCenterY=live.Count>0?sumY/live.Count:lastEmitter.y,MaxParticleCenterVerticalOffset=maxVertical,
            TotalSpawned=emission.TotalSpawned,EmissionBursts=emission.Bursts,ClippedParticles=emission.Clipped,
            LastRequested=emission.LastRequested,LastAdmitted=emission.LastAdmitted,LastClipped=emission.LastClipped,
            BurstLatched=emission.BurstLatched};
    }
    float Random()=> (float)random.NextDouble();
    float Parameter(string key,float time,float fallback=0)=>source.Tracks.TryGetValue(key,out var curve)?curve.At(asset.Millis(time,curve,clockElapsed),fallback):fallback;
    internal void Tick(float dt,float elapsed,float animation,bool emitting)
    {
        clockElapsed=elapsed;
        float rate=Mathf.Clamp(Parameter("rate",animation),0,1600),gravity=Parameter("gravity",animation);
        bool enabled=emitting && (source.Enabled==null || source.Enabled.Values.ValueKind!=JsonValueKind.Array || source.Enabled.Values.GetArrayLength()==0 || source.Enabled.At(asset.Millis(animation,source.Enabled,elapsed),1)>0);
        int born=emission.Take(enabled?rate:0,dt,(source.Flags&0x8000)!=0,live.Count,Capacity);
        var basePosition=new Vector3(source.Position[0],source.Position[1],source.Position[2]);
        var attachment=asset.Attachment(source.Bone,animation);lastEmitter=owner.TransformPoint(attachment.MultiplyPoint3x4(basePosition));
        for(int i=0;i<born;i++)
        {
            Vector3 direction,offset;
            if(source.Shape==2)
            {
                OriginalParticleMath.Sphere(Parameter("vertical",animation,Mathf.PI),Parameter("horizontal",animation,Mathf.PI*2),Random(),Random(),out float x,out float y,out float z);
                direction=new Vector3(x,y,z);
                offset=direction*OriginalParticleMath.SphereRadius(Parameter("length",animation),Parameter("width",animation),Random());
                OriginalParticleMath.SphereVelocity(x,y,z,offset.x,offset.y,offset.z,Parameter("zSource",animation),source.Flags,out x,out y,out z);
                direction=new Vector3(x,y,z);
            }
            else
            {
                // Preserve the existing plane-emitter path; the native proof here is spherical only.
                float angle=Random()*Mathf.PI*2,polar=Random()*Parameter("vertical",animation,Mathf.PI),horizontal=Parameter("horizontal",animation,Mathf.PI*2);
                direction=new Vector3(Mathf.Sin(polar)*Mathf.Cos(horizontal*(Random()-.5f)),Mathf.Cos(polar),Mathf.Sin(polar)*Mathf.Sin(horizontal*(Random()-.5f)));
                offset=new Vector3((Random()-.5f)*Parameter("width",animation),0,(Random()-.5f)*Parameter("length",animation));
            }
            float speed=Parameter("speed",animation)*(1+(Random()*2-1)*Parameter("variation",animation));
            live.Add(new Particle{Position=owner.TransformPoint(attachment.MultiplyPoint3x4(basePosition+offset)),Velocity=owner.TransformDirection(direction)*speed*owner.lossyScale.x,Age=0,Life=Mathf.Clamp(Parameter("life",animation,.4f),.03f,3),Spin=Random()*Mathf.PI*2,Size=.8f+Random()*.4f});
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

// SOURCE_PARTICLE_MATH_BEGIN: compiled unchanged by the isolated managed checks.
internal static class OriginalParticleMath
{
    // Original sphere helper, followed by the exporter's M2 -> Unity axes [-y,z,x].
    internal static void Sphere(float vertical,float horizontal,float verticalRandom,float horizontalRandom,out float x,out float y,out float z)
    {
        float v=(verticalRandom*2-1)*vertical,h=(horizontalRandom*2-1)*horizontal;
        float cv=MathF.Cos(v);x=-MathF.Cos(h)*cv;y=MathF.Sin(v);z=-MathF.Sin(h)*cv;
    }
    internal static float SphereRadius(float length,float width,float random)=>length+random*MathF.Max(0,width-length);
    internal static void SphereVelocity(float radialX,float radialY,float radialZ,float offsetX,float offsetY,float offsetZ,float zSource,uint flags,out float x,out float y,out float z)
    {
        x=radialX;y=radialY;z=radialZ;
        if(zSource!=0)
        {
            x=offsetX;y=offsetY-zSource;z=offsetZ;
            float squared=x*x+y*y+z*z;
            float inverse=squared>0?1/MathF.Sqrt(squared):float.PositiveInfinity;
            if(inverse>0&&float.IsFinite(inverse)){x*=inverse;y*=inverse;z*=inverse;return;}
            // Original normalization fallback is positive source Z, i.e. Unity up.
            x=0;y=1;z=0;
        }
        else if((flags&0x100)!=0){x=0;y=1;z=0;}
    }
}
internal struct OriginalParticleEmission
{
    internal float Budget;
    internal bool BurstLatched;
    internal long TotalSpawned,Clipped;
    internal int Bursts,LastRequested,LastAdmitted,LastClipped;
    internal int Take(float effectiveRate,float dt,bool burst,int live,int capacity)
    {
        LastRequested=LastAdmitted=LastClipped=0;
        if(dt<=0)return 0;
        if(burst)
        {
            // A zero/disabled source rate rearms the original positive-rate edge latch.
            if(effectiveRate<=0){BurstLatched=false;Budget=0;return 0;}
            if(BurstLatched)return 0;
            BurstLatched=true;Bursts++;
            LastRequested=(int)MathF.Truncate(effectiveRate);
            LastAdmitted=Math.Min(Math.Max(0,capacity-live),LastRequested);
            LastClipped=LastRequested-LastAdmitted;
            Clipped+=LastClipped;TotalSpawned+=LastAdmitted;
            return LastAdmitted;
        }
        // Preserve the prior bounded continuous-emission budget, including its one-particle carry.
        Budget+=effectiveRate*dt;
        LastRequested=(int)Budget;
        LastAdmitted=Math.Min(Math.Max(0,capacity-live),LastRequested);
        Budget=Math.Min(1,Budget-LastAdmitted);
        LastClipped=Math.Max(0,LastRequested-LastAdmitted-(int)Budget);
        Clipped+=LastClipped;TotalSpawned+=LastAdmitted;
        return LastAdmitted;
    }
}
// SOURCE_PARTICLE_MATH_END
