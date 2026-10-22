using System.Reflection;
using System.Text.Json;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using Assets.Scripts._Data.MapsAndStages;
using Assets.Scripts.Utility;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using SourceLocale=UnityEngine.Localization.Locale;
using Object=UnityEngine.Object;
namespace NamesGhostProbe;

[BepInPlugin("local.azerothmegabonk.namesghosts.probe","Names and ghosts QA only","0.1.0")]
[BepInDependency("local.azerothmegabonk.enemyvisuals")]
public sealed class Plugin:BasePlugin
{
    public override void Load(){if(!Probe.Armed)return;ClassInjector.RegisterTypeInIl2Cpp<Probe>();AddComponent<Probe>();new Harmony("local.azerothmegabonk.namesghosts.probe").PatchAll();}
}
public sealed class Probe:MonoBehaviour
{
    public Probe(IntPtr p):base(p){}
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-names-ghosts-smoke");
    const string Bridge="AzerothMegabonk.EnemyVisuals.EnemyVisualBridge";
    readonly (int Id,string Key)[] bosses={(8,"ogre"),(23,"infernal"),(27,"tarantula"),(34,"lich"),(50,"bonegolem"),(7,"doomguard"),(35,"dragonspawncaster")};
    readonly int[] ghostIds={16,17,39,40,48};
    readonly Dictionary<uint,string> nativeNames=new();readonly List<object> checks=new();readonly List<Enemy> actors=new();readonly List<(BaseInputModule Module,bool Enabled)> input=new();readonly Dictionary<string,(string Ru,string En)> names=new();
    int phase,index,passed,failed;uint killedId;float began,entered;string folder;bool done;Enemy current,ordinary,deathGhost;TargetOfInterestPrefab label;SourceLocale initialLocale;Vector3 capturedGhostScale;JsonElement lastGhost;
    void Start(){if(!Armed){enabled=false;return;}began=entered=Time.realtimeSinceStartup;folder=Path.GetFullPath(Path.Combine(Paths.GameRootPath,"../reports","names-ghosts-"+Environment.ProcessId));Directory.CreateDirectory(folder);if(!Environment.GetCommandLineArgs().Contains("-azeroth-test-menu")){Finish(new InvalidOperationException("Test-menu save guard required"));return;}var d=J(File.ReadAllText(Path.Combine(Paths.GameRootPath,"AzerothAssets/boss-names.json")));foreach(var e in d.GetProperty("entries").EnumerateArray())names[e.GetProperty("modelKey").GetString()]=(e.GetProperty("ru").GetString(),e.GetProperty("en").GetString());}
    void Update()
    {
        if(done||!Armed)return;
        try
        {
            if(Time.realtimeSinceStartup-began>150)throw new TimeoutException("Names/ghosts native fixture exceeded150s phase="+phase+" index="+index);
            foreach(var m in Resources.FindObjectsOfTypeAll<BaseInputModule>())try{if(m==null||!m.gameObject.scene.IsValid())continue;if(!input.Any(x=>x.Module!=null&&x.Module.Pointer==m.Pointer))input.Add((m,m.enabled));m.enabled=false;}catch{}
            var player=MyPlayer.Instance;if(player?.inventory?.playerHealth!=null){player.inventory.playerHealth.hp=player.inventory.playerHealth.maxHp;player.inventory.playerHealth.damageCooldown=10;}
            if(LevelupScreen.isLevelingUp){Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(x=>x.gameObject.activeInHierarchy)?.CloseLevelupScreen();return;}
            var main=Object.FindObjectOfType<MainMenu>();
            switch(phase)
            {
                case 0:if(main==null||Age<3)return;initialLocale=LocalizationSettings.SelectedLocale;SetLocale("ru");Call("AzerothHeroes.HeroProbeApi","BatchProbe","focus");Next(1);break;
                case 1:if(main==null||!main.tabCharacters.activeSelf||Age<.5f)return;Call("AzerothHeroes.HeroProbeApi","BatchProbe","click:mage");Next(2);break;
                case 2:if(Age<1)return;Call("AzerothHeroes.HeroProbeApi","BatchProbe","confirm:mage");main.GoToMapSelection();var map=main.mapSelectionUi;if(map.runConfig==null)map.Init();var data=DataManager.Instance.GetMap(EMap.Desert);map.runConfig.mapData=data;map.runConfig.stageData=data.stages[0];map.runConfig.mapTierIndex=0;map.runConfig.challenge=null;map.StartMap();Next(3);break;
                case 3:
                    if(GameManager.Instance==null||player?.inventory==null||MapGenerationController.isGenerating||LoadingScreen.isLoading||Age<1)return;
                    player.hasStarted=true;if(!GameManager.Instance.IsPlaying())GameManager.Instance.StartPlaying();Call("AzerothAbilities.TomeCompatibilityDiagnostics","SetAutomaticCastingForProbe",false);Call("AzerothAbilities.ClassAbilityDiagnostics","ClearEffectsForProbe");Save("native-enums.json",Enum.GetValues(typeof(EEnemy)).Cast<EEnemy>().Select(x=>new{Id=(int)x,Name=x.ToString()}));index=0;SpawnBoss();break;
                case 4:
                    if(current.IsTeleporting()||Age<1.3f)return;
                    label=FindLabel(current);if(label==null){if(Age>12)throw new TimeoutException("Actual native boss HUD missing "+bosses[index]);return;}
                    var actual=J(Call(Bridge,"VarietyDiagnostics",current));Check(actual.GetProperty("ActualKey").GetString()==bosses[index].Key,"actual boss model "+bosses[index].Key);
                    Check(label.t_name.text==names[bosses[index].Key].Ru,"actual native Russian TMP boss caption "+bosses[index].Key,label.t_name.text);Check(current.enemyData.GetName()==nativeNames[current.id],"caption uses instance without changing native EnemyData "+bosses[index].Key);
                    Save("boss-"+bosses[index].Key+"-ru.json",J(Get(Bridge,"BossNameDiagnosticsJson")));SetLocale("en");Next(5);break;
                case 5:
                    if(Age<.5f)return;Check(label.t_name.text==names[bosses[index].Key].En,"actual native English TMP boss caption "+bosses[index].Key,label.t_name.text);Save("boss-"+bosses[index].Key+"-en.json",J(Get(Bridge,"BossNameDiagnosticsJson")));
                    Check((bool)Call(Bridge,"RestoreVisualForProbe",current),"actual restore source visual "+bosses[index].Key);Check(label.t_name.text==current.enemyData.GetName(),"native caption restored with native model "+bosses[index].Key,label.t_name.text);Check((bool)Call(Bridge,"RebindVisualForProbe",current),"actual source model rebind "+bosses[index].Key);Next(6);break;
                case 6:
                    if(Age<.2f)return;Check(label.t_name.text==names[bosses[index].Key].En,"source caption returns after rebind "+bosses[index].Key);SetLocale("ru");if(index<bosses.Length-1)current.ReleaseToPool();index++;if(index<bosses.Length)SpawnBoss();else{ordinary=Spawn(27,false);label.SetEnemy(ordinary);Next(7);}break;
                case 7:
                    if(Age<1.2f)return;Check(!ordinary.IsBoss()&&label.t_name.text==ordinary.enemyData.GetName(),"same species ordinary actor keeps native caption");current.gameObject.SetActive(true);label.SetEnemy(current);Next(8);break;
                case 8:
                    if(Age<.2f)return;Check(label.t_name.text==names["dragonspawncaster"].Ru,"pooled native HUD follows actual new boss");Save("hud-pooled-rebind.json",J(Get(Bridge,"BossNameDiagnosticsJson")));var regression=J(Call(Bridge,"RunGhostPresentationRegressionForProbe"));Check(regression.GetProperty("pass").GetBoolean(),"managed original ghost pass/scope regression",regression.GetRawText());Save("ghost-native-regression.json",regression);index=0;SpawnGhost();break;
                case 9:
                    if(current.IsTeleporting()||Age<1.3f)return;var g=J(Call(Bridge,"DescribeGhostPalette",current));lastGhost=g;Check(g.GetProperty("Attached").GetBoolean()&&g.GetProperty("Visible").GetBoolean(),"live ghost source body attached/visible "+ghostIds[index]);Check(g.GetProperty("PaletteAvailable").GetBoolean(),"actual native ghost palette available "+ghostIds[index]);Check(Math.Abs(g.GetProperty("SizeMultiplier").GetSingle()-1)<.0001f&&Math.Abs(g.GetProperty("IncreaseFromPrevious").GetSingle()-1.5f)<.0001f,"live ghost exact50percent visual increase "+ghostIds[index]);
                    var draw=g.GetProperty("DrawPasses")[0];Check(draw.GetProperty("Opaque").GetBoolean()&&draw.GetProperty("SrcBlend").GetInt32()==1&&draw.GetProperty("DstBlend").GetInt32()==0&&draw.GetProperty("ZWrite").GetInt32()==1,"live ghost opaque body blend/depth "+ghostIds[index]);Check(Math.Abs(g.GetProperty("ObservedSourceColor")[3].GetSingle()-1)<.0001f,"live ghost actual MPB alpha1 "+ghostIds[index]);
                    var body=Body(current);var material=body.sharedMaterials[0];Check(material.GetInt("_SrcBlend")==1&&material.GetInt("_DstBlend")==0&&material.GetInt("_ZWrite")==1,"actual renderer material opaque One/Zero and depth "+ghostIds[index]);var actualBlock=new MaterialPropertyBlock();body.GetPropertyBlock(actualBlock,0);var actualColor=actualBlock.GetColor("_Color");var expectedColor=g.GetProperty("ExpectedSourceColor");Check(Math.Abs(actualColor.r-expectedColor[0].GetSingle())<.0001f&&Math.Abs(actualColor.g-expectedColor[1].GetSingle())<.0001f&&Math.Abs(actualColor.b-expectedColor[2].GetSingle())<.0001f&&Math.Abs(actualColor.a-1)<.0001f,"actual renderer RGB phase tint and alpha1 "+ghostIds[index],actualColor.ToString());var scaleInfo=J(Call(Bridge,"Describe",current));var expectedScale=scaleInfo.GetProperty("referenceHeight").GetSingle()/(scaleInfo.GetProperty("sourceHeight").GetSingle()*Math.Abs(current.transform.lossyScale.y));Check(Math.Abs(body.transform.localScale.y-expectedScale)<.0001f,"actual transform equals full native reference height "+ghostIds[index],body.transform.localScale.y+" expected "+expectedScale);capturedGhostScale=body.transform.lossyScale;CaptureOpaqueBody(body,"ghost-"+ghostIds[index]+"-opaque.ppm");Save("ghost-"+ghostIds[index]+"-live.json",g);Check((bool)Call(Bridge,"RestoreVisualForProbe",current)&&(bool)Call(Bridge,"RebindVisualForProbe",current),"actual ghost restore/rebind "+ghostIds[index]);Next(10);break;
                case 10:
                    if(Age<.2f)return;Check(Vector3.Distance(Body(current).transform.lossyScale,capturedGhostScale)<.0001f,"no accumulated ghost scale on rebind "+ghostIds[index]);if(index==0){deathGhost=current;killedId=current.id;deathGhost.MakeInvulnerable(false);deathGhost.Kill("NamesGhostProbe:ActualDeath");Next(11);}else NextGhost();break;
                case 11:
                    if(Age<.15f)return;var deaths=J(Call(Bridge,"DeathDiagnostics"));var corpse=deaths.GetProperty("Corpses").EnumerateArray().FirstOrDefault(c=>c.GetProperty("EnemyId").GetUInt32()==killedId);if(corpse.ValueKind==JsonValueKind.Undefined){if(Age>2)throw new InvalidOperationException("Actual ghost death corpse missing");return;}Check(corpse.GetProperty("NativeEnemyId").GetInt32()==16&&corpse.GetProperty("ScaleUnchanged").GetBoolean(),"actual ghost corpse preserves grown visual scale");Check(Vector3.Distance(new Vector3(corpse.GetProperty("CapturedScale")[0].GetSingle(),corpse.GetProperty("CapturedScale")[1].GetSingle(),corpse.GetProperty("CapturedScale")[2].GetSingle()),capturedGhostScale)<.0001f,"actual ghost death captures1.5scale once");Save("actual-ghost-death.json",deaths);NextGhost();break;
                case 12:
                    if(Age<.5f)return;var ordinaryInfo=J(Call(Bridge,"Describe",ordinary));Check(Math.Abs(ordinaryInfo.GetProperty("sizeMultiplier").GetSingle()-2f/3)<.0001f,"ordinary mob scale remains two-thirds");var king=Spawn(43,true);Next(13);current=king;break;
                case 13:
                    if(current.IsTeleporting()||Age<1.2f)return;Check(!J(Call(Bridge,"DescribeGhostPalette",current)).GetProperty("Attached").GetBoolean(),"GhostKing boss excluded from endless ghost changes");Save("final-boss-name-state.json",J(Get(Bridge,"BossNameDiagnosticsJson")));Check(J(Get(Bridge,"BossNameDiagnosticsJson")).GetProperty("Failures").GetInt32()==0,"no production caption runtime failures");Finish(null);break;
            }
        }
        catch(Exception e){Finish(e is TargetInvocationException t?t.InnerException??e:e);}
    }
    float Age=>Time.realtimeSinceStartup-entered;
    [HideFromIl2Cpp] void Next(int p){phase=p;entered=Time.realtimeSinceStartup;}
    [HideFromIl2Cpp] void SpawnBoss(){current=Spawn(bosses[index].Id,true);Next(4);}
    [HideFromIl2Cpp] void SpawnGhost(){current=Spawn(ghostIds[index],false);Next(9);}
    [HideFromIl2Cpp] void NextGhost(){if(current!=null)current.gameObject.SetActive(false);index++;if(index<ghostIds.Length)SpawnGhost();else Next(12);}
    [HideFromIl2Cpp] Enemy Spawn(int id,bool boss){var at=MyPlayer.Instance.transform.position+Vector3.forward*15+Vector3.up*2;Enemy e;if(boss)e=EnemyManager.Instance.SpawnBoss((EEnemy)id,-1,EEnemyFlag.Boss,at,1);else{var data=Resources.FindObjectsOfTypeAll<EnemyData>().First(d=>d!=null&&(int)d.enemyName==id);e=EnemyManager.Instance.SpawnEnemy(data,at,0,true,EEnemyFlag.None,false,1);}if(e==null)throw new InvalidOperationException("Native spawn failed "+id);nativeNames[e.id]=e.enemyData.GetName();e.hp=e.maxHp=10000000;e.MakeInvulnerable(true);actors.Add(e);return e;}
    [HideFromIl2Cpp] static TargetOfInterestPrefab FindLabel(Enemy e)=>Object.FindObjectsOfType<TargetOfInterestPrefab>(true).FirstOrDefault(ui=>ui!=null&&ui.gameObject.activeInHierarchy&&ui.enemy!=null&&ui.enemy.Pointer==e.Pointer&&ui.t_name!=null);
    [HideFromIl2Cpp] static MeshRenderer Body(Enemy e)=>e.GetComponentsInChildren<MeshRenderer>(true).First(r=>r!=null&&r.gameObject.activeInHierarchy&&r.name.StartsWith("AzerothCreature_",StringComparison.Ordinal));
    [HideFromIl2Cpp] void SetLocale(string code){SourceLocale locale=null;var available=LocalizationSettings.AvailableLocales.Locales;for(int i=0;i<available.Count;i++){var item=available[i];if(item.Identifier.Code.StartsWith(code,StringComparison.OrdinalIgnoreCase)){locale=item;break;}}if(locale==null)throw new InvalidOperationException("Native locale missing "+code);LocalizationSettings.SelectedLocale=locale;}
    [HideFromIl2Cpp] void CaptureOpaqueBody(MeshRenderer body,string name)
    {
        var original=body.GetComponent<MeshFilter>().sharedMesh;var mesh=Object.Instantiate(original);mesh.subMeshCount=1;var indices=original.GetTriangles(0);mesh.SetTriangles(indices,0);var vertices=original.vertices;var bounds=new Bounds(vertices[indices[0]],Vector3.zero);foreach(int i in indices)bounds.Encapsulate(vertices[i]);mesh.bounds=bounds;
        var go=new GameObject("QA_actual_ghost_opaque_body");go.layer=31;go.transform.localScale=body.transform.lossyScale;var filter=go.AddComponent<MeshFilter>();filter.sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=body.sharedMaterials[0];var block=new MaterialPropertyBlock();body.GetPropertyBlock(block,0);renderer.SetPropertyBlock(block,0);var b=renderer.bounds;
        var rig=new GameObject("QA_ghost_camera");var camera=rig.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.magenta;camera.orthographic=true;camera.orthographicSize=Mathf.Max(b.size.x,b.size.y,b.size.z)*.65f;camera.nearClipPlane=.01f;camera.farClipPlane=1000;camera.transform.position=b.center+new Vector3(1,.2f,1).normalized*Mathf.Max(10,b.size.magnitude*3);camera.transform.LookAt(b.center);
        var rt=new RenderTexture(256,256,24);rt.Create();var previous=RenderTexture.active;Texture2D image=null;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(256,256,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();var pixels=image.GetPixels32();var rgb=new byte[pixels.Length*3];int at=0,visible=0;for(int y=255;y>=0;y--)for(int x=0;x<256;x++){var p=pixels[y*256+x];rgb[at++]=p.r;rgb[at++]=p.g;rgb[at++]=p.b;if(!(p.r>245&&p.g<10&&p.b>245))visible++;}using(var file=File.Create(Path.Combine(folder,name))){file.Write(System.Text.Encoding.ASCII.GetBytes("P6\n256 256\n255\n"));file.Write(rgb);}Check(visible>64,"actual opaque-body shader produces visible pixels "+ghostIds[index],visible+" pixels; actual native mesh frame/material/MPB/scale, body submesh only");}
        finally{go.SetActive(false);camera.targetTexture=null;RenderTexture.active=previous;rt.Release();Object.Destroy(rt);Object.Destroy(mesh);Object.Destroy(go);Object.Destroy(rig);if(image!=null)Object.Destroy(image);}
    }
    [HideFromIl2Cpp] static Type Named(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).First(t=>t!=null);
    [HideFromIl2Cpp] static object Call(string type,string method,params object[] args)=>Named(type).GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);
    [HideFromIl2Cpp] static object Get(string type,string property)=>Named(type).GetProperty(property).GetValue(null);
    [HideFromIl2Cpp] static JsonElement J(object value){using var d=JsonDocument.Parse(value as string??JsonSerializer.Serialize(value));return d.RootElement.Clone();}
    [HideFromIl2Cpp] void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),value as string??JsonSerializer.Serialize(value));
    [HideFromIl2Cpp] void Check(bool condition,string label,string detail=""){checks.Add(new{Pass=condition,Label=label,Detail=detail});if(condition)passed++;else{failed++;throw new InvalidOperationException(label+": "+detail);}}
    [HideFromIl2Cpp] void Finish(Exception error){if(done)return;done=true;if(error!=null){failed++;checks.Add(new{Pass=false,Label="native fixture",Detail=error.ToString()});}try{if(initialLocale!=null)LocalizationSettings.SelectedLocale=initialLocale;}catch{}foreach(var m in input)try{if(m.Module!=null)m.Module.enabled=m.Enabled;}catch{}Save("summary.json",new{Pass=failed==0,Passed=passed,Failed=failed,Phase=phase,Index=index,Seconds=Time.realtimeSinceStartup-began,Checks=checks,Error=error?.ToString(),Scope="Owned hidden process; actual native Enemy/UI/locale and source mesh/material captures. Isolated body captures do not establish full gameplay visual fidelity."});Application.Quit(failed==0?0:1);}
}
[HarmonyPatch]
static class ProbeButtons{static bool Prepare()=>Probe.Armed;static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));static bool Prefix(ref bool __result){__result=false;return false;}}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class ProbeAxes{static bool Prepare()=>Probe.Armed;static bool Prefix(ref float __result){__result=0;return false;}}
