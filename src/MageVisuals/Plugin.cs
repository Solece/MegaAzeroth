using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Assets.Scripts.Actors.Player;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

[BepInPlugin(Id,"Azeroth Megabonk - Mage Visuals","0.1.1")]
public sealed class Plugin:BasePlugin
{
 public const string Id="local.azerothmegabonk.magevisuals";
 internal static ManualLogSource Logger;
 internal static Model Model;
 public override void Load(){Logger=Log;ClassInjector.RegisterTypeInIl2Cpp<MageVisual>();AddComponent<MageVisual>();AddComponent<MagePortrait>();CreatureVisualBridge.Initialize();Log.LogInfo("Original human Mage visual ready; native Megabonk movement and collisions retained.");}
 public override bool Unload(){CreatureVisualBridge.Shutdown();foreach(var v in Object.FindObjectsOfType<MageVisual>(true))v.RestoreNative();Model?.Release();Model=null;return true;}
}

public static class MageVisualBridge
{
 public static Texture PortraitTexture {get;internal set;}
 public static bool IsAttached {get;internal set;}
 public static bool SourceHeadClosed {get;internal set;}
 public static int SourceVertexCount {get;internal set;}
 public static int BindCount {get;internal set;}
 public static string CurrentClip {get;internal set;}="";
 public static Vector3[] HandWorldPositions {get;internal set;}=Array.Empty<Vector3>();
 public static Vector3 BoundsCenter {get;internal set;}
}

public sealed class MageVisual:MonoBehaviour
{
 public MageVisual(IntPtr p):base(p){}
 private PlayerMovement movement;
 private PlayerRenderer native;
 private Rigidbody body;
 private GameObject visual;
 private MeshFilter filter;
 private MeshRenderer renderer;
 private readonly List<(Renderer renderer,bool enabled)> hidden=new();
 private Model data;
 private Type bridge;
 private float nextSearch,nextFrame,clipStart,castUntil,lastHideScan;
 private string clipName="";
 private int lastCast,lastFrame=-1;
 private bool failed;

