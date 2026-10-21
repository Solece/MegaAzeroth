using System.Reflection;
using System.Text.Json;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Explicit temporary batch fixture: actual source body, native Unity materials
// and camera renders; no input injection or alteration of the user's game.
public sealed class CatMaterialProbe:MonoBehaviour
{
    public CatMaterialProbe(IntPtr p):base(p){}
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-cat-material-smoke");
    const string Visual="AzerothMegabonk.MageVisuals.MageVisualBridge",Hero="AzerothHeroes.HeroVisualBridge";
    readonly List<object> checks=new();readonly List<(BaseInputModule Module,bool Enabled,int Id)> modules=new();
    readonly string[] clips={"idle","run","jump","slide","crouch"};
    string folder;float began,entered;int phase,index,passed,failed;bool done;AsyncOperation unload;
    void Start(){if(!Armed){enabled=false;return;}began=entered=Time.realtimeSinceStartup;folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports","cat-material-"+Environment.ProcessId));Directory.CreateDirectory(folder);}
    void Update()
    {
        if(done||!Armed)return;
        try
        {
            if(Time.realtimeSinceStartup-began>85)throw new TimeoutException("Cat material native check exceeded85s");
            IsolateInput();var player=MyPlayer.Instance;
            if(player?.inventory?.playerHealth!=null){player.inventory.playerHealth.hp=player.inventory.playerHealth.maxHp;player.inventory.playerHealth.damageCooldown=10;}
            if(LevelupScreen.isLevelingUp){var w=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(x=>x.gameObject.activeInHierarchy);if(w!=null)w.CloseLevelupScreen();return;}
            switch(phase)
            {
                case 0:
                    if(!ElwynnBridge.Ready||player?.inventory==null||!player.hasStarted||MapGenerationController.isGenerating||GameManager.Instance?.IsPlaying()!=true)return;
                    Require(Environment.GetCommandLineArgs().Contains("-azeroth-test-battle"),"explicit save-guarded test battle");
                    Require((bool)Call("AzerothAbilities.TomeCompatibilityDiagnostics","SetAutomaticCastingForProbe",false),"disable automatic spells in visual fixture");
                    Require((bool)Call(Hero,"SetFormForProbe",768),"native source Cat768 visual selection");Call(Visual,"SetMotionForProbe","idle");entered=Time.realtimeSinceStartup;phase=1;break;
                case 1:
                    if(Get<int>(Hero,"ActiveFormSpellId")!=768||!Get<bool>(Hero,"IsAttached")){if(Age>10)throw new TimeoutException("Cat source body binding");return;}
                    if(Age<.35f)return;ValidateCat();CaptureWorld("cat-idle-fixed");CompareOpaqueBody();Call(Visual,"SetMotionForProbe",clips[0]);entered=Time.realtimeSinceStartup;phase=2;break;
                case 2:
                    if(Age<.22f)return;
                    var motion=J(Call(Visual,"MotionDiagnostics"));Require(P(motion,"currentClip").GetString()==clips[index],"actual source animation "+clips[index]);ValidatePartitions(Body());CaptureWorld("cat-"+clips[index]+"-fixed");Save("cat-"+clips[index]+"-motion.json",motion);
                    index++;if(index<clips.Length){Call(Visual,"SetMotionForProbe",clips[index]);entered=Time.realtimeSinceStartup;return;}
                    Require((bool)Call(Hero,"SetFormForProbe",9634),"switch to original Bear9634");entered=Time.realtimeSinceStartup;phase=3;break;
                case 3:
                    if(Get<int>(Hero,"ActiveFormSpellId")!=9634){if(Age>8)throw new TimeoutException("Bear control binding");return;}
                    var bear=J(Call(Visual,"SourceMaterialDiagnostics"));Require(!P(bear,"profileLoaded").GetBoolean()&&P(bear,"submeshes").GetInt32()==1,"unrelated Bear material path remains single-pass");Save("bear-control.json",bear);
                    Call(Hero,"SetFormForProbe",768);Call(Visual,"SetMotionForProbe","idle");entered=Time.realtimeSinceStartup;phase=4;break;
                case 4:
                    if(Get<int>(Hero,"ActiveFormSpellId")!=768){if(Age>8)throw new TimeoutException("Cat rebind");return;}
                    if(Age<.25f)return;ValidateCat();unload=Resources.UnloadUnusedAssets();entered=Time.realtimeSinceStartup;phase=5;break;
                case 5:
                    if(!unload.isDone||Age<.35f)return;
                    ValidateCat();Require(Get<bool>(Visual,"AllCachedAssetsValid"),"all source materials and animation assets survive native unload");Save("asset-health-after-unload.json",J(Call(Visual,"AssetDiagnostics")));CaptureWorld("cat-after-unload-fixed");Finish(null);break;
            }
        }
        catch(Exception error){Finish(error is TargetInvocationException tie?tie.InnerException??error:error);}
    }
    float Age=>Time.realtimeSinceStartup-entered;
    [HideFromIl2Cpp] static MeshRenderer Body()=>Object.FindObjectsOfType<MeshRenderer>().First(r=>r!=null&&r.enabled&&r.gameObject.activeInHierarchy&&r.name=="AzerothHero_druid");
    [HideFromIl2Cpp] void ValidateCat()
    {
        var value=J(Call(Visual,"SourceMaterialDiagnostics"));Save("cat-materials.json",value);Require(P(value,"profileLoaded").GetBoolean(),"original four source passes loaded");Require(P(value,"allFramesMaterialPartitions").GetBoolean(),"every baked animation retains four material partitions");ValidatePartitions(Body());
        var rows=P(value,"materials").EnumerateArray().ToArray();Require(rows.Length==4&&rows.Select(x=>P(x,"triangles").GetInt32()).SequenceEqual(new[]{670,60,8,4}),"exact source triangle partition670/60/8/4");
        Require(!P(rows[0],"alphaTest").GetBoolean()&&!P(rows[2],"alphaTest").GetBoolean(),"body and teeth ignore texture alpha");Require(P(rows[1],"alphaTest").GetBoolean()&&P(rows[1],"cull").GetInt32()==0,"fur retains source cutout and two-sided rendering");Require(P(rows[3],"alphaBlend").GetBoolean()&&P(rows[3],"zWrite").GetInt32()==0,"eyes retain source alpha blend without depth write");
    }
    [HideFromIl2Cpp] void ValidatePartitions(MeshRenderer body){Require(body.sharedMaterials.Length==4&&body.GetComponent<MeshFilter>().sharedMesh.subMeshCount==4,"actual player renderer has four source materials");}
    [HideFromIl2Cpp] void CompareOpaqueBody()
    {
        var body=Body();var source=body.GetComponent<MeshFilter>().sharedMesh;var clone=Object.Instantiate(source);
        var go=new GameObject("AzerothCatOpaqueBodyProbe");go.layer=31;go.transform.position=body.transform.position;go.transform.rotation=body.transform.rotation;go.transform.localScale=body.transform.lossyScale;var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=clone;var renderer=go.AddComponent<MeshRenderer>();
        var old=new Material(Shader.Find("Unlit/Transparent Cutout")??Shader.Find("Standard")??Shader.Find("Sprites/Default")){name="CatLegacyGlobalCutoutControl",mainTexture=body.sharedMaterials[0].mainTexture,color=Color.white};old.SetFloat("_Mode",1);old.SetFloat("_Cutoff",.5f);old.EnableKeyword("_ALPHATEST_ON");old.renderQueue=2450;
        try
        {
            renderer.sharedMaterials=Enumerable.Repeat(old,4).ToArray();var before=Capture("cat-body-old-cutout.ppm",body,1<<31,true);
            renderer.sharedMaterials=body.sharedMaterials;var after=Capture("cat-body-fixed-opaque.ppm",body,1<<31,true);
            int added=0,removed=0;for(int i=0;i<before.Length;i+=3){bool a=Background(before,i),b=Background(after,i);if(a&&!b)added++;if(!a&&b)removed++;}
            Save("opaque-body-pixel-proof.json",new{addedBodyPixels=added,removedBodyPixels=removed,scope="Actual same source pose/complete geometry rendered by Unity camera; exact legacy shader/globalcutout versus four original source material modes. Magenta clear, no postprocessing, no CPU mesh rasterization."});
            Require(added>40,"actual opaque body fills former alpha holes",added+" recovered body pixels");Save("legacy-control.json",new{shader=old.shader.name,keywords=old.shaderKeywords.ToArray(),cutoff=old.GetFloat("_Cutoff"),fixedMaterials=body.sharedMaterials.Select(m=>m.shader.name).ToArray(),removedControlPixels=removed});
        }
        finally{Object.Destroy(go);Object.Destroy(clone);Object.Destroy(old);}
    }
    [HideFromIl2Cpp] static bool Background(byte[] pixels,int i)=>pixels[i]>250&&pixels[i+1]<5&&pixels[i+2]>250;
    [HideFromIl2Cpp] void CaptureWorld(string label)=>Capture(label+".ppm",Body(),Camera.main.cullingMask,false);
    [HideFromIl2Cpp] byte[] Capture(string name,Renderer body,int mask,bool silhouette)
    {
        var go=new GameObject("AzerothCatMaterialInspectionCamera");var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.cullingMask=mask;camera.fieldOfView=40;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=silhouette?new Color(1,0,1,1):Camera.main.backgroundColor;
        var at=body.bounds.center;float distance=Mathf.Max(2f,Mathf.Max(body.bounds.size.z,body.bounds.size.y)*2.1f);var right=body.transform.right;camera.transform.position=at+right*distance+Vector3.up*(distance*.35f)-body.transform.forward*(distance*.2f);camera.transform.rotation=Quaternion.LookRotation(at-camera.transform.position,Vector3.up);
        var rt=new RenderTexture(960,540,24);rt.Create();var active=RenderTexture.active;Texture2D image=null;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();var pix=image.GetPixels32();var bytes=new byte[960*540*3];int n=0;for(int y=539;y>=0;y--)for(int x=0;x<960;x++){var c=pix[y*960+x];bytes[n++]=c.r;bytes[n++]=c.g;bytes[n++]=c.b;}using var stream=File.Create(Path.Combine(folder,name));stream.Write(System.Text.Encoding.ASCII.GetBytes("P6\n960 540\n255\n"));stream.Write(bytes);return bytes;}
        finally{camera.targetTexture=null;RenderTexture.active=active;rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);Object.Destroy(go);}
    }
    [HideFromIl2Cpp] void IsolateInput(){foreach(var m in Resources.FindObjectsOfTypeAll<BaseInputModule>()){try{if(m==null||!m.gameObject.scene.IsValid())continue;int id=m.GetInstanceID();if(!modules.Any(s=>s.Id==id))modules.Add((m,m.enabled,id));m.enabled=false;}catch{}}}
    [HideFromIl2Cpp] static Type Type(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).First(t=>t!=null);
    [HideFromIl2Cpp] static object Call(string type,string method,params object[] args)=>Type(type).GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);
    [HideFromIl2Cpp] static T Get<T>(string type,string property)=>(T)Type(type).GetProperty(property,BindingFlags.Public|BindingFlags.Static).GetValue(null);
    [HideFromIl2Cpp] static JsonElement J(object value){using var d=JsonDocument.Parse(value as string??JsonSerializer.Serialize(value));return d.RootElement.Clone();}
    [HideFromIl2Cpp] static JsonElement P(JsonElement value,string name)=>value.EnumerateObject().First(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).Value;
    [HideFromIl2Cpp] void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),JsonSerializer.Serialize(value));
    [HideFromIl2Cpp] void Require(bool condition,string label,string detail=""){checks.Add(new{pass=condition,label,detail});if(condition)passed++;else{failed++;throw new InvalidOperationException(label+" "+detail);}}
    [HideFromIl2Cpp] void Finish(Exception error)
    {
        if(done)return;done=true;if(error!=null){failed++;checks.Add(new{pass=false,label="native cat material fixture",detail=error.ToString()});}foreach(var m in modules){try{if(m.Module!=null)m.Module.enabled=m.Enabled;}catch{}}
        try{Call(Visual,"SetMotionForProbe",(object)null);Call(Hero,"SetFormForProbe",-1);}catch{}
        Save("summary.json",new{pass=failed==0,passed,failed,phase,index,seconds=Time.realtimeSinceStartup-began,checks,error=error?.ToString(),scope="Original Cat892 four native material modes and unchanged geometry, actual camera opaque/cutout pixel comparison, five movement clips, Bear control and native resource unload. No gameplay stats/physics/input changes."});Application.Quit(failed==0?0:2);
    }
}
[HarmonyPatch]
static class CatProbeButtonIsolation
{static bool Prepare()=>CatMaterialProbe.Armed;static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));static bool Prefix(ref bool __result){__result=false;return false;}}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class CatProbeAxisIsolation
{static bool Prepare()=>CatMaterialProbe.Armed;static bool Prefix(ref float __result){__result=0;return false;}}
