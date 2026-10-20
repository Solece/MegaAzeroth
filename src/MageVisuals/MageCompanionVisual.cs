using System.Reflection;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Managers;
using BepInEx;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

// Pure visuals: neither companions nor polymorph swaps create physics objects,
// change native transforms, or replace native combat/AI ownership.
public static class CreatureVisualBridge
{
 private static readonly Dictionary<string,Model> Models=new();
 private static bool registered;
 private static GameObject tracker;
 public static int ActiveSheep {get;internal set;}
 public static int Created {get;private set;}
 public static void Initialize()
 {
  if(!registered){ClassInjector.RegisterTypeInIl2Cpp<MageCompanionVisual>();ClassInjector.RegisterTypeInIl2Cpp<MageSheepTracker>();registered=true;}
  if(tracker==null){tracker=new GameObject("AzerothCreatureVisualTracker");Object.DontDestroyOnLoad(tracker);tracker.AddComponent<MageSheepTracker>();}
 }
 public static GameObject Create(string key,Vector3 position,float height=1.6f)
 {
  Initialize();key=key.ToLowerInvariant();if(key!="sheep" && key!="waterelemental")throw new ArgumentException("Unknown original Mage creature pack",nameof(key));
  if(!Models.TryGetValue(key,out var model))
  {
   model=Model.Load(Path.Combine(Paths.GameRootPath,"AzerothMageAssets",key));
   if(key=="waterelemental")
   {
    // The source creature contains translucent water surfaces. Keep its source
    // RGBA alpha; do not turn transparent water strips into opaque polygons.
    var shader=Shader.Find("Standard");
    if(shader!=null){Object.Destroy(model.Material);model.Material=new Material(shader){mainTexture=model.Texture,color=Color.white};model.Material.SetFloat("_Mode",3);model.Material.SetInt("_SrcBlend",1);model.Material.SetInt("_DstBlend",10);model.Material.SetInt("_ZWrite",0);model.Material.EnableKeyword("_ALPHAPREMULTIPLY_ON");model.Material.renderQueue=3000;}
   }
   Models[key]=model;
  }
  var go=new GameObject("AzerothCompanion_"+key);go.transform.position=position;
  var visual=go.AddComponent<MageCompanionVisual>();visual.Bind(model,height);Created++;
  Plugin.Logger.LogInfo($"Original Mage creature visual created: {key}; height={height}; source model and animation pack; no collider/AI created.");return go;
 }
 public static void SetPosition(GameObject go,Vector3 position){if(go!=null)go.transform.position=position;}
 public static void SetFacing(GameObject go,Vector3 direction){if(go==null)return;direction.y=0;if(direction.sqrMagnitude>.001f)go.transform.rotation=Quaternion.LookRotation(direction);}
 public static void SetClip(GameObject go,string clip){if(go!=null)go.GetComponent<MageCompanionVisual>()?.Play(clip);}
 public static void Destroy(GameObject go){if(go!=null){go.SetActive(false);Object.Destroy(go);}}
 public static void Shutdown()
 {
  if(tracker!=null){tracker.GetComponent<MageSheepTracker>()?.RestoreAll();Object.Destroy(tracker);tracker=null;}
  foreach(var v in Object.FindObjectsOfType<MageCompanionVisual>(true))if(v!=null)Object.Destroy(v.gameObject);
  foreach(var model in Models.Values)model.Release();Models.Clear();ActiveSheep=0;
 }
}

public sealed class MageCompanionVisual:MonoBehaviour
{
 public MageCompanionVisual(IntPtr p):base(p){}
 private Model model;private MeshFilter filter;private string clip="idle";private float start,nextFrame;private int last=-1;
 [HideFromIl2Cpp]
 internal void Bind(Model value,float height)
 {
  model=value;var child=new GameObject("AzerothOriginalCreatureMesh");child.transform.SetParent(transform,false);var bounds=value.Template.bounds;float scale=Math.Max(.05f,height)/Math.Max(.01f,bounds.size.y);child.transform.localScale=Vector3.one*scale;child.transform.localPosition=-new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)*scale;
  filter=child.AddComponent<MeshFilter>();filter.sharedMesh=model.Template;child.AddComponent<MeshRenderer>().sharedMaterial=model.Material;start=Time.time;
 }
 [HideFromIl2Cpp]
 internal void Play(string name){if(model==null || !model.Clips.ContainsKey(name))return;if(clip!=name || !model.Clips[name].Loop){clip=name;start=Time.time;last=-1;}}
 void LateUpdate()
 {
  if(model==null || filter==null || Time.time<nextFrame)return;nextFrame=Time.time+1f/24;if(!model.Clips.TryGetValue(clip,out var c))return;float elapsed=Time.time-start;if(!c.Loop && elapsed>=c.Duration && clip!="death"){Play("idle");return;}float t=c.Loop?elapsed%c.Duration:Math.Min(elapsed,c.Duration);int frame=Math.Clamp((int)(t/c.Duration*(c.Count-1)),0,c.Count-1);if(frame!=last){last=frame;filter.sharedMesh=c.Meshes[frame];}
 }
}