 void LateUpdate()
 {
  if(failed)return;
  try
  {
   if(bridge==null && Time.unscaledTime>=nextSearch){nextSearch=Time.unscaledTime+1;bridge=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothAbilities.HudBridge")).FirstOrDefault(t=>t!=null);}
   bool mage=bridge!=null && (bool)(bridge.GetProperty("Active")?.GetValue(null)??false);
   var player=MyPlayer.Instance;
   var current=PlayerMovement.Instance;
   if(!mage || player==null || !player.hasStarted || current==null){if(visual!=null)RestoreNative();return;}
   if(visual==null || movement!=current)Bind(current);
   if(visual==null)return;
   foreach(var saved in hidden)if(saved.renderer!=null)saved.renderer.enabled=false;
   if(Time.unscaledTime-lastHideScan>1){lastHideScan=Time.unscaledTime;HideTree(movement.transform);}
   if(Time.time<nextFrame)return;
   nextFrame=Time.time+1f/24;
   var velocity=body!=null?body.velocity:Vector3.zero;
   var horizontal=new Vector3(velocity.x,0,velocity.z);
   if(horizontal.sqrMagnitude>.04f)visual.transform.rotation=Quaternion.LookRotation(horizontal);
   int casts=(int)(bridge.GetProperty("CastCount")?.GetValue(null)??0);
   if(casts>lastCast)
   {
    lastCast=casts;castUntil=Time.time+.72f;
    var lastSpell=bridge.GetProperty("LastCastSpell")?.GetValue(null)?.ToString()??"";
    if(lastSpell=="arcane_explosion")castUntil=Time.time+.7f;
    var direction=bridge.GetProperty("LastCastDirection")?.GetValue(null);
    if(direction is Vector3 dir){dir.y=0;if(dir.sqrMagnitude>.001f)visual.transform.rotation=Quaternion.LookRotation(dir);}
   }
   string next=!movement.grounded?"jump":horizontal.sqrMagnitude>.16f?"run":Time.time<castUntil?"cast":"idle";
   if(!data.Clips.TryGetValue(next,out var clip))return;
   if(clipName!=next){clipName=next;clipStart=Time.time;lastFrame=-1;MageVisualBridge.CurrentClip=next;}
   float elapsed=Time.time-clipStart;
   float t=clip.Loop?elapsed%clip.Duration:Math.Min(elapsed,clip.Duration);
   int frame=Math.Clamp((int)(t/clip.Duration*(clip.Count-1)),0,clip.Count-1);
   if(frame!=lastFrame){lastFrame=frame;filter.sharedMesh=clip.Meshes[frame];}
   if(clip.Hands!=null)
   {
    MageVisualBridge.HandWorldPositions=new[]{visual.transform.TransformPoint(clip.Hands[frame*2]),visual.transform.TransformPoint(clip.Hands[frame*2+1])};
   }
   MageVisualBridge.BoundsCenter=renderer.bounds.center;
  }
  catch(Exception ex){failed=true;RestoreNative();Plugin.Logger.LogError("Mage visual disabled after failure; native appearance restored: "+ex);}
 }
 [HideFromIl2Cpp]
 private void Bind(PlayerMovement target)
 {
  RestoreNative();movement=target;body=target.GetComponent<Rigidbody>();
  foreach(var r in Resources.FindObjectsOfTypeAll<PlayerRenderer>())if(r!=null && r.playerMovement==target){native=r;break;}
  if(native==null || native.renderer==null)return;
  if(Plugin.Model==null)Plugin.Model=Model.Load(Path.Combine(Paths.GameRootPath,"AzerothMageAssets","human-male"));
  data=Plugin.Model;visual=new GameObject("AzerothMage_HumanMale");visual.layer=native.renderer.gameObject.layer;visual.transform.SetParent(target.transform,false);
  var collider=target.GetComponent<CapsuleCollider>();var bounds=native.renderer.bounds;
  if(collider!=null){float h=(collider.direction==1?collider.height:collider.radius*2)*Math.Abs(collider.transform.lossyScale.y);bounds=new Bounds(collider.transform.TransformPoint(collider.center),new Vector3(collider.radius*2,h,collider.radius*2));}
  float parent=Math.Max(.0001f,Math.Abs(target.transform.lossyScale.y));float scale=Math.Max(.1f,bounds.size.y)/parent/Math.Max(.01f,data.Template.bounds.size.y);
  visual.transform.localScale=Vector3.one*scale;
  var feet=bounds.center-Vector3.up*bounds.size.y*.5f;
  var sb=data.Template.bounds;visual.transform.localPosition=target.transform.InverseTransformPoint(feet)-new Vector3(sb.center.x*scale,sb.min.y*scale,sb.center.z*scale);
  var forward=target.orientation!=null?target.orientation.forward:target.transform.forward;forward.y=0;visual.transform.rotation=forward.sqrMagnitude>.001f?Quaternion.LookRotation(forward):Quaternion.identity;
  filter=visual.AddComponent<MeshFilter>();filter.sharedMesh=data.Template;renderer=visual.AddComponent<MeshRenderer>();renderer.sharedMaterial=data.Material;
  HideTree(target.transform);lastFrame=-1;clipName="";lastCast=(int)(bridge.GetProperty("CastCount")?.GetValue(null)??0);nextFrame=0;
  MageVisualBridge.IsAttached=true;MageVisualBridge.SourceHeadClosed=data.HeadClosed;MageVisualBridge.SourceVertexCount=data.VertexCount;MageVisualBridge.BindCount++;Plugin.Logger.LogInfo($"Mage visual attached: original HumanMale M2, robe item 56/display 12647, {data.Clips.Count} source clips; native renderers hidden={hidden.Count}, source height={sb.size.y}, target height={bounds.size.y}, scale={scale}. Native movement/camera/collider untouched. Source head closed={data.HeadClosed}, vertices={data.VertexCount}.");
 }
 [HideFromIl2Cpp]
 private void HideTree(Transform node)
 {
  if(node==null || visual==null || node==visual.transform || node.name.StartsWith("Azeroth",StringComparison.Ordinal))return;
  var r=node.GetComponent<Renderer>();
  if(r!=null && !hidden.Any(x=>x.renderer==r)){hidden.Add((r,r.enabled));r.enabled=false;}
  for(int i=0;i<node.childCount;i++)HideTree(node.GetChild(i));
 }
 [HideFromIl2Cpp]
 public void RestoreNative()
 {
  foreach(var saved in hidden)if(saved.renderer!=null)saved.renderer.enabled=saved.enabled;hidden.Clear();
  if(visual!=null){visual.SetActive(false);Object.Destroy(visual);}visual=null;filter=null;renderer=null;movement=null;native=null;body=null;data=null;
  MageVisualBridge.IsAttached=false;MageVisualBridge.HandWorldPositions=Array.Empty<Vector3>();MageVisualBridge.CurrentClip="";
 }
 void OnDestroy(){RestoreNative();}
}

internal sealed class Clip
{
 public string Name="";public float Duration;public int Count;public bool Loop;public Mesh[] Meshes;public Vector3[] Hands;
}

internal sealed class Model
{
 public int VertexCount;public bool HeadClosed;
 public Mesh Template;public Material Material;public Texture2D Texture;public Dictionary<string,Clip> Clips=new();
 public static Model Load(string folder)
 {
  using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"model.json")));var j=doc.RootElement;var model=new Model();
  try
  {
   float[] Floats(string key)=>j.GetProperty(key).EnumerateArray().Select(x=>x.GetSingle()).ToArray();
   var p=Floats("positions");var n=Floats("normals");var uv=Floats("uv");int count=j.GetProperty("vertexCount").GetInt32();var verts=new Vector3[count];var norms=new Vector3[count];var uvs=new Vector2[count];
   for(int i=0;i<count;i++){verts[i]=new(p[i*3],p[i*3+1],p[i*3+2]);norms[i]=new(n[i*3],n[i*3+1],n[i*3+2]);uvs[i]=new(uv[i*2],uv[i*2+1]);}
   model.VertexCount=count;
   if(j.TryGetProperty("source",out var source) && source.TryGetProperty("selectedGeosetIds",out var gs)){var ids=gs.EnumerateArray().Select(x=>x.GetInt32()).ToHashSet();model.HeadClosed=ids.Contains(1)&&ids.Contains(702)&&ids.Contains(1501);}
   model.Template=new Mesh{name="AzerothMage_OriginalHumanMale"};model.Template.vertices=verts;model.Template.normals=norms;model.Template.uv=uvs;model.Template.triangles=j.GetProperty("triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();model.Template.RecalculateBounds();
   using var tm=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"texture.rgba.json")));int w=tm.RootElement.GetProperty("width").GetInt32(),h=tm.RootElement.GetProperty("height").GetInt32();var raw=File.ReadAllBytes(Path.Combine(folder,"texture.rgba"));if(raw.Length!=w*h*4)throw new InvalidDataException("RGBA size mismatch");
   model.Texture=new Texture2D(w,h,TextureFormat.RGBA32,false);var pin=GCHandle.Alloc(raw,GCHandleType.Pinned);try{model.Texture.LoadRawTextureData(pin.AddrOfPinnedObject(),raw.Length);model.Texture.Apply(false,true);}finally{pin.Free();}model.Texture.filterMode=FilterMode.Bilinear;
   var shader=Shader.Find("Unlit/Texture")??Shader.Find("Standard")??Shader.Find("Sprites/Default");if(shader==null)throw new InvalidOperationException("No texture shader");model.Material=new Material(shader){mainTexture=model.Texture,color=Color.white};
   foreach(var c in j.GetProperty("animations").EnumerateArray())
   {
    var clip=new Clip{Name=c.GetProperty("name").GetString(),Duration=c.GetProperty("duration").GetSingle(),Count=c.GetProperty("frameCount").GetInt32(),Loop=c.GetProperty("loop").GetBoolean()};clip.Meshes=new Mesh[clip.Count];model.Clips[clip.Name]=clip;
    var bytes=File.ReadAllBytes(Path.Combine(folder,c.GetProperty("file").GetString()));if(bytes.Length!=clip.Count*count*12)throw new InvalidDataException("Animation frame length mismatch");using var r=new BinaryReader(new MemoryStream(bytes));
    for(int f=0;f<clip.Count;f++){var v=new Vector3[count];for(int i=0;i<count;i++)v[i]=new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());var mesh=Object.Instantiate(model.Template);mesh.vertices=v;mesh.RecalculateNormals();mesh.RecalculateBounds();clip.Meshes[f]=mesh;}
    if(c.TryGetProperty("attachmentsFile",out var af)){var ab=File.ReadAllBytes(Path.Combine(folder,af.GetString()));if(ab.Length!=clip.Count*2*12)throw new InvalidDataException("Attachment frame length mismatch");using var ar=new BinaryReader(new MemoryStream(ab));clip.Hands=new Vector3[clip.Count*2];for(int i=0;i<clip.Hands.Length;i++)clip.Hands[i]=new(ar.ReadSingle(),ar.ReadSingle(),ar.ReadSingle());}
   }
   Plugin.Logger.LogInfo($"Original Mage pack loaded: vertices={count}, clips={model.Clips.Count}, texture={w}x{h}, shader={shader.name}; source hand attachments 21/22 baked per frame.");return model;
  }
  catch{model.Release();throw;}
 }
 public void Release(){foreach(var c in Clips.Values)if(c.Meshes!=null)foreach(var m in c.Meshes)if(m!=null)Object.Destroy(m);if(Template!=null)Object.Destroy(Template);if(Material!=null)Object.Destroy(Material);if(Texture!=null)Object.Destroy(Texture);Clips.Clear();}
}


