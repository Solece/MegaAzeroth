using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using Assets.Scripts.Utility;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Armed native fixture: independent source topology/material contract, real
// rendered pose snapshots and real native Kill. No production appearance hook.
public sealed class EnemyVarietyProbe:MonoBehaviour
{
    public EnemyVarietyProbe(IntPtr p):base(p){}
    const string Bridge="AzerothMegabonk.EnemyVisuals.EnemyVisualBridge";
    readonly List<JsonElement> models=new();readonly List<Enemy> actors=new();readonly List<Vector3> holds=new();readonly List<object> checks=new(),motionRows=new(),deathRows=new();readonly Dictionary<uint,Vector3> displaced=new();readonly Dictionary<string,Type> types=new();readonly List<(BaseInputModule Module,bool Enabled,int Id)> modules=new();
    readonly Dictionary<uint,float> nativeBossReferenceHeights=new();readonly HashSet<string> poseHashes=new();readonly HashSet<int> motionFrames=new();
    string folder;int phase,index,deathIndex,deathSample,passed,failed,skipped;float began,entered,nextPose;bool done;Vector3 motionStart;Enemy current;uint currentId;JsonElement firstDeath,deathBefore,deathVertexMap;AsyncOperation unload;
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-enemy-variety-smoke");
    void Start()
    {
        if(!Armed){enabled=false;return;}began=entered=Time.realtimeSinceStartup;folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports","enemy-variety-"+Environment.ProcessId));Directory.CreateDirectory(folder);
        try{Require(Environment.GetCommandLineArgs().Contains("-azeroth-test-battle"),"explicit native battle/save guard");Require(Environment.GetEnvironmentVariable("AZEROTH_LAB_RENDER_PROBE")=="1","native graphics explicitly armed");var source=FindSourceContract();var contract=J(File.ReadAllText(source));Require(S(contract,"format")=="azeroth-enemy-variety-source-contract-v1","independent original source contract format");foreach(var row in P(contract,"models").EnumerateArray())models.Add(row.Clone());Require(models.Count==8,"all eight selected original models in source contract");Save("source-contract.json",File.ReadAllText(source));Save("source-contract-origin.json",new{path=source,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)))});}catch(Exception e){Finish(e);}
    }
    void Update()
    {
        if(done||!Armed)return;
        try
        {
            if(Time.realtimeSinceStartup-began>120)throw new TimeoutException("Enemy variety native fixture exceeded120s");IsolateInput();var player=MyPlayer.Instance;
            if(player?.inventory?.playerHealth!=null){player.inventory.playerHealth.hp=player.inventory.playerHealth.maxHp;player.inventory.playerHealth.damageCooldown=10;}
            if(LevelupScreen.isLevelingUp){var w=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(x=>x.gameObject.activeInHierarchy);if(w!=null){Save("incidental-xp-"+Time.frameCount+".json",new{player.inventory.playerXp.level,xp=player.inventory.playerXp.GetXpInt()});w.CloseLevelupScreen();}return;}
            for(int i=0;i<actors.Count;i++)if(actors[i]!=null&&!actors[i].IsDeadOrDyingNextFrame()&&(actors[i].id!=currentId||phase!=2)){var actual=actors[i].transform.position;actors[i].transform.position=new Vector3(holds[i].x,actual.y,holds[i].z);}DisplaceOthers();
            switch(phase)
            {
                case 0:
                    if(!ElwynnBridge.Ready||player?.inventory==null||!player.hasStarted||MapGenerationController.isGenerating||GameManager.Instance?.IsPlaying()!=true)return;
                    Require((bool)Call("AzerothAbilities.TomeCompatibilityDiagnostics","SetAutomaticCastingForProbe",false),"disable incidental source automatic casts");Require((bool)Call("AzerothAbilities.ClassAbilityDiagnostics","ClearEffectsForProbe"),"clear incidental source pets/effects");Save("runtime-catalog.json",Get(Bridge,"VarietyCatalogJson"));SpawnModel();break;
                case 1:
                    if(current.IsTeleporting()||Age<1.2f){if(Age>12)throw new TimeoutException("Native spawn admission: "+Key);return;}
                    if(!P(Diagnostics(current),"SourceMaterialProfileLoaded").GetBoolean()){if(Age>12)throw new TimeoutException("Original source model/materials never attached: "+Key);return;}
                    Validate(current,models[index],"initial");motionStart=current.transform.position;poseHashes.Clear();motionFrames.Clear();nextPose=0;phase=2;entered=Time.realtimeSinceStartup;break;
                case 2:
                    var motion=Diagnostics(current);if(Age>=nextPose){nextPose=Age+.2f;motionFrames.Add(P(motion,"CurrentFrame").GetInt32());poseHashes.Add(PoseHash(Body(current).GetComponent<MeshFilter>().sharedMesh));motionRows.Add(new{key=Key,nativeId=(int)current.enemyData.enemyName,age=Age,position=V(current.transform.position),clip=S(motion,"CurrentClip"),frame=P(motion,"CurrentFrame").GetInt32()});}
                    if(Age<.85f)return;
                    Check(P(motion,"FrameCount").GetInt32()>1&&motionFrames.Count>1,"actual source animation frames advance "+Key,S(motion,"CurrentClip")+":"+string.Join(",",motionFrames));
                    Check(poseHashes.Count>1,"actual native source mesh pose changes "+Key,poseHashes.Count+" distinct geometry snapshots");Save(Key+"-motion.json",new{naturalNativeDisplacement=Vector3.Distance(current.transform.position,motionStart),current=motion,scope="Actual native AI movement/idle animation; no forced visual clip or fabricated attack callback."});
                    Validate(current,models[index],"motion");Capture(Key+"-front.ppm",Body(current),false);Capture(Key+"-back.ppm",Body(current),true);OpaqueControls(current,models[index]);
                    holds[actors.IndexOf(current)]=current.transform.position;index++;if(index<models.Count)SpawnModel();else{currentId=0;phase=3;entered=Time.realtimeSinceStartup;SpawnRegressionAndAliases();}break;
                case 3:
                    if(actors.Any(a=>a!=null&&!a.IsDeadOrDyingNextFrame()&&a.IsTeleporting())){if(Age>12)throw new TimeoutException("Regression/alias spawn admission");return;}if(Age<1.2f)return;
                    ValidateRegressions();Save("before-unload.json",Call(Bridge,"AssetDiagnostics"));unload=Resources.UnloadUnusedAssets();phase=4;entered=Time.realtimeSinceStartup;break;
                case 4:
                    if(!unload.isDone||Age<.4f)return;
                    Check((bool)Get(Bridge,"AllCachedAssetsValid"),"all used source assets survive actual native UnloadUnusedAssets");for(int i=0;i<8;i++)Validate(actors[i],models[i],"after-native-unload");ValidateRegressions();Save("after-unload.json",Call(Bridge,"AssetDiagnostics"));Save("motion-samples.json",motionRows);deathIndex=0;StartDeath();break;
                case 5:
                    var death=J(Call(Bridge,"DeathDiagnostics"));var corpse=P(death,"Corpses").EnumerateArray().FirstOrDefault(c=>P(c,"EnemyId").GetUInt32()==currentId);
                    if(corpse.ValueKind==JsonValueKind.Undefined){if(deathIndex==7&&(MapGenerationController.isGenerating||GameManager.Instance?.isGameOver==true||MyTime.paused)&&Age>1){Record("SKIP","last boss death progression interrupted by native stage/run transition",death.GetRawText());Finish(null);return;}if(Age>3)throw new InvalidOperationException("Real native Kill did not retain matching source corpse: "+KeyForDeath);return;}
                    if(P(corpse,"Age").GetSingle()<(deathSample==0?.08f:.6f)){if(Age>5)throw new TimeoutException("Native death clock blocked: "+J(Call(Bridge,"AnimationDiagnostics")));return;}
                    Check(S(corpse,"ModelKey")==KeyForDeath&&P(corpse,"SourceDeathAnimationId").GetInt32()==1&&P(corpse,"FrameCount").GetInt32()>1,"actual native death selects original animated source model "+KeyForDeath);
                    Check(P(corpse,"Visible").GetBoolean()&&P(corpse,"AssetsValid").GetBoolean()&&!P(corpse,"HasCollider").GetBoolean()&&!P(corpse,"NativeActorChild").GetBoolean(),"actual detached source corpse visible/valid "+KeyForDeath);
                    var corpseBody=Object.FindObjectsOfType<MeshRenderer>().FirstOrDefault(r=>r!=null&&r.name=="AzerothCorpse_"+KeyForDeath+"_"+currentId);if(corpseBody==null)throw new InvalidOperationException("Actual matching source corpse MeshRenderer absent");ValidateMeshAndMaterials(corpseBody,models[deathIndex],P(corpse,"SourceMaterials"),deathVertexMap);Capture(KeyForDeath+"-death-"+deathSample+".ppm",corpseBody,false);Save(KeyForDeath+"-death-"+deathSample+".json",new{death,corpse});
                    if(deathSample++==0){firstDeath=corpse;return;}Check(P(corpse,"Frame").GetInt32()>P(firstDeath,"Frame").GetInt32(),"actual source death frames advance "+KeyForDeath);deathRows.Add(corpse.Clone());phase=6;entered=Time.realtimeSinceStartup;break;
                case 6:
                    var after=J(Call(Bridge,"DeathDiagnostics"));if(P(after,"Corpses").EnumerateArray().Any(c=>P(c,"EnemyId").GetUInt32()==currentId)){if(Age>4)throw new TimeoutException("Source death lifetime failed to expire: "+KeyForDeath);return;}
                    Check(P(after,"Completed").GetInt32()>P(deathBefore,"Completed").GetInt32(),"actual source1.2s corpse lifetime expires "+KeyForDeath);deathIndex++;if(deathIndex<8)StartDeath();else Finish(null);break;
            }
        }
        catch(Exception e){Finish(e is TargetInvocationException t?t.InnerException??e:e);}
    }
    float Age=>Time.realtimeSinceStartup-entered;
    string Key=>S(models[index],"key");
    string KeyForDeath=>S(models[Math.Min(deathIndex,7)],"key");
    [HideFromIl2Cpp] void SpawnModel(){var row=models[index];int id=P(row,"nativeEnemyIds")[0].GetInt32();bool boss=P(row,"bossOnly").GetBoolean();var at=Floor(MyPlayer.Instance.transform.position+Forward()*(boss?24:12));current=Spawn(id,boss,at+Vector3.up*3);currentId=current.id;actors.Add(current);holds.Add(at);phase=1;entered=Time.realtimeSinceStartup;}
    [HideFromIl2Cpp] Enemy Spawn(int id,bool boss,Vector3 at){Enemy e;if(boss)e=EnemyManager.Instance.SpawnBoss((EEnemy)id,-1,EEnemyFlag.Boss,at,1);else{var data=Resources.FindObjectsOfTypeAll<EnemyData>().FirstOrDefault(d=>d!=null&&(int)d.enemyName==id);Require(data!=null,"actual native EnemyData "+id);e=EnemyManager.Instance.SpawnEnemy(data,at,0,true,EEnemyFlag.None,false,1);}Require(e!=null,"real native spawn "+id);if(boss){float height=e.renderer.bounds.size.y;nativeBossReferenceHeights[e.id]=height;Save("native-boss-spawn-reference-"+e.id+".json",new{enemyId=e.id,nativeId=id,frame=Time.frameCount,nativeRendererHeight=height,nativeRendererCenter=V(e.renderer.bounds.center),nativeRendererSize=V(e.renderer.bounds.size),rootPosition=V(e.transform.position),rootScale=V(e.transform.lossyScale),colliderHeight=e.collider.height,colliderCenter=V(e.collider.bounds.center),scope="Frozen immediately after actual native SpawnBoss return, before next LateUpdate; animated native bounds remain separately observed."});}e.maxHp=10000000;e.hp=e.maxHp;return e;}
    readonly Dictionary<uint,JsonElement> savedDiagnostics=new();
    [HideFromIl2Cpp] void Validate(Enemy e,JsonElement expected,string label)
    {
        var d=Diagnostics(e);savedDiagnostics[e.id]=d;string key=S(expected,"key");Save(key+"-"+label+".json",d);
        Check(S(d,"ActualKey")==key&&S(d,"ExpectedKey")==key&&P(d,"NativeId").GetInt32()==(int)e.enemyData.enemyName,"actual native mapping "+key+" "+label);
        Check(P(d,"SourceDisplayId").GetInt32()==P(expected,"displayId").GetInt32()&&S(d,"SourceSha256")==S(expected,"sourceSha256"),"original source display/hash "+key+" "+label);
        Check(P(d,"AssetsValid").GetBoolean()&&P(d,"DeathProfileLoaded").GetBoolean(),"source live/death assets valid "+key+" "+label);
        var bodies=e.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r!=null&&r.gameObject.activeInHierarchy&&r.name.StartsWith("AzerothCreature_",StringComparison.Ordinal)).ToArray();Check(bodies.Length==1&&P(d,"MeshRenderers").GetInt32()==1,"one source body, no doubled boss/model "+key+" "+label);if(bodies.Length!=1)throw new InvalidOperationException("Source body count invalid");var body=bodies[0];
        Check(body.enabled&&!body.forceRenderingOff&&e.renderer!=null&&!e.renderer.enabled&&e.collider!=null&&e.collider.enabled,"source body visible/native body hidden/collider retained "+key+" "+label);
        float floor=Floor(e.transform.position).y;Save(key+"-"+label+"-native-floor.json",new{rootPosition=V(e.transform.position),nativeColliderCenter=V(e.collider.bounds.center),nativeColliderMinimum=V(e.collider.bounds.min),sourceMinimum=V(body.bounds.min),floorY=floor,pinning="XZ only; native Y/gravity retained"});Check(body.bounds.min.y>=floor-.3f,"source body above independent native terrain "+key+" "+label,"min="+body.bounds.min.y+",floor="+floor);
        var scale=P(d,"NativeScaleEvidence");float reference=e.IsBoss()?nativeBossReferenceHeights[e.id]:(e.collider.direction==1?e.collider.height:e.collider.radius*2)*Mathf.Abs(e.collider.transform.lossyScale.y);float normalized=P(scale,"sourceHeight").GetSingle()*P(scale,"currentLocalScale").GetSingle()*Mathf.Abs(e.transform.lossyScale.y)/Mathf.Max(.00001f,reference);Check(Mathf.Abs(normalized-(e.IsBoss()?1f:2f/3))<.03f,"independent native scale normal2/3 boss1 "+key+" "+label,"ratio="+normalized+",reference="+reference+",currentNativeAnimatedHeight="+e.renderer.bounds.size.y);
        ValidateMeshAndMaterials(body,expected,P(d,"SourceMaterials"),P(d,"SourceVertexMap"));
        var sourceAnimations=P(expected,"animations").EnumerateArray().ToArray();Check(new[]{"idle","run","walk","attack","death"}.All(name=>sourceAnimations.Any(a=>S(a,"name")==name&&P(a,"frameCount").GetInt32()>1)),"all five original animated sequences present "+key+" "+label);
    }
    [HideFromIl2Cpp] void ValidateMeshAndMaterials(MeshRenderer body,JsonElement expected,JsonElement source,JsonElement vertexMap)
    {
        string key=S(expected,"key");var mesh=body.GetComponent<MeshFilter>().sharedMesh;var map=vertexMap.EnumerateArray().Select(x=>x.GetInt32()).ToArray();var passes=P(expected,"sourcePasses").EnumerateArray().ToArray();var actual=P(source,"MaterialsSource").EnumerateArray().ToArray();
        Check(mesh!=null&&mesh.vertexCount==P(expected,"exportVertexCount").GetInt32()&&map.Length==mesh.vertexCount,"actual native vertex/export map count "+key);Check(mesh.subMeshCount==passes.Length&&body.sharedMaterials.Length==passes.Length&&actual.Length==passes.Length,"all original source passes actually bound "+key);
        Check(mesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"actual native animated vertices finite "+key);
        for(int i=0;i<passes.Length;i++)
        {
            var p=passes[i];var m=body.sharedMaterials[i];var report=actual[i];var indices=mesh.GetTriangles(i).ToArray();Check(indices.Length==P(p,"sourceIndexCount").GetInt32()&&Canonical(indices,map)==S(p,"sourceCanonicalTriangleSha256"),"independent native canonical source triangles "+key+" pass"+i);
            int flags=P(p,"flags").GetInt32(),blend=P(p,"blend").GetInt32();var texture=P(expected,"sourceTextures").EnumerateArray().First(t=>P(t,"id").GetInt32()==P(p,"textureId").GetInt32());Check(S(report,"SourceTextureSha256")==S(texture,"sourceSha256")&&m!=null&&m.mainTexture!=null,"original source texture identity/resources "+key+" pass"+i);
            var block=new MaterialPropertyBlock();body.GetPropertyBlock(block,i);var color=block.HasColor("_Color")?block.GetColor("_Color"):m.color;var expectedColor=P(report,"SourceColor").EnumerateArray().Select(v=>v.GetSingle()).ToArray();Check(new[]{color.r,color.g,color.b,color.a}.Select((v,n)=>Mathf.Abs(v-expectedColor[n])<.0005f).All(v=>v),"actual native slot color/opacity matches original source track "+key+" pass"+i);
            if(P(report,"VisibilitySuppressed").GetBoolean()){Check(m.GetInt("_SrcBlend")==0&&m.GetInt("_DstBlend")==1&&m.GetInt("_ZWrite")==0,"source opacity-zero pass writes neither color nor depth "+key+" pass"+i);continue;}
            Check(m.mainTexture.name=="AzerothEnemy_"+key+"_Texture_"+P(p,"textureId").GetInt32()&&m.mainTexture.width==P(texture,"width").GetInt32()&&m.mainTexture.height==P(texture,"height").GetInt32(),"actual native visible-pass texture binding "+key+" pass"+i);
            Check(m.GetInt("_Cull")==((flags&4)!=0?0:2)&&m.GetInt("_ZWrite")==((flags&16)!=0?0:1),"actual source cull/depth flags "+key+" pass"+i);
            Check(m.GetInt("_SrcBlend")==((blend==2||blend==4)?5:1)&&m.GetInt("_DstBlend")==((blend==4)?1:(blend==2)?10:0)&&m.shaderKeywords.Any(k=>k=="_ALPHATEST_ON")== (blend==1),"actual source blend/alpha mode, no blanket cutout "+key+" pass"+i);
        }
    }
    [HideFromIl2Cpp] void OpaqueControls(Enemy e,JsonElement row)
    {
        var body=Body(e);var mesh=body.GetComponent<MeshFilter>().sharedMesh;var materials=P(Diagnostics(e),"SourceMaterials");var sourceRows=P(materials,"MaterialsSource").EnumerateArray().ToArray();var passes=P(row,"sourcePasses").EnumerateArray().ToArray();
        for(int i=0;i<passes.Length;i++)if(P(passes[i],"blend").GetInt32()==0&&!P(sourceRows[i],"VisibilitySuppressed").GetBoolean())
        {
            var clone=Object.Instantiate(mesh);clone.subMeshCount=1;clone.SetTriangles(mesh.GetTriangles(i),0);var original=body.sharedMaterials[i];var reference=new Material(Shader.Find("Unlit/Texture")){mainTexture=original.mainTexture};
            try{var actual=Capture(S(row,"key")+"-opaque-pass"+i+"-actual.ppm",body,false,clone,original,i);var opaque=Capture(S(row,"key")+"-opaque-pass"+i+"-reference.ppm",body,false,clone,reference,i);int missing=0,coverage=0;for(int p=0;p<opaque.Length;p+=3){bool visible=!Background(opaque,p);if(visible){coverage++;if(Background(actual,p))missing++;}}Save(S(row,"key")+"-opaque-pass"+i+"-pixels.json",new{coverage,missing,scope="Same actual native source geometry/pose, current material versus independent opaque Unlit/Texture alpha-ignored reference; no source triangle mutation."});Check(coverage>0&&missing<=Math.Max(3,coverage*.005f),"opaque source geometry has no alpha-cutout holes "+S(row,"key")+" pass"+i,"missing="+missing+",reference="+coverage);}
            finally{Object.Destroy(clone);Object.Destroy(reference);}
        }
    }
    [HideFromIl2Cpp] byte[] Capture(string name,MeshRenderer body,bool back,Mesh overrideMesh=null,Material overrideMaterial=null,int propertySlot=-1)
    {
        var go=new GameObject("EnemyVarietyInspectionPose");go.layer=31;go.transform.position=body.transform.position;go.transform.rotation=body.transform.rotation;go.transform.localScale=body.transform.lossyScale;go.AddComponent<MeshFilter>().sharedMesh=overrideMesh??body.GetComponent<MeshFilter>().sharedMesh;var render=go.AddComponent<MeshRenderer>();render.sharedMaterials=overrideMaterial!=null?new[]{overrideMaterial}:body.sharedMaterials;var block=new MaterialPropertyBlock();body.GetPropertyBlock(block);render.SetPropertyBlock(block);for(int i=0;i<render.sharedMaterials.Length;i++){body.GetPropertyBlock(block,propertySlot>=0?propertySlot:i);render.SetPropertyBlock(block,i);}
        var cameraGo=new GameObject("EnemyVarietyInspectionCamera");var camera=cameraGo.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(1,0,1,1);camera.fieldOfView=40;var at=body.bounds.center;float distance=Mathf.Max(2,Mathf.Max(body.bounds.size.y,body.bounds.size.x)*2.1f);camera.transform.position=at+body.transform.forward*(back?-distance:distance)+Vector3.up*distance*.12f;camera.transform.LookAt(at);
        var rt=new RenderTexture(960,540,24);rt.Create();var active=RenderTexture.active;Texture2D image=null;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();var pixels=image.GetPixels32();var bytes=new byte[960*540*3];int n=0,visible=0;for(int y=539;y>=0;y--)for(int x=0;x<960;x++){var p=pixels[y*960+x];bytes[n++]=p.r;bytes[n++]=p.g;bytes[n++]=p.b;if(!(p.r>250&&p.g<5&&p.b>250))visible++;}using var f=File.Create(Path.Combine(folder,name));f.Write(System.Text.Encoding.ASCII.GetBytes("P6\n960 540\n255\n"));f.Write(bytes);if(overrideMesh==null)Check(visible>20,"actual camera source silhouette visible "+name,visible+" pixels");return bytes;}
        finally{go.SetActive(false);camera.targetTexture=null;RenderTexture.active=active;rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);Object.Destroy(cameraGo);Object.Destroy(go);}
    }
    [HideFromIl2Cpp] void SpawnRegressionAndAliases()
    {
        var player=MyPlayer.Instance;foreach(var row in models)foreach(var id in P(row,"nativeEnemyIds").EnumerateArray().Skip(1)){var at=actors[actors.Count%8].transform.position;actors.Add(Spawn(id.GetInt32(),P(row,"bossOnly").GetBoolean(),at+Vector3.up*3));holds.Add(at);}
        foreach(int id in new[]{16,17,40,39,48}){if(id==48&&((EEnemy)id).ToString()!="GhostInvincible")continue;var at=actors[actors.Count%8].transform.position;actors.Add(Spawn(id,false,at+Vector3.up*3));holds.Add(at);}foreach(int id in new[]{23,43}){var at=actors[actors.Count%8].transform.position;actors.Add(Spawn(id,true,at+Vector3.up*3));holds.Add(at);}
    }
    [HideFromIl2Cpp] void ValidateRegressions()
    {
        foreach(var e in actors.Skip(8))
        {
            int id=(int)e.enemyData.enemyName;var expected=models.FirstOrDefault(m=>P(m,"nativeEnemyIds").EnumerateArray().Any(x=>x.GetInt32()==id));if(expected.ValueKind!=JsonValueKind.Undefined){Validate(e,expected,"native-alias-"+id);continue;}
            if(id is 16 or 17 or 40 or 39 or 48){var ghost=J(Call(Bridge,"DescribeGhostPalette",e));Save("ghost-preserved-"+id+".json",ghost);Check(P(ghost,"Attached").GetBoolean()&&S(ghost,"SourceModel")=="ghost"&&P(ghost,"SourceDisplayId").GetInt32()==146&&P(ghost,"PassCount").GetInt32()==3&&P(ghost,"SourcePassCount").GetInt32()==26&&P(ghost,"Visible").GetBoolean()&&P(ghost,"PaletteAvailable").GetBoolean(),"existing source Ghost override/palette/pass preservation "+id);continue;}
            var native=J(Call(Bridge,"Describe",e));Save("legacy-boss-"+id+".json",native);Check(e.IsBoss()&&e.GetComponentsInChildren<MeshRenderer>(true).Count(r=>r!=null&&r.gameObject.activeInHierarchy&&r.name.StartsWith("AzerothCreature_",StringComparison.Ordinal))==1&&P(native,"nativeRendererHidden").GetBoolean(),"legacy boss has exactly one source body/native hidden "+id);if(id==43)Check(!P(J(Call(Bridge,"DescribeGhostPalette",e)),"Attached").GetBoolean(),"native GhostKing43 excluded from ordinary ghost override");
        }
    }
    [HideFromIl2Cpp] void StartDeath(){current=actors[deathIndex];currentId=current.id;deathVertexMap=P(savedDiagnostics[currentId],"SourceVertexMap");deathBefore=J(Call(Bridge,"DeathDiagnostics"));deathSample=0;current.Kill("EnemyVarietyProbe:RealNativeDeath");var after=J(Call(Bridge,"DeathDiagnostics"));Check(P(after,"TotalCreated").GetInt32()>P(deathBefore,"TotalCreated").GetInt32(),"real native Kill creates source death presentation "+KeyForDeath);Save(KeyForDeath+"-native-kill.json",new{enemyId=currentId,death=after});phase=5;entered=Time.realtimeSinceStartup;}
    [HideFromIl2Cpp] JsonElement Diagnostics(Enemy e)=>J(Call(Bridge,"VarietyDiagnostics",e));
    [HideFromIl2Cpp] static MeshRenderer Body(Enemy e)=>e.GetComponentsInChildren<MeshRenderer>(true).First(r=>r!=null&&r.gameObject.activeInHierarchy&&r.name.StartsWith("AzerothCreature_",StringComparison.Ordinal));
    [HideFromIl2Cpp] static string Canonical(int[] indices,int[] map){var triangles=new List<int[]>();for(int i=0;i<indices.Length;i+=3){var t=new[]{map[indices[i]],map[indices[i+1]],map[indices[i+2]]};Array.Sort(t);triangles.Add(t);}triangles.Sort((a,b)=>a[0]!=b[0]?a[0].CompareTo(b[0]):a[1]!=b[1]?a[1].CompareTo(b[1]):a[2].CompareTo(b[2]));using var stream=new MemoryStream();using var w=new BinaryWriter(stream);foreach(var t in triangles)foreach(int v in t)w.Write(v);return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();}
    [HideFromIl2Cpp] static string PoseHash(Mesh mesh){using var stream=new MemoryStream();using var w=new BinaryWriter(stream);foreach(var v in mesh.vertices){w.Write(v.x);w.Write(v.y);w.Write(v.z);}return Convert.ToHexString(SHA256.HashData(stream.ToArray()));}
    [HideFromIl2Cpp] static bool Background(byte[] b,int n)=>b[n]>250&&b[n+1]<5&&b[n+2]>250;
    [HideFromIl2Cpp] void DisplaceOthers(){if(MyPlayer.Instance==null||EnemyManager.Instance?.enemies==null)return;foreach(var p in EnemyManager.Instance.enemies){var e=p.Value;if(e==null||actors.Any(a=>a!=null&&a.id==p.Key))continue;if(!displaced.ContainsKey(p.Key))displaced[p.Key]=e.transform.position;e.transform.position=MyPlayer.Instance.transform.position+Vector3.back*100+Vector3.right*(p.Key%9);}}
    [HideFromIl2Cpp] static Vector3 Floor(Vector3 at){if(Physics.Raycast(at+Vector3.up*40,Vector3.down,out var hit,100,GameManager.Instance.whatIsGround.value,QueryTriggerInteraction.Ignore))return hit.point+Vector3.up*.1f;throw new InvalidOperationException("No real native floor under variety fixture");}
    [HideFromIl2Cpp] static Vector3 Forward(){var f=MyPlayer.Instance.transform.forward;f.y=0;return f.sqrMagnitude>.001f?f.normalized:Vector3.forward;}
    [HideFromIl2Cpp] void IsolateInput(){foreach(var m in Resources.FindObjectsOfTypeAll<BaseInputModule>())try{if(m==null||!m.gameObject.scene.IsValid())continue;int id=m.GetInstanceID();if(!modules.Any(x=>x.Id==id))modules.Add((m,m.enabled,id));m.enabled=false;}catch{}}
    [HideFromIl2Cpp] string FindSourceContract(){var explicitPath=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-azeroth-enemy-variety-contract="))?.Substring("-azeroth-enemy-variety-contract=".Length);if(explicitPath!=null)return Path.GetFullPath(explicitPath);for(var parent=new DirectoryInfo(BepInEx.Paths.GameRootPath);parent!=null;parent=parent.Parent){var path=Path.Combine(parent.FullName,"docs/source-contracts/enemy-variety-source.json");if(File.Exists(path))return path;}throw new FileNotFoundException("Independent source contract missing; supply -azeroth-enemy-variety-contract=<path>");}
    [HideFromIl2Cpp] Type Named(string name){if(types.TryGetValue(name,out var t))return t;return types[name]=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null)??throw new MissingMemberException(name);}
    [HideFromIl2Cpp] object Get(string type,string field)=>Named(type).GetProperty(field)?.GetValue(null)??throw new MissingMemberException(type,field);
    [HideFromIl2Cpp] object Call(string type,string method,params object[] args)=>Named(type).GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);
    [HideFromIl2Cpp] static JsonElement J(object v){using var d=JsonDocument.Parse(v as string??JsonSerializer.Serialize(v));return d.RootElement.Clone();}
    [HideFromIl2Cpp] static JsonElement P(JsonElement e,string n){foreach(var p in e.EnumerateObject())if(p.Name.Equals(n,StringComparison.OrdinalIgnoreCase))return p.Value;throw new MissingMemberException(n);}
    [HideFromIl2Cpp] static string S(JsonElement e,string n)=>P(e,n).GetString();
    [HideFromIl2Cpp] static float[] V(Vector3 p)=>new[]{p.x,p.y,p.z};
    [HideFromIl2Cpp] void Save(string n,object v)=>File.WriteAllText(Path.Combine(folder,n),v as string??JsonSerializer.Serialize(v));
    [HideFromIl2Cpp] void Check(bool okay,string n,string detail="")=>Record(okay?"PASS":"FAIL",n,detail);
    [HideFromIl2Cpp] void Require(bool okay,string n){Check(okay,n);if(!okay)throw new InvalidOperationException(n);}
    [HideFromIl2Cpp] void Record(string status,string n,string detail=""){if(status=="PASS")passed++;else if(status=="FAIL")failed++;else skipped++;checks.Add(new{status,label=n,detail});Plugin.Note("ENEMY VARIETY "+status+" "+n+" "+detail);}
    [HideFromIl2Cpp] void Finish(Exception error)
    {
        if(done)return;done=true;if(error!=null){Record("FAIL","native variety fixture",error.ToString());try{Save("failure-state.json",new{phase,index,deathIndex,clock=J(Call(Bridge,"AnimationDiagnostics")),death=J(Call(Bridge,"DeathDiagnostics"))});}catch{}}
        foreach(var m in modules)try{if(m.Module!=null)m.Module.enabled=m.Enabled;}catch{}try{if(EnemyManager.Instance?.enemies!=null)foreach(var p in displaced)if(EnemyManager.Instance.enemies.ContainsKey(p.Key)&&EnemyManager.Instance.enemies[p.Key]!=null)EnemyManager.Instance.enemies[p.Key].transform.position=p.Value;}catch{}
        Save("summary.json",new{pass=failed==0,passed,failed,skipped,phase,index,deathIndex,wallSeconds=Time.realtimeSinceStartup-began,checks,sourceModels=models.Select(m=>S(m,"key")),motionSamples=motionRows.Count,deathSamples=deathRows.Count,error=error?.ToString(),visualReference="UNVERIFIED",coverage="Eight actual native source models, all source passes/canonical topology, actual material states and opaque hole controls, native posed front/back camera gallery, actual animation meshes, native Kill/corpse progression/lifetime, aliases, legacy Golem/GhostKing uniqueness and minimal Ghost override preservation. Original-source client pixel equivalence and naturally triggered attack animation are not claimed."});Application.Quit(failed==0?0:2);
    }
}
[HarmonyPatch]
static class EnemyVarietyButtonIsolation
{static bool Prepare()=>EnemyVarietyProbe.Armed;static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));static bool Prefix(ref bool __result){__result=false;return false;}}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class EnemyVarietyAxisIsolation
{static bool Prepare()=>EnemyVarietyProbe.Armed;static bool Prefix(ref float __result){__result=0;return false;}}
