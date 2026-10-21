using System.Reflection;
using System.Text.Json;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Real native endless enemy variants; no invented palette or native material write.
public sealed class GhostProbe:MonoBehaviour
{
    public GhostProbe(IntPtr pointer):base(pointer){}
    const string Bridge="AzerothMegabonk.EnemyVisuals.EnemyVisualBridge";
    readonly List<Enemy> ghosts=new();readonly List<Vector3> positions=new();readonly List<int> nativeTypes=new(){16,17,40,39};
    readonly List<object> checks=new(),rows=new();readonly Dictionary<string,Type> types=new();readonly List<(BaseInputModule Module,bool Enabled,int Id)> modules=new();
    int inspectIndex=-1;Enemy king;Vector3 center,rowForward;string folder;float began,entered;int phase,passed,failed,skipped;bool done;
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-ghost-smoke");
    void Start()
    {
        if(!Armed){enabled=false;return;}began=entered=Time.realtimeSinceStartup;folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports","ghost-"+Environment.ProcessId));Directory.CreateDirectory(folder);
        try{Require(Environment.GetCommandLineArgs().Contains("-azeroth-test-battle"),"explicit native battle/save guard");if(((EEnemy)48).ToString()=="GhostInvincible")nativeTypes.Add(48);else Record("SKIP","additional48 variant","Actual enum does not confirm GhostInvincible.");}catch(Exception error){Finish(error);}
    }
    void Update()
    {
        if(done||!Armed)return;
        try
        {
            if(Time.realtimeSinceStartup-began>70)throw new TimeoutException("Ghost probe exceeded70s");IsolateInput();var player=MyPlayer.Instance;
            if(player?.inventory?.playerHealth!=null){player.inventory.playerHealth.hp=player.inventory.playerHealth.maxHp;player.inventory.playerHealth.damageCooldown=10;}
            if(LevelupScreen.isLevelingUp){var window=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(w=>w.gameObject.activeInHierarchy);if(window!=null){window.CloseLevelupScreen();Record("SKIP","incidental XP window","Closed actual window without selecting upgrade.");}return;}
            for(int i=0;i<ghosts.Count;i++)if(ghosts[i]!=null&&!ghosts[i].IsDeadOrDyingNextFrame()){var held=positions[i];held.y=ghosts[i].transform.position.y;ghosts[i].transform.position=held;}
            switch(phase)
            {
                case 0:
                    if(!ElwynnBridge.Ready||player?.inventory==null||!player.hasStarted||MapGenerationController.isGenerating||GameManager.Instance?.IsPlaying()!=true||GameManager.Instance.isGameOver)return;
                    var density=J(DecorDensityDiagnostics.SnapshotJson);Save("default-density.json",density);Require(P(density,"Multiplier").GetSingle()==1f,"no density flag retains native amount1");Require((bool)Call("AzerothAbilities.TomeCompatibilityDiagnostics","SetAutomaticCastingForProbe",false),"disable automatic spell casts");Require((bool)Call("AzerothAbilities.ClassAbilityDiagnostics","ClearEffectsForProbe"),"clear previous source pets/effects");
                    var forward=Camera.main.transform.forward;forward.y=0;forward=forward.sqrMagnitude>.001f?forward.normalized:Vector3.forward;rowForward=forward;var right=Vector3.Cross(Vector3.up,forward).normalized;center=player.transform.position+forward*10;
                    for(int i=0;i<nativeTypes.Count;i++)
                    {
                        int id=nativeTypes[i];var data=Resources.FindObjectsOfTypeAll<EnemyData>().FirstOrDefault(d=>d!=null&&(int)d.enemyName==id);Require(data!=null,"native enemy data "+id,((EEnemy)id).ToString());
                        var at=Floor(center+right*((i-(nativeTypes.Count-1)*.5f)*2.8f));var enemy=EnemyManager.Instance.SpawnEnemy(data,at,0,true,EEnemyFlag.None,false,1);Require(enemy!=null,"actual ordinary native spawn "+id);enemy.maxHp=10000000;enemy.hp=enemy.maxHp;ghosts.Add(enemy);positions.Add(at);
                    }
                    king=EnemyManager.Instance.SpawnBoss((EEnemy)43,-1,EEnemyFlag.Boss,Floor(player.transform.position+forward*55),1);Require(king!=null,"native GhostKing43 negative fixture");king.maxHp=10000000;king.hp=king.maxHp;
                    Save("spawn-fixture.json",new{ids=nativeTypes,positions=positions.Select(V).ToArray(),nativeBoss43=king.id,policy="Native ordinary SpawnEnemy with no elite/boss flag, real ghost assets. Held fixture X/Z and10MHP only; native vertical root/flight movement retained; AI/colliders/native palettes unchanged. GhostKing is a separate actual boss."});entered=Time.realtimeSinceStartup;phase=1;break;
                case 1:
                    if(ghosts.Any(e=>e==null||e.IsTeleporting())||king.IsTeleporting()){if(Age>12)throw new TimeoutException("Ghost spawn admission");return;}
                    var pending=ghosts.Any(e=>!P(J(Call(Bridge,"DescribeGhostPalette",e)),"Attached").GetBoolean());if(pending){if(Age>12)throw new TimeoutException("Source ghost body never attached");return;}
                    if(Age<1.2f)return;ValidateAll();for(int k=0;k<ghosts.Count;k++){inspectIndex=k;Capture("ghost-tier-"+nativeTypes[k]+"-close.ppm",true);}inspectIndex=-1;Capture("ghost-tiers-native-camera.ppm",false);Capture("ghost-tiers-inspection.ppm",true);Save("ghost-palette-diagnostics.json",Call(Bridge,"GhostPaletteDiagnostics"));Save("ghost-texture-cache.json",Call(Bridge,"GhostNativeTextureDiagnostics"));entered=Time.realtimeSinceStartup;phase=2;break;
                case 2:
                    if(Age<.6f)return;
                    for(int i=0;i<ghosts.Count;i++){var snapshot=J(Call(Bridge,"DescribeGhostPalette",ghosts[i]));CompareNative(ghosts[i],snapshot);Save("tier-"+nativeTypes[i]+"-later.json",snapshot);}Capture("ghost-tiers-inspection-later.ppm",true);var cache=J(Call(Bridge,"GhostNativeTextureDiagnostics"));Save("ghost-texture-cache-later.json",cache);Require(P(cache,"ReadFailures").GetInt32()==0,"native palette cache has no failed readbacks");Require(P(cache,"Textures").EnumerateArray().All(t=>P(t,"Valid").GetBoolean()&&P(t,"ReadbackAlive").GetBoolean()&&P(t,"ReadbackRetained").GetBoolean()),"all native palette readbacks remain retained");Require(P(cache,"ReadAttempts").GetInt32()==P(cache,"Textures").GetArrayLength(),"one bounded readback per actual native texture");Finish(null);break;
            }
        }
        catch(Exception error){Finish(error is TargetInvocationException invocation?invocation.InnerException??error:error);}
    }
    float Age=>Time.realtimeSinceStartup-entered;
    [HideFromIl2Cpp] void ValidateAll()
    {
        var nativeColors=new List<float[]>();var observedColors=new List<float[]>();var expectedColors=new List<float[]>();
        for(int i=0;i<ghosts.Count;i++)
        {
            var e=ghosts[i];Save("native-material-"+nativeTypes[i]+".json",InspectNative(e));Save("source-pose-"+nativeTypes[i]+".json",new{general=J(Call(Bridge,"Describe",e)),renderers=e.GetComponentsInChildren<Renderer>(true).Select(r=>new{name=r.name,enabled=r.enabled,active=r.gameObject.activeInHierarchy,center=V(r.bounds.center),size=V(r.bounds.size),position=V(r.transform.position),scale=V(r.transform.lossyScale)}).ToArray()});var snapshot=J(Call(Bridge,"DescribeGhostPalette",e));Save("tier-"+nativeTypes[i]+".json",snapshot);Require(P(snapshot,"NativeEnemyId").GetInt32()==nativeTypes[i]&&!e.IsBoss(),"actual ordinary ghost ID "+nativeTypes[i]);
            Require(S(snapshot,"SourceModel")=="ghost"&&P(snapshot,"SourceDisplayId").GetInt32()==146,"original source Ghost model "+nativeTypes[i]);Require(P(snapshot,"PaletteAvailable").GetBoolean(),"actual native palette available "+nativeTypes[i]);
            Require(P(snapshot,"NativeBodyHidden").GetBoolean()&&P(snapshot,"Visible").GetBoolean(),"source body visible and native body suppressed "+nativeTypes[i]);Require(P(snapshot,"PassCount").GetInt32()==3&&P(snapshot,"SourcePassCount").GetInt32()==26,"source26passes batched into3 "+nativeTypes[i]);
            var blends=P(snapshot,"MaterialBlends").EnumerateArray().Select(x=>x.GetInt32()).ToArray();Require(blends.Length==3&&blends.All(b=>b is 0 or 4)&&blends.Contains(4),"source ghost blend metadata preserved "+nativeTypes[i]);Require(P(snapshot,"SourceAtlasLuminanceAlpha").GetBoolean(),"source atlas luminance adapter active "+nativeTypes[i]);var adapted=P(snapshot,"RuntimePaletteBlends").EnumerateArray().Select(v=>v.GetString()).ToArray();Require(adapted.Count(v=>v=="SrcAlpha/OneMinusSrcAlpha")==2,"black-visible alpha-over palette adapter "+nativeTypes[i]);
            var sourceRenderer=e.GetComponentsInChildren<Renderer>(true).First(r=>r!=null&&r.enabled&&r.name.StartsWith("AzerothCreature_",StringComparison.Ordinal));var fixtureFloor=Floor(e.transform.position).y-.1f;Require(sourceRenderer.bounds.min.y>=fixtureFloor-.3f,"source ghost remains above actual native floor "+nativeTypes[i],"minY="+sourceRenderer.bounds.min.y+" floor="+fixtureFloor);var independent=CompareNative(e,snapshot);nativeColors.Add(independent);observedColors.Add(Rgba(P(snapshot,"ObservedSourceColor")));expectedColors.Add(Rgba(P(snapshot,"ExpectedSourceColor")));rows.Add(new{nativeId=nativeTypes[i],enemyId=e.id,snapshot,independentNativeAlbedo=independent});
        }
        int nativeDistinct=Distinct(nativeColors.Take(4)),expectedDistinct=Distinct(expectedColors.Take(4)),observedDistinct=Distinct(observedColors.Take(4));
        Require(nativeDistinct>1,"actual native texture palettes differ",nativeDistinct+" distinct among4");Require(observedDistinct==expectedDistinct,"source colors preserve actual native tier distinctions",observedDistinct+" observed, "+expectedDistinct+" expected");
        var negative=J(Call(Bridge,"DescribeGhostPalette",king));Save("boss43-negative.json",new{kingNativeId=(int)king.enemyData.enemyName,isBoss=king.IsBoss(),ghostPalette=negative,general=J(Call(Bridge,"Describe",king))});Require((int)king.enemyData.enemyName==43&&king.IsBoss()&&!P(negative,"Attached").GetBoolean(),"native GhostKing43 is not ordinary source ghost");
        Record("SKIP","source embedded particles","Three original source particles are not yet rendered by the candidate; body/material/palette proof only.");
    }
    [HideFromIl2Cpp] float[] CompareNative(Enemy e,JsonElement snapshot)
    {
        var renderer=e.enemyRenderer.enemyRenderer;Require(renderer!=null,"native reference renderer retained "+e.id);string key=e.enemyRenderer.albedoColorKey;Require(!string.IsNullOrEmpty(key)&&key==S(snapshot,"NativeAlbedoKey"),"native shader albedo key independently matches "+e.id);
        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,0);var wide=new MaterialPropertyBlock();renderer.GetPropertyBlock(wide);Color native;string origin;
        if(block.HasColor(key)){native=block.GetColor(key);origin="native-property-block";}
        else if(wide.HasColor(key)){native=wide.GetColor(key);origin="native-wide-property-block";}
        else if(renderer.sharedMaterial!=null&&renderer.sharedMaterial.HasProperty(key)){native=renderer.sharedMaterial.GetColor(key);origin="native-renderer-material";}
        else if(e.enemyData.material!=null&&e.enemyData.material.HasProperty(key)){native=e.enemyData.material.GetColor(key);origin="native-enemy-data-material";}
        else throw new InvalidOperationException("Actual native palette key unavailable: "+key);
        var rgba=new[]{native.r,native.g,native.b,native.a};var texture=block.GetTexture("_AlbedoMap")??wide.GetTexture("_AlbedoMap")??renderer.sharedMaterial.GetTexture("_AlbedoMap");Require(texture!=null,"native texture palette retained "+e.id);var mean=SamplePalette(texture);var tint=new[]{mean[0]*native.r,mean[1]*native.g,mean[2]*native.b,1f};Require(Near(tint,Rgba(P(snapshot,"NativePaletteTint"))),"independent native texture RGB and status equals source tint "+e.id);var source=Rgba(P(snapshot,"SourceBaseColor"));var expected=new[]{source[0]*tint[0],source[1]*tint[1],source[2]*tint[2],source[3]};
        Require(Near(rgba,Rgba(P(snapshot,"NativeAlbedo"))),"independent native palette equals diagnostic "+e.id,origin+":"+key);Require(Near(expected,Rgba(P(snapshot,"ExpectedSourceColor")))&&Near(expected,Rgba(P(snapshot,"ObservedSourceColor"))),"actual source MPB equals source luminance multiplied by sampled native palette "+e.id);return tint;
    }
    // Independent fixture readback from the unmodified, authoritative native texture.
    [HideFromIl2Cpp] float[] SamplePalette(Texture texture)
    {
        int width=Math.Min(texture.width,256),height=Math.Min(texture.height,256);var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
        var rt=new RenderTexture(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);rt.Create();Texture2D image=null;
        try
        {
            GL.sRGBWrite=false;Graphics.Blit(texture,rt);RenderTexture.active=rt;image=new Texture2D(width,height,TextureFormat.RGBA32,false,true);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            double r=0,g=0,b=0,weight=0;foreach(var c in image.GetPixels32()){double a=c.a/255d;r+=c.r/255d*a;g+=c.g/255d*a;b+=c.b/255d*a;weight+=a;}
            if(weight<=0)throw new InvalidOperationException("Native palette texture has no opaque sample");return new[]{(float)(r/weight),(float)(g/weight),(float)(b/weight),1f};
        }
        finally{GL.sRGBWrite=srgb;RenderTexture.active=active;rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);}
    }
    [HideFromIl2Cpp] object InspectNative(Enemy enemy)
    {
        object MaterialInfo(Material material,Renderer renderer)
        {
            if(material==null)return new{missing=true};
            var block=new MaterialPropertyBlock();if(renderer!=null)renderer.GetPropertyBlock(block);
            var props=new List<object>();var shader=material.shader;
            for(int i=0;i<shader.GetPropertyCount();i++)
            {
                var key=shader.GetPropertyName(i);var kind=shader.GetPropertyType(i).ToString();object value=null,mpb=null;
                if(kind=="Color"){var c=material.GetColor(key);value=new[]{c.r,c.g,c.b,c.a};if(block.HasColor(key)){c=block.GetColor(key);mpb=new[]{c.r,c.g,c.b,c.a};}}
                else if(kind=="Vector"){var v=material.GetVector(key);value=new[]{v.x,v.y,v.z,v.w};}
                else if(kind is "Float" or "Range")value=material.GetFloat(key);
                else if(kind=="Texture"){var t=material.GetTexture(key);value=t!=null?new{name=t.name,width=t.width,height=t.height,id=t.GetInstanceID()}:null;}
                props.Add(new{key,kind,value,mpb});
            }
            return new{name=material.name,id=material.GetInstanceID(),shader=shader.name,rendererId=renderer!=null?renderer.GetInstanceID():0,properties=props};
        }
        return new{nativeId=(int)enemy.enemyData.enemyName,renderer=MaterialInfo(enemy.renderer?.sharedMaterial,enemy.renderer),controller=MaterialInfo(enemy.enemyRenderer?.enemyRenderer?.sharedMaterial,enemy.enemyRenderer?.enemyRenderer),data=MaterialInfo(enemy.enemyData.material,null)};
    }
    [HideFromIl2Cpp] void Capture(string name,bool inspection)
    {
        var camera=Camera.main;var prior=camera.targetTexture;var active=RenderTexture.active;var pos=camera.transform.position;var rot=camera.transform.rotation;var canvases=Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.enabled&&c.renderMode!=RenderMode.WorldSpace).ToArray();var rt=new RenderTexture(960,540,24);rt.Create();Texture2D image=null;
        try
        {
            foreach(var canvas in canvases)canvas.enabled=false;if(inspection){
            var bodies=ghosts.Select(e=>e.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r=>r!=null&&r.enabled&&r.name.StartsWith("AzerothCreature_",StringComparison.Ordinal))).ToArray();
            Vector3 at;float distance;
            if(inspectIndex>=0){var bounds=bodies[inspectIndex].bounds;at=bounds.center;distance=Mathf.Max(2f,Mathf.Max(bounds.size.x,bounds.size.y)*1.8f);}
            else{at=bodies.Aggregate(Vector3.zero,(a,r)=>a+r.bounds.center)/bodies.Length;distance=12f;}
            camera.transform.position=at+Vector3.up*(distance*.35f)-rowForward*distance;camera.transform.rotation=Quaternion.LookRotation(at-camera.transform.position,Vector3.up);
        }camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();var pixels=image.GetPixels32();var bytes=new byte[960*540*3];int n=0;for(int y=539;y>=0;y--)for(int x=0;x<960;x++){var pixel=pixels[y*960+x];bytes[n++]=pixel.r;bytes[n++]=pixel.g;bytes[n++]=pixel.b;}using var stream=File.Create(Path.Combine(folder,name));stream.Write(System.Text.Encoding.ASCII.GetBytes("P6\n960 540\n255\n"));stream.Write(bytes);
        }
        finally{foreach(var canvas in canvases)if(canvas!=null)canvas.enabled=true;camera.targetTexture=prior;camera.transform.position=pos;camera.transform.rotation=rot;RenderTexture.active=active;rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);}
    }
    [HideFromIl2Cpp] static Vector3 Floor(Vector3 at){if(Physics.Raycast(at+Vector3.up*50,Vector3.down,out var hit,150,GameManager.Instance.whatIsGround.value,QueryTriggerInteraction.Ignore))return hit.point+Vector3.up*.1f;throw new InvalidOperationException("No native floor under ghost fixture "+at);}
    [HideFromIl2Cpp] void IsolateInput(){foreach(var m in Resources.FindObjectsOfTypeAll<BaseInputModule>()){try{if(m==null||!m.gameObject.scene.IsValid())continue;int id=m.GetInstanceID();if(!modules.Any(s=>s.Id==id))modules.Add((m,m.enabled,id));m.enabled=false;}catch{ }}}
    [HideFromIl2Cpp] static int Distinct(IEnumerable<float[]> rows){var unique=new List<float[]>();foreach(var row in rows)if(!unique.Any(c=>Near(c,row)))unique.Add(row);return unique.Count;}
    [HideFromIl2Cpp] static bool Near(float[] a,float[] b)=>a.Length==b.Length&&a.Select((x,i)=>Mathf.Abs(x-b[i])<.0002f).All(v=>v);
    [HideFromIl2Cpp] static float[] Rgba(JsonElement value)=>value.EnumerateArray().Select(x=>x.GetSingle()).ToArray();
    [HideFromIl2Cpp] static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
    [HideFromIl2Cpp] object Call(string type,string method,params object[] args){if(!types.TryGetValue(type,out var t))types[type]=t=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(type,false)).FirstOrDefault(a=>a!=null)??throw new MissingMemberException(type);return t.GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);}
    [HideFromIl2Cpp] static JsonElement J(object value){using var d=JsonDocument.Parse(value as string??JsonSerializer.Serialize(value));return d.RootElement.Clone();}
    [HideFromIl2Cpp] static JsonElement P(JsonElement value,string name){foreach(var p in value.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase))return p.Value;throw new MissingMemberException(name);}
    [HideFromIl2Cpp] static string S(JsonElement value,string name)=>P(value,name).GetString();
    [HideFromIl2Cpp] void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),value as string??JsonSerializer.Serialize(value));
    [HideFromIl2Cpp] void Record(string status,string label,string detail=""){if(status=="PASS")passed++;else if(status=="FAIL")failed++;else skipped++;checks.Add(new{status,label,detail});Plugin.Note("GHOST "+status+" "+label+" "+detail);}
    [HideFromIl2Cpp] void Require(bool value,string label,string detail=""){Record(value?"PASS":"FAIL",label,detail);if(!value)throw new InvalidOperationException(label+" "+detail);}
    [HideFromIl2Cpp] void Finish(Exception error)
    {
        if(done)return;done=true;if(error!=null)Record("FAIL","ghost fixture exception",error.ToString());foreach(var m in modules){try{if(m.Module!=null&&m.Module.gameObject!=null)m.Module.enabled=m.Enabled;}catch{ }}
        Save("summary.json",new{pass=failed==0,passed,failed,skipped,phase,wallSeconds=Time.realtimeSinceStartup-began,checks,ghosts=rows,error=error?.ToString(),coverage="Actual ordinary native16/17/40/39(+confirmed48), independent native texture readback and shader/MPB status versus observed source body color,3batched sourcepasses and43boss exclusion. No invented RGB, combat behavior or embedded particle proof."});Application.Quit(failed==0?0:2);
    }
}
[HarmonyPatch]
static class GhostButtonIsolation
{static bool Prepare()=>GhostProbe.Armed;static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));static bool Prefix(ref bool __result){__result=false;return false;}}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class GhostAxisIsolation
{static bool Prepare()=>GhostProbe.Armed;static bool Prefix(ref float __result){__result=0;return false;}}
