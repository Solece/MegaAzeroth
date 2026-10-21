using System.Reflection;
using System.Text.Json;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using Assets.Scripts.Utility;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Dedicated red/green Consecration regression. Missing visuals are accumulated
// as failures after the complete recording; they never abort the baseline clip.
public sealed class PaladinVfxContractProbe:MonoBehaviour
{
    public PaladinVfxContractProbe(IntPtr p):base(p){}
    const string Spell="consecration",Ability="AzerothAbilities.ClassAbilityDiagnostics",Tome="AzerothAbilities.TomeCompatibilityDiagnostics",Talent="MageTalents.TalentBridge",Vfx="AzerothAbilities.SpellVfxDiagnostics",Clock="AzerothAbilities.CombatSimulation";
    const int Width=960,Height=540,Fps=15,MaximumFrames=420;
    readonly Dictionary<string,Type> types=new();readonly List<object> checks=new(),frames=new(),hpTicks=new();readonly Dictionary<uint,Vector3> displaced=new();readonly List<(BaseInputModule Module,bool Enabled,int Id)> modules=new();
    string folder,talentState,contractPath;int phase,passed,failed,frame,roleIndex;float began,entered,castClock,castWall,nextCapture,previousCapture,lastSourceHp,firstHp,plannedDuration,tickEnd,interval,lastRemaining,lastHpChange=-1,primaryCaptureEnded,roleClock,baselineWall,lastCaptureWall;
    readonly List<object> roleProof=new(),orientationRows=new();VfxSourceContract roleContract;bool primaryCompleted,terrainEvidenceWritten,orientationFlagsSeen,orientationFlagsWrong,fixtureRelocated,fullParticleMetricsSeen,particleMetricsMissing;int peakEarlyParticles,lateParticlePeak,earlyOrientationSamples,lateOrientationSamples,maxEmissionBursts;long maximumTotalSpawned,maximumClipped;float maxParticleCenterDrift,maxNativeMeshCenterDrift,nextNativeParticleSample;int nativeParticleMeshSamples;Vector3 originalPlayerPosition;GameObject controlledStage;Material controlledStageMaterial;
    static readonly (float Seconds,string Stage)[] RoleSchedule={ (.1f,"cast"),(.333f,"cast"),(.4f,"impact"),(.5f,"area"),(.65f,"cast"),(1.3f,"impact"),(1.5f,"area"),(2.4f,"impact"),(6.5f,"area"),(7.4f,"area") };
    float pauseClock,lastUnstoppedAreaAge=-1,largestCaptureGap;bool done,pauseAdmitted,resumed,fieldScaleObserved,fieldScaleCorrect=true;Enemy target;uint targetId;Vector3 targetAt,center,cameraAt;Quaternion cameraLook;VfxSourceContract contract;JsonElement before,pauseBefore,initialArea;RenderTexture rt;Texture2D image;byte[] rgb;
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-paladin-vfx-smoke");
    void Start()
    {
        if(!Armed){enabled=false;return;}began=entered=Time.realtimeSinceStartup;folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports","paladin-vfx-"+Environment.ProcessId));Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,"frames"));
        try{Require(Environment.GetCommandLineArgs().Contains("-azeroth-test-battle"),"isolated native battle/save guard");Require(Environment.GetEnvironmentVariable("AZEROTH_LAB_RENDER_PROBE")=="1","native render probe explicitly enabled");Application.runInBackground=true;Application.targetFrameRate=60;QualitySettings.vSyncCount=0;}catch(Exception error){Finish(error);}
    }
    void Update()
    {
        if(done||!Armed)return;
        try
        {
            if(Time.realtimeSinceStartup-began>90)throw new TimeoutException("Paladin full-cycle fixture exceeded90s");IsolateInput();var player=MyPlayer.Instance;
            if(player?.inventory?.playerHealth!=null){player.inventory.playerHealth.hp=player.inventory.playerHealth.maxHp;player.inventory.playerHealth.damageCooldown=10;}
            if(LevelupScreen.isLevelingUp){var w=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(s=>s.gameObject.activeInHierarchy);if(w!=null){Save("incidental-xp-"+Time.frameCount+".json",new{level=player?.inventory?.playerXp?.level,xp=player?.inventory?.playerXp?.GetXpInt()});w.CloseLevelupScreen();}return;}
            if(target!=null&&target.id==targetId&&!target.IsDeadOrDyingNextFrame()){target.transform.position=targetAt;DisplaceOtherEnemies();}
            switch(phase)
            {
                case 0:
                    if(!ElwynnBridge.Ready||player?.inventory==null||!player.hasStarted||MapGenerationController.isGenerating||GameManager.Instance?.IsPlaying()!=true||!(bool)Get(Clock,"CanSimulate"))return;
                    Require((string)Get(Ability,"ActiveClassId")=="paladin","actual selected source class is paladin");Require((bool)Call(Tome,"SetAutomaticCastingForProbe",false),"suppress incidental automatic casts");
                    talentState=(string)Get(Talent,"StateJson");var state=J(talentState);Require((bool)Call(Talent,"SetProbeState",P(state,"TalentXp").GetInt64(),"{}"),"isolate source talent procs in memory");Call(Talent,"ApplyDebugNow");
                    Require((bool)Call(Tome,"UseIsolatedTomeForProbe","Quantity",0,1),"isolate native tome multipliers");Require((bool)Call(Ability,"LearnAllForProbe"),"learn original active class catalog");Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear existing spell effects and pets");
                    originalPlayerPosition=player.transform.position;var patch=SelectFlatNativeGround();player.playerMovement.TeleportPlayer(patch+Vector3.up*1.1f);fixtureRelocated=true;
                    var data=Resources.FindObjectsOfTypeAll<EnemyData>().FirstOrDefault(d=>d!=null&&(int)d.enemyName==0);Require(data!=null,"native ordinary Skeleton data");targetAt=Floor(patch+Forward()*1.8f);target=EnemyManager.Instance.SpawnEnemy(data,targetAt,0,true,EEnemyFlag.None,false,1);Require(target!=null,"actual ordinary native durable target");targetId=target.id;target.maxHp=10000000;target.hp=target.maxHp;phase=1;entered=Time.realtimeSinceStartup;break;
                case 1:
                    if(target.IsTeleporting()||Age<.5f||!(bool)Get(Clock,"CanSimulate")){if(Age>12)throw new TimeoutException("Native target admission");return;}
                    contractPath=Arg("-azeroth-paladin-vfx-contract=")??Path.Combine(BepInEx.Paths.PluginPath,"AzerothPaladinVfxProbe","consecration-contract.json");
                    center=Floor(player.transform.position);SetCaptureCamera();CreateCapture();
                    // Graphics-only warmup, not a source cast: prime original
                    // shaders/readback, then clear and wait before the baseline.
                    foreach(string stage in new[]{"cast","area","impact"})Call(Vfx,"PlayForProbe",Spell,stage,center,1f);
                    phase=10;entered=Time.realtimeSinceStartup;break;
                case 10:
                    if(Age<.65f)return;Capture(false,false);Save("graphics-warmup.json",new{sourceCastsBefore=J(Get(Ability,"TelemetryJson")),scope="Explicit graphics-only original source assets, including delayed particle layers; not a gameplay cast or acceptance observation. Cleared before baseline."});Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear graphics warmup before actual cast baseline");phase=11;entered=Time.realtimeSinceStartup;break;
                case 11:
                    if(Age<.15f||!(bool)Get(Clock,"CanSimulate"))return;
                    before=J(Get(Vfx,"RendererSnapshotJson"));contract=VfxSourceContract.Load(contractPath,Spell,before.GetRawText());Save("source-contract.json",File.ReadAllText(contractPath));Save("source-bindings.json",Get(Vfx,"BindingAuditJson"));Save("before-cast.json",Snapshot());
                    Save("admission-positions.json",new{player=new[]{player.transform.position.x,player.transform.position.y,player.transform.position.z},enemy=new[]{target.transform.position.x,target.transform.position.y,target.transform.position.z},range=Vector3.Distance(player.transform.position,target.transform.position),targetAt=new[]{targetAt.x,targetAt.y,targetAt.z},teleporting=target.IsTeleporting(),alive=!target.IsDeadOrDyingNextFrame()});
                    baselineWall=Time.realtimeSinceStartup;Capture(false);Require((bool)Call(Tome,"CastSpellForProbe",Spell),"real native-source Consecration cast accepted");
                    castClock=ClockTime;castWall=Time.realtimeSinceStartup;nextCapture=castWall;firstHp=lastSourceHp=target.hp;previousCapture=castWall;
                    frames.Add(new{frame=0,wallSeconds=baselineWall-castWall,sourceSeconds=(float?)null,kind="measured pre-cast baseline",nativePaused=MyTime.paused});
                    var tome=J(Get(Tome,"SnapshotJson"));plannedDuration=P(tome,"Spells").EnumerateArray().Where(s=>S(s,"Id")==Spell).Select(s=>P(s,"Duration").GetSingle()).First();
                    var area=Area(tome);Require(area.ValueKind!=JsonValueKind.Undefined,"actual Consecration gameplay area created");initialArea=area;center=V(P(area,"Center"));SetCaptureCamera();interval=P(area,"Interval").GetSingle();lastRemaining=P(area,"Remaining").GetSingle();tickEnd=.25f+interval*Mathf.Max(0,lastRemaining-1);
                    Require(plannedDuration>2&&plannedDuration<20&&interval>0&&lastRemaining>=2,"bounded real field duration and periodic tick budget");Save("actual-cast.json",new{plannedDuration,interval,tickEnd,area=initialArea,tome});phase=2;entered=Time.realtimeSinceStartup;break;
                case 2:
                    ObserveAndRecord();
                    if(Elapsed>=2.2f)
                    {if(!(bool)Get(Clock,"CanSimulate")){if(Age>10)throw new TimeoutException("Native pause owner blocked: "+Get(Clock,"PauseReason"));return;}Require(UiManager.Instance?.pause!=null,"native pause available");UiManager.Instance.pause.Pause();phase=3;entered=Time.realtimeSinceStartup;}break;
                case 3:
                    ObserveAndRecord();
                    if(!MyTime.paused||UiManager.Instance.pause.IsPaused()!=true){if(Age>4)throw new TimeoutException("Native pause was not admitted");return;}
                    if(!pauseAdmitted){pauseAdmitted=true;pauseClock=ClockTime;pauseBefore=J(Get(Vfx,"SnapshotJson"));entered=Time.realtimeSinceStartup;Save("pause-admitted.json",Snapshot());return;}
                    if(Age<.6f)return;
                    Check(Mathf.Abs(ClockTime-pauseClock)<.0001f,"actual source clock freezes during native pause");var paused=J(Get(Vfx,"SnapshotJson"));Check(P(paused,"EffectAges").GetRawText()==P(pauseBefore,"EffectAges").GetRawText(),"actual source effect ages freeze during pause");Save("paused.json",Snapshot());UiManager.Instance.pause.Resume();phase=4;entered=Time.realtimeSinceStartup;break;
                case 4:
                    ObserveAndRecord();if(MyTime.paused||UiManager.Instance.pause.IsPaused()){if(Age>4)throw new TimeoutException("Native resume not admitted");return;}resumed=true;phase=5;entered=Time.realtimeSinceStartup;Save("resumed.json",Snapshot());break;
                case 5:
                    ObserveAndRecord();if(Elapsed<plannedDuration+3.6f)return;
                    primaryCompleted=true;primaryCaptureEnded=Time.realtimeSinceStartup;var finalVfx=J(Get(Vfx,"RendererSnapshotJson"));var report=contract.Evaluate(finalVfx,false);Save("critical-source-contract.json",report);Save("critical-source-contract-first-cycle.json",report);foreach(var row in report.Checks)Check(row.Pass,"First cycle: "+row.Label,JsonSerializer.Serialize(row.Evidence));
                    Check(Counter(finalVfx,"Requested",Spell+"/cast")-Counter(before,"Requested",Spell+"/cast")==1&&Counter(finalVfx,"SpawnedStages",Spell+"/cast")-Counter(before,"SpawnedStages",Spell+"/cast")==2,"exactly one initial cast dispatch admits both unique source cast assets","Source slots6/7 currently share one deduplicated hand asset; three-attachment parity is independently unverified.");
                    var evictions=P(finalVfx,"Evictions").EnumerateObject().Where(p=>p.Name.Contains("<-"+Spell+"/",StringComparison.Ordinal)&&p.Value.GetInt32()>Counter(before,"Evictions",p.Name)).Select(p=>p.Name).ToArray();Check(evictions.Length==0,"no critical Consecration role was budget-evicted",string.Join(",",evictions));
                    Check(fieldScaleObserved&&fieldScaleCorrect,"native source field scale equals actual effective radius / source radius8");
                    Check(orientationFlagsSeen&&!orientationFlagsWrong&&earlyOrientationSamples>0&&peakEarlyParticles>0,"actual source flags32777 produce a nonempty initial particle burst","earlyPeak="+peakEarlyParticles);
                    Check(earlyOrientationSamples>0&&maxParticleCenterDrift<.2f,"original source sphere vertical0 produces horizontal particle centers, no upward column","maxCenterYDrift="+maxParticleCenterDrift+"; sourceRVA1418D2412..2495");
                    Check(lateOrientationSamples>0&&lateParticlePeak==0,"source0x8000 once-burst particles expire; no continuous respawn at1.5..2s","latePeak="+lateParticlePeak+"; sourceRVA1415CACC0..CACED/latch436");Save("source-particle-orientation.json",new{sourceFlags=32777,sourceSphereEvidence="RVA1418D2412..2495; vertical0 horizontal ring",sourceBurstEvidence="RVA1415CACC0..CACED;0x8000 once latch436",peakEarlyParticles,lateParticlePeak,maxParticleCenterDrift,earlyOrientationSamples,lateOrientationSamples,rows=orientationRows,note="Particle centers are independently read from actual native renderer diagnostics, relative to actual last emitter worldY. No mesh elevation, emitter replacement or claim of exact200 count before rate/factor proof."});
                    Check(fullParticleMetricsSeen&&!particleMetricsMissing&&VfxSourceContract.ConsecrationOnceBurst(peakEarlyParticles,maximumTotalSpawned,maxEmissionBursts,maximumClipped,lateParticlePeak),"source positive67ms gate emits exactly200 once, capacity admits all, none later","peak="+peakEarlyParticles+",spawned="+maximumTotalSpawned+",bursts="+maxEmissionBursts+",clipped="+maximumClipped);
                    Check(nativeParticleMeshSamples>0&&VfxSourceContract.ConsecrationHorizontal(maxNativeMeshCenterDrift),"independent actual billboard mesh centers confirm horizontal ring","samples="+nativeParticleMeshSamples+",maxMeshCenterYDrift="+maxNativeMeshCenterDrift);
                    Save("source-particle-complete-centers.json",new{maximumTotalSpawned,maxEmissionBursts,maximumClipped,fullParticleMetricsSeen,particleMetricsMissing,nativeParticleMeshSamples,maxNativeMeshCenterDrift,sourceFacts="flags32777; rate step0→200@67ms; shape2 vertical0 horizontalring; length=width.833333; lifetime1.2; source native positivegate/latch436",rows=orientationRows});
                    Check(hpTicks.Count>=2&&target.hp<firstHp,"actual native target receives multiple periodic HP losses",hpTicks.Count.ToString());
                    Check(lastHpChange<=tickEnd+.3f,"native periodic damage stops at actual gameplay tick budget","last="+lastHpChange+",lastDue="+tickEnd);
                    Check(Area(J(Get(Tome,"SnapshotJson"))).ValueKind==JsonValueKind.Undefined,"native-source gameplay area expires");
                    Check(lastUnstoppedAreaAge>=plannedDuration-.2f,"source field lifetime remains admitted through actual spell duration","lastUnstoppedAge="+lastUnstoppedAreaAge+",duration="+plannedDuration+"; original mesh opacity may correctly fade after8.5s");
                    var remaining=P(J(Get(Vfx,"RendererSnapshotJson")),"Effects").EnumerateArray().Where(e=>S(e,"Id")==Spell&&S(e,"Stage")=="area").ToArray();Check(remaining.Length==0,"persistent source field and its tail expire normally");Check(resumed&&ClockTime>pauseClock,"native resume restores field progression");
                    Save("expired.json",Snapshot());
                    Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear first-cycle residue before separate role proof");targetAt=Floor(player.transform.position+Forward()*1.8f);target.transform.position=targetAt;
                    var roleBefore=J(Get(Vfx,"RendererSnapshotJson"));roleContract=VfxSourceContract.Load(contractPath,Spell,roleBefore.GetRawText());
                    Require((bool)Call(Tome,"CastSpellForProbe",Spell),"separate actual Consecration cast for role-isolated proof");roleClock=ClockTime;roleIndex=0;var secondArea=Area(J(Get(Tome,"SnapshotJson")));if(secondArea.ValueKind!=JsonValueKind.Undefined){center=V(P(secondArea,"Center"));SetCaptureCamera();}phase=6;entered=Time.realtimeSinceStartup;Save("secondary-role-cast.json",new{primaryComplete=true,before=roleBefore,after=Snapshot(),note="Separate actual cast, outside the full-cycle recording and first-cast mechanical counters."});break;
                case 6:
                    float roleElapsed=ClockTime-roleClock;roleContract.Observe(J(Get(Vfx,"RendererSnapshotJson")),roleElapsed);
                    if(roleIndex<RoleSchedule.Length&&roleElapsed>=RoleSchedule[roleIndex].Seconds)
                    {
                        string stage=RoleSchedule[roleIndex].Stage;float measured=roleElapsed;var pixels=Capture(true,false,stage,"roleproof-"+roleIndex.ToString("D2"));roleContract.ObservePixels(stage,measured,pixels.TryGetValue(stage,out long count)?count:0);roleProof.Add(new{sample=roleIndex,stage,sourceSeconds=measured,pixels,snapshot=J(Get(Vfx,"RendererSnapshotJson"))});roleIndex++;
                    }
                    if(roleElapsed<7.8f)return;var secondReport=roleContract.Evaluate(J(Get(Vfx,"RendererSnapshotJson")));Save("critical-source-contract-secondary-roles.json",secondReport);foreach(var row in secondReport.Checks)Check(row.Pass,"Secondary role proof: "+row.Label,JsonSerializer.Serialize(row.Evidence));Save("secondary-role-proof.json",roleProof);Call(Ability,"ClearEffectsForProbe");Finish(null);break;
            }
        }
        catch(Exception error){Finish(error is TargetInvocationException t?t.InnerException??error:error);}
    }
    float Age=>Time.realtimeSinceStartup-entered;
    float ClockTime=>(float)Get(Clock,"Time");
    float Elapsed=>ClockTime-castClock;
    [HideFromIl2Cpp] void ObserveAndRecord()
    {
        var detailed=J(Get(Vfx,"RendererSnapshotJson"));contract.Observe(detailed,Elapsed);
        ObserveSourceParticleOrientation(detailed);
        if(!terrainEvidenceWritten&&Elapsed>=.5f)CaptureFieldTerrain();
        foreach(var fx in P(detailed,"Effects").EnumerateArray())if(S(fx,"Id")==Spell&&S(fx,"Stage")=="area"&&!P(fx,"Stopped").GetBoolean())lastUnstoppedAreaAge=Mathf.Max(lastUnstoppedAreaAge,P(fx,"Age").GetSingle());
        if(!fieldScaleObserved&&Elapsed>.3f){float expected=P(initialArea,"Radius").GetSingle()/8;foreach(var t in Object.FindObjectsOfType<Transform>())if(t!=null&&t.name=="AzerothVfx_"+Spell+"_area"){fieldScaleObserved=true;fieldScaleCorrect&=Mathf.Abs(t.localScale.x-expected)<.001f&&Mathf.Abs(t.localScale.y-expected)<.001f&&Mathf.Abs(t.localScale.z-expected)<.001f;Save("native-field-scale.json",new{expected,actual=new[]{t.localScale.x,t.localScale.y,t.localScale.z},radius=P(initialArea,"Radius").GetSingle()});}}
        if(target==null||target.id!=targetId||target.IsDeadOrDyingNextFrame())throw new InvalidOperationException("Durable native target unexpectedly died/reused");
        if(target.hp<lastSourceHp){lastHpChange=Elapsed;hpTicks.Add(new{elapsed=Elapsed,hpBefore=lastSourceHp,hpAfter=target.hp,telemetry=J(Get(Ability,"TelemetryJson")),area=Area(J(Get(Tome,"SnapshotJson"))).ValueKind==JsonValueKind.Undefined?(object)null:Area(J(Get(Tome,"SnapshotJson")))});lastSourceHp=target.hp;}
        if(Time.realtimeSinceStartup<nextCapture)return;if(frame>=MaximumFrames)throw new InvalidOperationException("Full-cycle recording reached bounded frame limit");
        float gap=Time.realtimeSinceStartup-previousCapture;largestCaptureGap=Mathf.Max(largestCaptureGap,gap);previousCapture=Time.realtimeSinceStartup;nextCapture+=1f/Fps;
        float captureWall=Time.realtimeSinceStartup,sourceSeconds=Elapsed;Capture(false);lastCaptureWall=captureWall;
        frames.Add(new{frame=frame-1,wallSeconds=captureWall-castWall,sourceSeconds,nativePaused=MyTime.paused,gapSeconds=gap,targetHp=target.hp,captureCostSeconds=Time.realtimeSinceStartup-captureWall});
        if(frame%15==0||Elapsed<1.2f&&frame%3==0)Save("renderer-"+(frame-1).ToString("D4")+".json",new{elapsed=Elapsed,phase,detailed,telemetry=J(Get(Ability,"TelemetryJson"))});
    }
    [HideFromIl2Cpp] Dictionary<string,long> Capture(bool roles,bool recordFrame=true,string onlyStage=null,string label=null)
    {
        var result=new Dictionary<string,long>();var camera=Camera.main;if(camera==null)throw new InvalidOperationException("Native world camera missing");
        var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;float oldFov=camera.fieldOfView;
        var canvases=Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.enabled&&c.renderMode!=RenderMode.WorldSpace).ToArray();
        try
        {
            foreach(var c in canvases)c.enabled=false;camera.transform.position=cameraAt;camera.transform.rotation=cameraLook;camera.fieldOfView=50;camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();var on=image.GetPixels32();if(recordFrame){WritePpm(Path.Combine("frames",frame.ToString("D4")+".ppm"),on);frame++;}else if(label!=null)WritePpm(label+"-world.ppm",on);
            if(roles)foreach(string stage in onlyStage!=null?new[]{onlyStage}:contract.Stages)
            {
                var renderers=Object.FindObjectsOfType<Renderer>().Where(r=>r!=null&&r.gameObject.activeInHierarchy&&HasStage(r.transform,stage)).ToArray();var flags=renderers.Select(r=>r.forceRenderingOff).ToArray();
                try
                {
                    foreach(var r in renderers)r.forceRenderingOff=true;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,Width,Height),0,0);image.Apply();var off=image.GetPixels32();long changed=0;for(int i=0;i<on.Length;i++)if(Math.Abs(on[i].r-off[i].r)+Math.Abs(on[i].g-off[i].g)+Math.Abs(on[i].b-off[i].b)>12)changed++;
                    result[stage]=changed;WritePpm((label??"role-"+(frame-1).ToString("D4"))+"-"+stage+"-off.ppm",off);
                }
                finally{for(int i=0;i<renderers.Length;i++)try{if(renderers[i]!=null)renderers[i].forceRenderingOff=flags[i];}catch{}}
            }
        }
        finally{foreach(var c in canvases)try{if(c!=null)c.enabled=true;}catch{}camera.targetTexture=oldTarget;camera.transform.position=oldPosition;camera.transform.rotation=oldRotation;camera.fieldOfView=oldFov;RenderTexture.active=oldActive;}
        return result;
    }
    [HideFromIl2Cpp] void WritePpm(string name,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> pixels){int at=0;for(int y=Height-1;y>=0;y--)for(int x=0;x<Width;x++){var p=pixels[y*Width+x];rgb[at++]=p.r;rgb[at++]=p.g;rgb[at++]=p.b;}using var f=File.Create(Path.Combine(folder,name));f.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n"));f.Write(rgb);}
    [HideFromIl2Cpp] void CreateCapture(){rt=new RenderTexture(Width,Height,24);rt.Create();image=new Texture2D(Width,Height,TextureFormat.RGB24,false);rgb=new byte[Width*Height*3];}
    [HideFromIl2Cpp] void ObserveSourceParticleOrientation(JsonElement detailed)
    {
        bool early=Elapsed>=.2f&&Elapsed<=.9f,late=Elapsed>=1.5f&&Elapsed<=2f;if(!early&&!late)return;
        foreach(var fx in P(detailed,"Effects").EnumerateArray())if(S(fx,"Id")==Spell&&S(fx,"Stage")=="area"&&S(fx,"Key")=="d02bc71a978e2379")foreach(var layer in P(fx,"ParticleLayers").EnumerateArray())
        {
            int flags=P(layer,"Flags").GetInt32();orientationFlagsSeen|=flags==32777;orientationFlagsWrong|=flags!=32777;int live=P(layer,"LiveParticles").GetInt32();float emitterY=V(P(layer,"LastEmitterPosition")).y,drift=0;var positions=P(layer,"ParticlePositions").EnumerateArray().Select(V).ToArray();foreach(var pos in positions)drift=Mathf.Max(drift,Mathf.Abs(pos.y-emitterY));
            if(TryP(layer,"TotalSpawned",out var total)&&TryP(layer,"EmissionBursts",out var bursts)&&TryP(layer,"ClippedParticles",out var clipped)&&TryP(layer,"AllParticleCentersMinimum",out var minimum)&&TryP(layer,"AllParticleCentersMaximum",out var maximum))
            {fullParticleMetricsSeen=true;maximumTotalSpawned=Math.Max(maximumTotalSpawned,total.GetInt64());maxEmissionBursts=Math.Max(maxEmissionBursts,bursts.GetInt32());maximumClipped=Math.Max(maximumClipped,clipped.GetInt64());if(live>0)drift=Mathf.Max(Mathf.Abs(V(minimum).y-emitterY),Mathf.Abs(V(maximum).y-emitterY));}
            else particleMetricsMissing=true;
            if(early&&live>0&&Elapsed>=nextNativeParticleSample){nextNativeParticleSample=Elapsed+.2f;foreach(var renderer in Object.FindObjectsOfType<MeshRenderer>())if(renderer!=null&&renderer.name=="OriginalM2Emitter"&&HasStage(renderer.transform,"area")){var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)continue;var vertices=mesh.vertices;for(int k=0;k+3<vertices.Length;k+=4){var actual=renderer.transform.TransformPoint((vertices[k]+vertices[k+1]+vertices[k+2]+vertices[k+3])*.25f);maxNativeMeshCenterDrift=Mathf.Max(maxNativeMeshCenterDrift,Mathf.Abs(actual.y-emitterY));}if(vertices.Length>0)nativeParticleMeshSamples++;}}
            if(early){earlyOrientationSamples++;peakEarlyParticles=Math.Max(peakEarlyParticles,live);maxParticleCenterDrift=Mathf.Max(maxParticleCenterDrift,drift);}if(late){lateOrientationSamples++;lateParticlePeak=Math.Max(lateParticlePeak,live);}if(orientationRows.Count<48)orientationRows.Add(new{elapsed=Elapsed,flags,live,emitterY,maximumCenterYDrift=drift,minimumCenterY=positions.Length>0?(float?)positions.Min(p=>p.y):null,maximumCenterY=positions.Length>0?(float?)positions.Max(p=>p.y):null});
        }
    }
    [HideFromIl2Cpp] Vector3 SelectFlatNativeGround()
    {
        var origin=MyPlayer.Instance.transform.position;int mask=GameManager.Instance.whatIsGround.value;var candidates=new List<object>();
        foreach(float distance in new[]{20f,28f,36f,44f})for(int direction=0;direction<8;direction++)
        {
            float angle=direction*Mathf.PI/4;var candidate=origin+new Vector3(Mathf.Cos(angle)*distance,0,Mathf.Sin(angle)*distance);var points=new List<Vector3>();string reason=null;
            for(int z=0;z<5&&reason==null;z++)for(int x=0;x<5;x++)
            {
                var offset=new Vector2((x-2)/2f,(z-2)/2f);if(offset.sqrMagnitude>1)offset=offset.normalized;var at=candidate+new Vector3(offset.x*12,0,offset.y*12);
                if(!Physics.Raycast(at+Vector3.up*20,Vector3.down,out var hit,60,mask,QueryTriggerInteraction.Ignore)){reason="native floor missing";break;}
                bool spawnPlatform=false;for(var parent=hit.collider.transform;parent!=null;parent=parent.parent)if(parent.name=="SpawnPlatform(Clone)")spawnPlatform=true;
                if(spawnPlatform||hit.collider.GetComponentInParent<ElwynnPropMarker>()!=null){reason="spawn platform or source decor under footprint";break;}points.Add(hit.point);
            }
            if(reason==null&&points.Max(p=>p.y)-points.Min(p=>p.y)>.12f)reason="native ground slope/height span exceeds.12m across12m footprint";
            var centerPoint=points.Count==25?points[12]:candidate;
            if(reason==null&&Physics.CheckBox(centerPoint+Vector3.up*1.6f,new Vector3(12,1.2f,12),Quaternion.identity,mask,QueryTriggerInteraction.Ignore))reason="ground-layer obstacle above native floor";
            candidates.Add(new{candidate=new[]{candidate.x,candidate.y,candidate.z},reason=reason??"selected",samples=points.Count,heightSpan=points.Count>0?points.Max(p=>p.y)-points.Min(p=>p.y):(float?)null});
            if(reason==null){Save("flat-native-fixture.json",new{originalPlayer=new[]{origin.x,origin.y,origin.z},chosen=new[]{centerPoint.x,centerPoint.y,centerPoint.z},mask,radius=12,heightSpan=points.Max(p=>p.y)-points.Min(p=>p.y),candidates,note="Read-only native ray/overlap selection; actual TeleportPlayer moves only the isolated fixture. No terrain replacement or raised effect plane."});return centerPoint;}
        }
        // A procedural forest need not contain an empty perfectly flat24m
        // patch. Use an explicitly labelled neutral QA stage in this owned
        // batch process, rather than weakening the native-ground criterion.
        var top=origin+Vector3.up*35;int layer=0;while(layer<31&&(mask&(1<<layer))==0)layer++;
        if((mask&(1<<layer))==0)throw new InvalidOperationException("No native ground layer for controlled QA stage");
        controlledStage=GameObject.CreatePrimitive(PrimitiveType.Cube);controlledStage.name="AzerothControlledQaFloor";
        controlledStage.layer=layer;controlledStage.transform.position=top-Vector3.up*.25f;controlledStage.transform.localScale=new Vector3(32,.5f,32);
        controlledStageMaterial=new Material(Shader.Find("Particles/Standard Unlit")){name="AzerothControlledQaNeutral",color=new Color(.1f,.1f,.1f,1)};
        controlledStageMaterial.SetFloat("_Mode",0);controlledStageMaterial.SetInt("_SrcBlend",1);controlledStageMaterial.SetInt("_DstBlend",0);controlledStageMaterial.SetInt("_ZWrite",1);controlledStageMaterial.renderQueue=2000;
        controlledStage.GetComponent<MeshRenderer>().sharedMaterial=controlledStageMaterial;Physics.SyncTransforms();
        Save("flat-native-fixture.json",new{selected=true,kind="controlled-neutral-QA-stage",mask,layer,chosen=new[]{top.x,top.y,top.z},nativeCandidates=candidates,
            note="Synthetic32x32 neutral flat floor above the existing map, only in the armed isolated batch probe. Actual native player/enemy, cast, particles, HP and pause are used. Native procedural terrain and source field geometry are not edited. Stage is removed on cleanup; this is not a native forest flatness claim."});
        return top;
    }
    [HideFromIl2Cpp] void CaptureFieldTerrain()
    {
        var parts=new List<object>();var triangles=new List<(Vector3 A,Vector3 B,Vector3 C)>();var used=new List<Vector3>();
        foreach(var renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            if(renderer==null||renderer.name!="OriginalM2Geometry"||!HasStage(renderer.transform,"area"))continue;
            var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)continue;
            var vertices=mesh.vertices.Select(p=>renderer.transform.TransformPoint(p)).ToArray();var indices=mesh.triangles.ToArray();
            for(int i=0;i+2<indices.Length;i+=3){var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];triangles.Add((a,b,c));used.Add(a);used.Add(b);used.Add(c);}
            parts.Add(new{renderer=renderer.GetInstanceID(),enabled=renderer.enabled,worldVertices=vertices.Select(p=>new[]{p.x,p.y,p.z}),indices,localScale=new[]{renderer.transform.localScale.x,renderer.transform.localScale.y,renderer.transform.localScale.z},worldScale=new[]{renderer.transform.lossyScale.x,renderer.transform.lossyScale.y,renderer.transform.lossyScale.z}});
        }
        if(used.Count==0){if(Elapsed>2){terrainEvidenceWritten=true;Save("field-terrain.json",new{available=false,reason="No actual area geometry triangles found; no terrain inference.",elapsed=Elapsed});}return;}
        float minX=used.Min(p=>p.x),maxX=used.Max(p=>p.x),minZ=used.Min(p=>p.z),maxZ=used.Max(p=>p.z),top=used.Max(p=>p.y);int mask=GameManager.Instance.whatIsGround.value;var samples=new List<object>();int covered=0,hitCount=0,buried=0;float maxBurial=0;
        for(int z=0;z<5;z++)for(int x=0;x<5;x++)
        {
            float wx=Mathf.Lerp(minX,maxX,(x+.5f)/5),wz=Mathf.Lerp(minZ,maxZ,(z+.5f)/5);var heights=new List<float>();
            foreach(var t in triangles)if(TriangleHeight(t.A,t.B,t.C,wx,wz,out float y))heights.Add(y);
            if(heights.Count==0){samples.Add(new{x=wx,z=wz,triangleCovered=false});continue;}covered++;
            if(!Physics.Raycast(new Vector3(wx,top+30,wz),Vector3.down,out var hit,100,mask,QueryTriggerInteraction.Ignore)){samples.Add(new{x=wx,z=wz,triangleCovered=true,nativeGroundHit=false,trianglePlaneYs=heights});continue;}hitCount++;
            float burial=hit.point.y-heights.Max();if(burial>0)buried++;maxBurial=Mathf.Max(maxBurial,burial);samples.Add(new{x=wx,z=wz,triangleCovered=true,nativeGroundHit=true,trianglePlaneYs=heights,nativeGroundY=hit.point.y,burial,collider=hit.collider!=null?hit.collider.name:null});
        }
        terrainEvidenceWritten=true;Save("field-terrain.json",new{elapsed=Elapsed,groundMask=mask,requestedNativeMask=4352,parts,boundsXZ=new[]{minX,maxX,minZ,maxZ},grid="5x5 interior world-XZ points; actual transformed mesh triangle planes",covered,nativeHits=hitCount,positiveBurialSamples=buried,maxPositiveBurial=maxBurial,samples,note="Read-only terrain evidence; no pass verdict, height offset, material change or claim that transparent texture corners are opaque ground coverage."});
    }
    [HideFromIl2Cpp] static bool TriangleHeight(Vector3 a,Vector3 b,Vector3 c,float x,float z,out float y)
    {float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<.000001f){y=0;return false;}float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/d,v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/d,w=1-u-v;y=u*a.y+v*b.y+w*c.y;return u>=-.0001f&&v>=-.0001f&&w>=-.0001f;}
    [HideFromIl2Cpp] void SetCaptureCamera(){cameraAt=center+Vector3.up*7-Forward()*6;cameraLook=Quaternion.LookRotation(center+Vector3.up*.75f-cameraAt,Vector3.up);}
    [HideFromIl2Cpp] static bool HasStage(Transform t,string stage){for(;t!=null;t=t.parent)if(t.name=="AzerothVfx_"+Spell+"_"+stage)return true;return false;}
    [HideFromIl2Cpp] void DisplaceOtherEnemies(){if(EnemyManager.Instance?.enemies==null)return;foreach(var pair in EnemyManager.Instance.enemies){var e=pair.Value;if(pair.Key==targetId||e==null)continue;if(!displaced.ContainsKey(pair.Key))displaced[pair.Key]=e.transform.position;e.transform.position=center-Forward()*100+Vector3.right*(pair.Key%7);}}
    [HideFromIl2Cpp] void IsolateInput(){foreach(var m in Resources.FindObjectsOfTypeAll<BaseInputModule>())try{if(m==null||!m.gameObject.scene.IsValid())continue;int id=m.GetInstanceID();if(!modules.Any(x=>x.Id==id))modules.Add((m,m.enabled,id));m.enabled=false;}catch{}}
    [HideFromIl2Cpp] object Snapshot()=>new{phase,sourceTime=Get(Clock,"Time"),pauseReason=Get(Clock,"PauseReason"),nativePaused=MyTime.paused,pauseUi=UiManager.Instance?.pause?.IsPaused(),target=target!=null?new{target.id,target.hp}:null,vfx=J(Get(Vfx,"RendererSnapshotJson")),tome=J(Get(Tome,"SnapshotJson")),telemetry=J(Get(Ability,"TelemetryJson"))};
    [HideFromIl2Cpp] static JsonElement Area(JsonElement tome)=>P(P(tome,"Behaviors"),"Areas").EnumerateArray().FirstOrDefault(a=>S(a,"Spell")==Spell);
    [HideFromIl2Cpp] static Vector3 Floor(Vector3 v){int mask=GameManager.Instance.whatIsGround.value;return mask!=0&&Physics.Raycast(v+Vector3.up*30,Vector3.down,out var hit,80,mask,QueryTriggerInteraction.Ignore)?hit.point:v;}
    [HideFromIl2Cpp] static Vector3 Forward(){var f=MyPlayer.Instance.transform.forward;f.y=0;return f.sqrMagnitude>.001f?f.normalized:Vector3.forward;}
    [HideFromIl2Cpp] Type Named(string name){if(types.TryGetValue(name,out var t))return t;return types[name]=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(a=>a!=null)??throw new MissingMemberException(name);}
    [HideFromIl2Cpp] object Get(string type,string field)=>Named(type).GetProperty(field,BindingFlags.Public|BindingFlags.Static)?.GetValue(null)??throw new MissingMemberException(type,field);
    [HideFromIl2Cpp] object Call(string type,string method,params object[] args)=>Named(type).GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);
    [HideFromIl2Cpp] static JsonElement J(object value)=>VfxSourceContract.Parse(value as string??JsonSerializer.Serialize(value));
    [HideFromIl2Cpp] static JsonElement P(JsonElement e,string name){foreach(var p in e.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase))return p.Value;throw new MissingMemberException(name);}
    [HideFromIl2Cpp] static string S(JsonElement e,string name)=>P(e,name).GetString();
    [HideFromIl2Cpp] static bool TryP(JsonElement e,string name,out JsonElement value){foreach(var p in e.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)){value=p.Value;return true;}value=default;return false;}
    [HideFromIl2Cpp] static int Counter(JsonElement e,string group,string id)=>P(e,group).TryGetProperty(id,out var value)?value.GetInt32():0;
    [HideFromIl2Cpp] static Vector3 V(JsonElement a)=>new(a[0].GetSingle(),a[1].GetSingle(),a[2].GetSingle());
    [HideFromIl2Cpp] static string Arg(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
    [HideFromIl2Cpp] void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),value as string??JsonSerializer.Serialize(value));
    [HideFromIl2Cpp] void Check(bool okay,string label,string detail=""){if(okay)passed++;else failed++;checks.Add(new{status=okay?"PASS":"FAIL",label,detail});Plugin.Note("PALADIN VFX "+(okay?"PASS":"FAIL")+" "+label+" "+detail);}
    [HideFromIl2Cpp] void Require(bool okay,string label){Check(okay,label);if(!okay)throw new InvalidOperationException(label);}
    [HideFromIl2Cpp] void Finish(Exception error)
    {
        if(done)return;done=true;if(error!=null){Check(false,"fixture exception",error.ToString());try{Save("failure-context.json",Snapshot());}catch{}}
        try{Call(Tome,"RestoreOriginalTomesForProbe");if(talentState!=null){var state=J(talentState);Call(Talent,"SetProbeState",P(state,"TalentXp").GetInt64(),P(state,"Ranks").GetRawText());Call(Talent,"ApplyDebugNow");}if(pauseAdmitted&&UiManager.Instance?.pause?.IsPaused()==true)UiManager.Instance.pause.Resume();if(EnemyManager.Instance?.enemies!=null)foreach(var p in displaced)if(EnemyManager.Instance.enemies.ContainsKey(p.Key)&&EnemyManager.Instance.enemies[p.Key]!=null)EnemyManager.Instance.enemies[p.Key].transform.position=p.Value;}catch(Exception cleanup){Check(false,"fixture cleanup",cleanup.ToString());}
        if(fixtureRelocated)try{MyPlayer.Instance?.playerMovement?.TeleportPlayer(originalPlayerPosition);}catch{}foreach(var saved in modules)try{if(saved.Module!=null)saved.Module.enabled=saved.Enabled;}catch{}
        if(rt!=null){rt.Release();Object.Destroy(rt);}if(image!=null)Object.Destroy(image);if(controlledStage!=null)Object.Destroy(controlledStage);if(controlledStageMaterial!=null)Object.Destroy(controlledStageMaterial);
        bool mechanicsPass=failed==0;float average=(frame-1)/Mathf.Max(.001f,primaryCaptureEnded-castWall);bool capturePass=primaryCompleted&&plannedDuration>0&&frame>=(plannedDuration+3.6f)*Fps*.8f&&largestCaptureGap<=.35f&&average>=Fps*.8f;Check(capturePass,"complete full-cycle native recording with bounded cadence gaps","actualFPS="+average+",requested="+Fps+",maxGap="+largestCaptureGap);
        Save("recording.json",new{requestedFps=Fps,width=Width,height=Height,frames,actualFrameCount=frame,baselineFrame=0,largestCaptureGap,actualAverageFps=average,capturePass,recordingEndedWallSeconds=primaryCaptureEnded-castWall,primaryCompleted,secondaryRoleCastOutsideTimeline=true,timestampMeaning="capture-start realtime; source clock sampled in same native frame",note="Real native renders and measured wall/source clocks. Main timeline performs one render/readback per frame. Role pairs belong to a separate actual cast. Use measured timestamps/VFR, not invented fixed15FPS duration."});Save("native-hp-ticks.json",hpTicks);
        Save("summary.json",new{pass=failed==0,mechanicalContract=mechanicsPass?"PASS":"FAIL",captureContract=capturePass?"PASS":"FAIL",visualReference="UNVERIFIED",originalReferenceClipAvailable=false,passed,failed,phase,plannedDuration,interval,tickEnd,frame,error=error?.ToString(),checks,coverage="Dedicated source Consecration cast/area/impact contract, complete native camera timeline, real native target HP, pause/resume/expiry. Source-engine visual appearance is independently unverified without original A/B recording. Missing critical visual roles fail after full recording, not fail-fast."});Application.Quit(failed==0?0:2);
    }
}

[HarmonyPatch]
static class PaladinVfxButtonIsolation
{static bool Prepare()=>PaladinVfxContractProbe.Armed;static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));static bool Prefix(ref bool __result){__result=false;return false;}}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class PaladinVfxAxisIsolation
{static bool Prepare()=>PaladinVfxContractProbe.Armed;static bool Prefix(ref float __result){__result=0;return false;}}