public sealed class MageSheepTracker:MonoBehaviour
{
 public MageSheepTracker(IntPtr p):base(p){}
 private sealed class Swap
 {
  internal Enemy Enemy;internal uint Id;internal GameObject Sheep;internal Vector3 Previous;
  internal readonly List<(Renderer renderer,bool enabled)> Renderers=new();
  internal readonly List<(GameObject go,bool active)> CreatureObjects=new();
 }
 private readonly Dictionary<IntPtr,Swap> swaps=new();
 private MethodInfo isSheep;private float nextScan;private bool failed;
 void LateUpdate()
 {
  if(failed)return;
  try
  {
   if(isSheep==null && Time.unscaledTime>=nextScan)
   {
    nextScan=Time.unscaledTime+.25f;var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothAbilities.MageSpellBehaviors")).FirstOrDefault(t=>t!=null);isSheep=type?.GetMethod("IsSheep",BindingFlags.Public|BindingFlags.Static);
   }
   foreach(var pair in swaps.ToArray())
   {
    var s=pair.Value;
    bool active=s.Enemy!=null && s.Enemy.id==s.Id && !s.Enemy.IsDeadOrDyingNextFrame() && s.Enemy.gameObject.activeInHierarchy && isSheep!=null && (bool)isSheep.Invoke(null,new object[]{s.Enemy});
    if(!active){Restore(s);swaps.Remove(pair.Key);continue;}
    foreach(var r in s.Renderers)if(r.renderer!=null)r.renderer.enabled=false;
    foreach(var o in s.CreatureObjects)if(o.go!=null)o.go.SetActive(false);
    var pos=Feet(s.Enemy);CreatureVisualBridge.SetPosition(s.Sheep,pos);var delta=pos-s.Previous;delta.y=0;CreatureVisualBridge.SetFacing(s.Sheep,delta);CreatureVisualBridge.SetClip(s.Sheep,delta.sqrMagnitude>.00001f?"walk":"idle");s.Previous=pos;
   }
   CreatureVisualBridge.ActiveSheep=swaps.Count;
   if(isSheep==null || Time.unscaledTime<nextScan)return;nextScan=Time.unscaledTime+.15f;
   var manager=EnemyManager.Instance;if(manager==null)return;
   foreach(var pair in manager.enemies)
   {
    var enemy=pair.Value;if(enemy==null || swaps.ContainsKey(enemy.Pointer) || enemy.IsDeadOrDyingNextFrame() || !(bool)isSheep.Invoke(null,new object[]{enemy}))continue;
    var s=new Swap{Enemy=enemy,Id=enemy.id,Previous=Feet(enemy)};float height=.75f;
    if(enemy.collider!=null)height=Math.Clamp(enemy.collider.height*Math.Abs(enemy.collider.transform.lossyScale.y)*.6f,.5f,1.1f);
    s.Sheep=CreatureVisualBridge.Create("sheep",s.Previous,height);Capture(enemy.transform,s);swaps[enemy.Pointer]=s;Plugin.Logger.LogInfo($"Polymorph source Sheep display856 attached to native enemy {enemy.id}; native collider/AI retained.");
   }
  }
  catch(Exception ex){failed=true;RestoreAll();Plugin.Logger.LogError("Polymorph visual tracker disabled; native visuals restored: "+ex);}
 }
 [HideFromIl2Cpp]
 private static Vector3 Feet(Enemy enemy)
 {
  if(enemy.collider==null)return enemy.transform.position;var c=enemy.collider;float height=(c.direction==1?c.height:c.radius*2)*Math.Abs(c.transform.lossyScale.y);return c.transform.TransformPoint(c.center)-Vector3.up*height*.5f;
 }
 [HideFromIl2Cpp]
 private static void Capture(Transform node,Swap s)
 {
  if(node==null || node==s.Sheep.transform)return;
  if(node.name.StartsWith("AzerothCreature_",StringComparison.Ordinal)){s.CreatureObjects.Add((node.gameObject,node.gameObject.activeSelf));node.gameObject.SetActive(false);}
  var r=node.GetComponent<Renderer>();if(r!=null){s.Renderers.Add((r,r.enabled));r.enabled=false;}
  for(int i=0;i<node.childCount;i++)Capture(node.GetChild(i),s);
 }
 [HideFromIl2Cpp]
 private static void Restore(Swap s)
 {
  CreatureVisualBridge.Destroy(s.Sheep);foreach(var r in s.Renderers)if(r.renderer!=null)r.renderer.enabled=r.enabled;foreach(var o in s.CreatureObjects)if(o.go!=null)o.go.SetActive(o.active);
 }
 [HideFromIl2Cpp]
 public void RestoreAll(){foreach(var s in swaps.Values)Restore(s);swaps.Clear();CreatureVisualBridge.ActiveSheep=0;}
 void OnDestroy(){RestoreAll();}
}
