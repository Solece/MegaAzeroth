using System.Reflection;
using System.Text.Json;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using Assets.Scripts.Utility;
using Assets.Scripts.Inventory__Items__Pickups.Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Stats;
using UnityEngine.EventSystems;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Camera evidence from real casts in an explicitly isolated, temporary batch run.
// Pixel differences are an observation; they do not establish source-engine fidelity.
public sealed class VfxFidelityProbe:MonoBehaviour
{
    public VfxFidelityProbe(IntPtr pointer):base(pointer){}
    const string Ability="AzerothAbilities.ClassAbilityDiagnostics",Tome="AzerothAbilities.TomeCompatibilityDiagnostics",Vfx="AzerothAbilities.SpellVfxDiagnostics",Clock="AzerothAbilities.CombatSimulation";
    readonly List<(BaseInputModule Module,bool Enabled,string Name,int Id)> inputModules=new();
    readonly Dictionary<string,Type> types=new();readonly List<object> checks=new(),observations=new();readonly List<JsonElement> spells=new();
    static readonly float[] SampleTimes={.12f,.4f,1f},RainSampleTimes={.12f,.4f,.8f,1f,1.3f};
    string folder,classId,spellId;float began,entered,castTime,pauseTime;int phase,index,sample,passed,failed,skipped,maxVertices,pointerDestroyedSkipped;long changedPixels;bool done,accepted,pauseExercised,pauseAdmitted,conditionalHpFixture,budgetFixture;Enemy fixture;
    JsonElement before,pauseBefore,deathBefore,deathFirst,casterBefore;float hpBefore;uint deathId;int deathSample,casterHpBefore,casterMaxBefore;bool corpsePoolProof;float shieldBegan,shieldDuration,shieldPauseClock;bool shieldAdmitted;float[] shieldPausedTimers;
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-vfx-fidelity-smoke");
    void Start()
    {
        if(!Armed){enabled=false;return;}began=Time.realtimeSinceStartup;
        folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports","vfx-fidelity-"+Environment.ProcessId));Directory.CreateDirectory(folder);
        try
        {
            Require(Environment.GetCommandLineArgs().Contains("-azeroth-test-battle"),"explicit save-guarded test battle");
            classId=Arg("-azeroth-hero=");Require(!string.IsNullOrEmpty(classId),"explicit source class");
            Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;entered=began;
        }
        catch(Exception error){Finish(error);}
    }
    void Update()
    {
        if(done||!Armed)return;
        try
        {
            if(Time.realtimeSinceStartup-began>120)throw new TimeoutException("VFX fixture exceeded120s");
            IsolatePointerModules();var player=MyPlayer.Instance;
            if(phase<20&&player?.inventory?.playerHealth!=null){if(!conditionalHpFixture)player.inventory.playerHealth.hp=player.inventory.playerHealth.maxHp;player.inventory.playerHealth.damageCooldown=10;}
            if((phase<4||phase==6||phase>=7&&phase<=10||phase==20||phase==21||phase>=24)&&LevelupScreen.isLevelingUp)
            {
                var choice=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(w=>w.gameObject.activeInHierarchy);
                if(choice!=null){Save("incidental-levelup-"+Time.frameCount+".json",new{level=player?.inventory?.playerXp?.level,xp=player?.inventory?.playerXp?.GetXpInt()});choice.CloseLevelupScreen();Record("SKIP","incidental native XP choice","Closed real window without selecting an offer.");}return;
            }
            switch(phase)
            {
                case 0:
                    if(!ElwynnBridge.Ready||player?.inventory==null||!player.hasStarted||MapGenerationController.isGenerating||GameManager.Instance?.IsPlaying()!=true||GameManager.Instance.isGameOver)return;
                    Require((string)Get(Ability,"ActiveClassId")==classId,"actual active source class");
                    Require((bool)Call(Tome,"SetAutomaticCastingForProbe",false),"disable automatic casting");Require((bool)Call(Ability,"LearnAllForProbe"),"learn current class through guarded API");
                    if(Environment.GetCommandLineArgs().Contains("-azeroth-vfx-shield-only")){phase=20;entered=Time.realtimeSinceStartup;break;}
                    var catalog=Json(Get(Ability,"ActiveSourceCatalogJson"));string only=Arg("-azeroth-vfx-spell=");
                    foreach(var spell in Prop(catalog,"Spells").EnumerateArray())if(only==null||Text(spell,"Id")==only)spells.Add(spell.Clone());
                    spells.Sort((a,b)=>(Text(a,"Id")=="rain_of_fire"?0:1).CompareTo(Text(b,"Id")=="rain_of_fire"?0:1));
                    Require(spells.Count>0,"requested source spells exist",only??"all current class");Save("catalog.json",catalog);Save("binding-audit.json",Optional(Vfx,"BindingAuditJson"));
                    fixture=EnemyManager.Instance.SpawnBoss((EEnemy)0,-1,EEnemyFlag.Boss,player.transform.position+Forward(player)*2.5f,1);Require(fixture!=null,"native Skeleton fixture");fixture.maxHp=10000000;fixture.hp=fixture.maxHp;
                    entered=Time.realtimeSinceStartup;phase=1;break;
                case 1:
                    if(fixture.IsTeleporting()){if(Age>12)throw new TimeoutException("Native target spawn");return;}phase=2;entered=Time.realtimeSinceStartup;break;
                case 2:
                    if(index>=spells.Count)
                    {if(Environment.GetCommandLineArgs().Contains("-azeroth-vfx-death-smoke")){entered=Time.realtimeSinceStartup;phase=7;break;}Save("death-not-run.json",new{status="NOT_RUN",reason="Optional -azeroth-vfx-death-smoke not supplied."});AfterSourceChecks();return;}
                    if(!(bool)Get(Clock,"CanSimulate")){if(Age>8)throw new TimeoutException("Cast clock blocked: "+Get(Clock,"PauseReason"));return;}
                    spellId=Text(spells[index],"Id");Require(fixture!=null&&!fixture.IsDeadOrDyingNextFrame(),"durable native target remains alive");fixture.transform.position=player.transform.position+Forward(player)*2.5f;
                    Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear previous source effects");before=Json(Get(Ability,"TelemetryJson"));hpBefore=fixture.hp;sample=0;maxVertices=0;changedPixels=0;
                    int status=Prop(spells[index],"StatusKind").GetInt32();conditionalHpFixture=status is 6 or 11;
                    if(conditionalHpFixture)player.inventory.playerHealth.hp=Math.Max(1,(int)(player.inventory.playerHealth.maxHp*(status==6?.60f:.25f)));
                    casterHpBefore=player.inventory.playerHealth.hp;casterMaxBefore=player.inventory.playerHealth.maxHp;casterBefore=Json(Get(Ability,"SnapshotJson"));
                    budgetFixture=spellId=="rain_of_fire"&&Environment.GetCommandLineArgs().Contains("-azeroth-vfx-budget-smoke");
                    if(budgetFixture){var load=Json(Call("AzerothAbilities.VfxBudgetDiagnostics","FillTransientBudgetForProbe"));Save("budget-before-rain.json",load);Record(Prop(load,"ReachedTransientBudget").GetBoolean()?"PASS":"FAIL","real source transient budget filled",load.GetRawText());}
                    Save(spellId+"-baseline.json",Snapshot());Capture(spellId+"-baseline",false);accepted=(bool)Call(Tome,"CastSpellForProbe",spellId);
                    if(budgetFixture)for(int stress=1;stress<5;stress++)Record((bool)Call(Tome,"CastSpellForProbe",spellId)?"PASS":"FAIL","actual budget Rain cast "+stress);
                    castTime=(float)Get(Clock,"Time");phase=3;break;
                case 3:
                    if((float)Get(Clock,"Time")-castTime<Schedule()[sample])return;
                    var frame=RendererSnapshot();maxVertices=Math.Max(maxVertices,Vertices(frame));Save(spellId+"-frame-"+sample+".json",Snapshot(frame));
                    changedPixels=Math.Max(changedPixels,Capture(spellId+"-frame-"+sample,sample==1||spellId=="rain_of_fire"&&sample>=2));
                    if(spellId=="rain_of_fire"&&sample>=2)Capture(spellId+"-frame-"+sample+"-ground-inspection",true,true);
                    sample++;
                    if(sample==2&&!pauseExercised)
                    {
                        pauseExercised=true;entered=Time.realtimeSinceStartup;phase=6;break;
                    }
                    if(sample==Schedule().Length){CompleteSpell();index++;entered=Time.realtimeSinceStartup;phase=2;}break;
                case 6:
                    Save(spellId+"-pause-entry.json",PauseObservation());
                    if(!(bool)Get(Clock,"CanSimulate")||LevelupScreen.isLevelingUp)
                    {if(Age>8)throw new TimeoutException("Native pause admission blocked: "+Get(Clock,"PauseReason"));return;}
                    Require(UiManager.Instance?.pause!=null,"native pause window exists");
                    UiManager.Instance.pause.Pause();pauseAdmitted=false;entered=Time.realtimeSinceStartup;phase=4;break;
                case 4:
                    Save(spellId+"-pause-admission.json",PauseObservation());
                    if(!MyTime.paused||UiManager.Instance.pause.IsPaused()!=true)
                    {if(Age>4)throw new TimeoutException(pauseAdmitted?"Native pause closed after admission; see pause-admission.json":"Native pause was not admitted; see pause-admission.json");return;}
                    if(!pauseAdmitted)
                    {pauseAdmitted=true;Require(true,"native pause admitted");pauseBefore=Json(Get(Vfx,"SnapshotJson"));pauseTime=(float)Get(Clock,"Time");entered=Time.realtimeSinceStartup;return;}
                    if(Age<.55f)return;
                    var paused=Json(Get(Vfx,"SnapshotJson"));Save(spellId+"-paused.json",Snapshot());Capture(spellId+"-paused",false);
                    Require(Mathf.Abs((float)Get(Clock,"Time")-pauseTime)<.0001f,"source clock frozen during pause");
                    if(Prop(pauseBefore,"EffectAges").GetArrayLength()>0)Require(Prop(pauseBefore,"EffectAges").GetRawText()==Prop(paused,"EffectAges").GetRawText(),"live VFX ages freeze");else Record("SKIP","live VFX age freeze","No effect alive at pause admission.");
                    UiManager.Instance.pause.Resume();entered=Time.realtimeSinceStartup;phase=5;break;
                case 7:
                    if(!(bool)Get(Clock,"CanSimulate")){if(Age>8)throw new TimeoutException("Death fixture clock blocked");return;}
                    Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear source effects before native death");
                    Require(fixture!=null&&!fixture.IsDeadOrDyingNextFrame(),"live native death fixture");fixture.transform.position=player.transform.position+Forward(player)*2.5f;
                    fixture.transform.position=player.transform.position+Forward(player)*80;
                    var ordinary=Resources.FindObjectsOfTypeAll<EnemyData>().FirstOrDefault(d=>d!=null&&d.enemyName==(EEnemy)0);Require(ordinary!=null,"native ordinary Skeleton data");
                    fixture=EnemyManager.Instance.SpawnEnemy(ordinary,player.transform.position+Forward(player)*10,0,true,EEnemyFlag.None,false,1);Require(fixture!=null,"ordinary death fixture10m from pickup collector");fixture.maxHp=10000000;fixture.hp=fixture.maxHp;
                    conditionalHpFixture=false;entered=Time.realtimeSinceStartup;phase=10;break;
                case 10:
                    if(fixture.IsTeleporting()||Age<.4f){if(Age>12)throw new TimeoutException("Ordinary source death fixture spawn");return;}
                    Require(!fixture.IsBoss(),"death fixture avoids native boss-reward side effects");deathBefore=Death();Save("death-before.json",deathBefore);deathId=fixture.id;deathSample=0;corpsePoolProof=false;spellId="__corpse__";
                    fixture.Kill("VfxFidelityProbe:NativeKill");Save("death-native-kill.json",new{enemyId=deathId,diagnostics=Death(),nativeReleased=NativeReleased(),note="Actual ordinary native Enemy.Kill at10m; no direct corpse creation or fake damage callback."});entered=Time.realtimeSinceStartup;phase=8;break;
                case 8:
                    var death=Death();if(FindCorpse(death,out var timingRow)&&Prop(timingRow,"Age").GetSingle()<(deathSample==0?.08f:deathSample==1?.4f:.8f)){if(Age>8){SaveDeathBlocker();throw new TimeoutException("Source corpse clock blocked by native owner; see death-clock-blocked.json");}return;}Save("death-frame-"+deathSample+".json",new{diagnostics=death,nativeReleased=NativeReleased(),renderers=RendererSnapshot()});
                    Require(FindCorpse(death,out var row),"matching source corpse remains during lifetime");
                    Require(Prop(death,"TotalCreated").GetInt32()>Prop(deathBefore,"TotalCreated").GetInt32(),"native Kill created source corpse");
                    Require(Prop(row,"SourceDeathAnimationId").GetInt32()==Prop(death,"SourceDeathAnimationId").GetInt32()&&Prop(row,"FrameCount").GetInt32()>1,"original source Death animation selected");
                    Require(Prop(row,"Visible").GetBoolean()&&Prop(row,"AssetsValid").GetBoolean(),"source corpse mesh/material visible and valid");
                    Require(!Prop(row,"HasCollider").GetBoolean()&&!Prop(row,"HasRigidbody").GetBoolean()&&!Prop(row,"NativeActorChild").GetBoolean(),"detached render-only corpse");
                    if(deathSample==0)deathFirst=row.Clone();else Require(Prop(row,"Frame").GetInt32()>Prop(deathFirst,"Frame").GetInt32()||Prop(row,"NormalizedTime").GetSingle()>=.99f,"original corpse animation advances");
                    if(NativeReleased())corpsePoolProof=true;Capture("death-frame-"+deathSample,false);Capture("death-frame-"+deathSample+"-ground",false,true);
                    deathSample++;if(deathSample==3){entered=Time.realtimeSinceStartup;phase=9;}break;
                case 9:
                    var end=Death();Save("death-after-lifetime.json",new{diagnostics=end,nativeReleased=NativeReleased()});
                    if(FindCorpse(end,out var stillAlive)){if(Age>4){SaveDeathBlocker();throw new TimeoutException("Source corpse lifetime observation blocked; see death-clock-blocked.json");}return;}
                    Require(Prop(end,"Completed").GetInt32()>Prop(deathBefore,"Completed").GetInt32(),"source corpse removed by completed lifetime");
                    Require(Prop(end,"Failed").GetInt32()==Prop(deathBefore,"Failed").GetInt32(),"death presentation has no new asset failures");
                    Record(corpsePoolProof?"PASS":"SKIP","corpse survives actual native pool release",corpsePoolProof?"Original native actor inactive/reused while detached source corpse was visible.":"Native pool release not observed inside sampled source lifetime; no survival claim.");
                    Capture("death-after-lifetime",false);AfterSourceChecks();break;
                case 20:
                    if(!(bool)Get(Clock,"CanSimulate")||LevelupScreen.isLevelingUp){if(Age>8)throw new TimeoutException("Native shield pickup admission clock");return;}
                    Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear source effects before native shield fixture");player.inventory.playerHealth.damageCooldown=0;
                    Save("native-shield-before.json",ShieldSnapshot());Require(!ShieldStatus()&&player.CanTakeDamage(),"native shield absent and teleport admission clear before pickup");
                    shieldDuration=PowerupConstants.GetShieldTime();Require(shieldDuration>1.3f&&shieldDuration<80,"bounded native shield duration",shieldDuration.ToString());
                    var pickup=PickupManager.Instance.SpawnPickup(EPickup.Shield,player.transform.position,1,false,0);Require(pickup!=null,"actual native Shield pickup spawned");pickup.ApplyPickup();shieldBegan=(float)Get(Clock,"Time");spellId="__native_shield__";entered=Time.realtimeSinceStartup;phase=21;break;
                case 21:
                    if((float)Get(Clock,"Time")-shieldBegan<1.3f)return;
                    Save("native-shield-active.json",ShieldSnapshot());Require(ShieldStatus(),"actual native pickup activates Shield status");
                    Record(ShieldVisible()?"PASS":"FAIL","native shield remains visible after HideTree scan","enabled/active/not-force-off renderer observed after1.3s; native activation unchanged.");
                    Capture("native-shield-active",true);Record("SKIP","natural collision HP protection","No native collision/damage event is fabricated; actual Shield status and raw isInvincible flag recorded; CanTakeDamage is only the native teleport gate.");
                    if(!(bool)Get(Clock,"CanSimulate"))return;UiManager.Instance.pause.Pause();shieldAdmitted=false;entered=Time.realtimeSinceStartup;phase=22;break;
                case 22:
                    if(!MyTime.paused||UiManager.Instance.pause.IsPaused()!=true){if(Age>4)throw new TimeoutException("Native shield pause admission");return;}
                    if(!shieldAdmitted){shieldAdmitted=true;shieldPausedTimers=ShieldTimers();shieldPauseClock=(float)Get(Clock,"Time");entered=Time.realtimeSinceStartup;return;}
                    if(Age<.55f)return;
                    var timers=ShieldTimers();Save("native-shield-paused.json",ShieldSnapshot());Require(timers.Length>0&&timers.Length==shieldPausedTimers.Length&&timers.Select((v,i)=>Mathf.Abs(v-shieldPausedTimers[i])<.01f).All(v=>v),"native shield lifetime freezes during pause");
                    Require(Mathf.Abs((float)Get(Clock,"Time")-shieldPauseClock)<.0001f&&ShieldStatus(),"native shield status persists during pause");UiManager.Instance.pause.Resume();entered=Time.realtimeSinceStartup;phase=23;break;
                case 23:
                    if(MyTime.paused||UiManager.Instance.pause.IsPaused()){if(Age>4)throw new TimeoutException("Native shield resume");return;}phase=24;entered=Time.realtimeSinceStartup;break;
                case 24:
                    Save("native-shield-expiry-observation.json",ShieldSnapshot());
                    if(ShieldStatus()){if((float)Get(Clock,"Time")-shieldBegan>shieldDuration+5)throw new TimeoutException("Native shield did not expire normally");return;}
                    Require(true,"native Shield status expires normally");Save("native-shield-expired.json",ShieldSnapshot());Capture("native-shield-expired",false);Finish(null);break;
                case 5:
                    if(MyTime.paused||UiManager.Instance.pause.IsPaused()){if(Age>4)throw new TimeoutException("Native resume");return;}
                    if((float)Get(Clock,"Time")<=pauseTime)return;Require(true,"source clock resumes");Save(spellId+"-resumed.json",Snapshot());phase=3;break;
            }
        }
        catch(Exception error){Finish(error is TargetInvocationException invocation?invocation.InnerException??error:error);}
    }
    [HideFromIl2Cpp] float[] Schedule()=>spellId=="rain_of_fire"?RainSampleTimes:SampleTimes;
    float Age=>Time.realtimeSinceStartup-entered;
    [HideFromIl2Cpp] void CompleteSpell()
    {
        var after=Json(Get(Ability,"TelemetryJson"));double casts=Counter(after,"CastsBySpell",spellId)-Counter(before,"CastsBySpell",spellId);
        bool passive=Text(spells[index],"Kind")=="passive";string conditionalReason=RejectedCondition();
        Record(passive||!accepted&&conditionalReason!=null?"SKIP":accepted&&casts>0?"PASS":"FAIL","real cast "+spellId,"accepted="+accepted+",castDelta="+casts+",knownCondition="+(conditionalReason??"none; unaccepted remains failure"));
        // An invisible Rain of Fire is the reported regression, not an optional cosmetic pass.
        if(spellId=="rain_of_fire")Record(maxVertices>0&&changedPixels>0?"PASS":"FAIL","Rain source renderer has nonzero pixels (weak evidence only)","maxVertices="+maxVertices+",sameFrameChangedPixels="+changedPixels);
        if(spellId=="rain_of_fire")Record("SKIP","Rain meteor appearance requires visual confirmation","Nonzero pixels/vertices do not establish visible falling meteors or source fidelity. Inspect normal/ground images and bone positions.");
        else Record(changedPixels>0?"PASS":"SKIP","observed VFX pixels "+spellId,changedPixels>0?changedPixels+" same-clock changed pixels":"No pixel contribution observed in bounded sample window; no visibility claim.");
        if(budgetFixture)
        {var budget=Json(Get(Vfx,"RendererSnapshotJson"));Save("budget-after-rain.json",budget);int peak=Prop(budget,"PeakActive").GetInt32(),capacity=Prop(budget,"EffectCapacity").GetInt32();int evictions=Prop(budget,"Evictions").EnumerateObject().Sum(p=>p.Value.GetInt32());Record(peak<=capacity&&evictions>0?"PASS":"FAIL","reserved area budget accepts Rain by transient eviction","peak="+peak+",capacity="+capacity+",evictions="+evictions);Save("budget-cleared.json",Call("AzerothAbilities.VfxBudgetDiagnostics","ClearTransientBudgetForProbe"));}
        observations.Add(new{id=spellId,accepted,casts,status=passive||!accepted&&conditionalReason!=null?"SKIP":accepted&&casts>0?"CAST":"FAILED",conditionalReason,casterHpBefore,casterMaxBefore,casterBefore,budgetFixture,maxVertices,changedPixels,nativeFixtureHpLoss=Mathf.Max(0,hpBefore-fixture.hp),samples=Schedule(),meteorAppearanceVerified=false,attribution="HP may include pets/DoTs/procs. Pixel pair toggles only matching spell renderers; history-dependent camera passes may add residual differences."});Save(spellId+"-complete.json",Snapshot());conditionalHpFixture=false;
    }
    [HideFromIl2Cpp] Renderer[] Renderers()=>Object.FindObjectsOfType<Renderer>().Where(r=>r!=null&&r.gameObject.activeInHierarchy&&Owned(r.transform)).ToArray();
    [HideFromIl2Cpp] bool Owned(Transform value)
    {for(var t=value;t!=null;t=t.parent)if(spellId=="__native_shield__"&&t.GetComponent<Shield>()!=null||t.name.StartsWith("AzerothVfx_"+spellId+"_",StringComparison.Ordinal)||t.name=="AzerothProjectileVfx_"+spellId||spellId=="__corpse__"&&t.name.StartsWith("AzerothCorpse_",StringComparison.Ordinal))return true;return false;}
    [HideFromIl2Cpp] object[] RendererSnapshot()
    {
        var camera=Camera.main;var result=new List<object>();
        foreach(var renderer in Renderers())
        {
            var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;var material=renderer.sharedMaterial;var c=camera.WorldToViewportPoint(renderer.bounds.center);
            result.Add(new{name=renderer.name,renderer.enabled,renderer.forceRenderingOff,layer=renderer.gameObject.layer,cameraLayerVisible=(camera.cullingMask&(1<<renderer.gameObject.layer))!=0,vertices=mesh?.vertexCount??0,triangles=mesh?.triangles?.Length??0,boundsCenter=V(renderer.bounds.center),boundsSize=V(renderer.bounds.size),viewportCenter=V(c),material=material?.name,shader=material?.shader?.name,alpha=NativeAlpha(material),texture=material?.mainTexture?.name,textureWidth=material?.mainTexture?.width??0,textureHeight=material?.mainTexture?.height??0});
        }return result.ToArray();
    }
    [HideFromIl2Cpp] static int Vertices(object value)=>Json(value).EnumerateArray().Sum(r=>Prop(r,"vertices").GetInt32());
    [HideFromIl2Cpp] string RejectedCondition()
    {
        if(accepted)return null;string kind=Text(spells[index],"Kind");int status=Prop(spells[index],"StatusKind").GetInt32();
        if(kind=="passive")return "Source passive requires its natural trigger; explicit free cast is not a valid proc.";
        if(kind is "selfBuff" or "barrier")
        {
            if(status==6&&casterHpBefore>casterMaxBefore*.85f)return "Actual source heal HP threshold: beforeHP>.85*maxHP.";
            if(status==11&&casterHpBefore>casterMaxBefore*.35f)return "Actual source invulnerability HP threshold: beforeHP>.35*maxHP.";
            if(status==7&&Prop(casterBefore,"ShieldRemaining").GetSingle()>0)return "Actual source shield already active in before-cast snapshot.";
        }
        return null; // Do not infer a pet/target/cooldown reason from a rejected bool.
    }
    [HideFromIl2Cpp] void SaveDeathBlocker()=>Save("death-clock-blocked.json",new{pause=PauseObservation(),tome=Json(Get(Tome,"SnapshotJson")),animation=Json(Call("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge","AnimationDiagnostics")),death=Death()});
    [HideFromIl2Cpp] void AfterSourceChecks(){if(Environment.GetCommandLineArgs().Contains("-azeroth-vfx-shield-smoke")){phase=20;entered=Time.realtimeSinceStartup;}else Finish(null);}
    [HideFromIl2Cpp] static object NativeAlpha(Material value)=>value!=null&&value.HasProperty("_Color")?(object)value.color.a:null;
    [HideFromIl2Cpp] bool ShieldStatus()=>MyPlayer.Instance.inventory.statusEffects.HasStatusEffect(EStatusEffect.Shield);
    [HideFromIl2Cpp] Shield[] Shields()=>Object.FindObjectsOfType<Shield>().Where(s=>s!=null&&s.gameObject.activeInHierarchy).OrderBy(s=>s.GetInstanceID()).ToArray();
    [HideFromIl2Cpp] float[] ShieldTimers()=>Shields().Select(s=>s.destroyTimer).ToArray();
    [HideFromIl2Cpp] bool ShieldVisible()=>Shields().Any(s=>s.renderer!=null&&s.renderer.enabled&&s.renderer.gameObject.activeInHierarchy&&!s.renderer.forceRenderingOff);
    [HideFromIl2Cpp] object ShieldSnapshot()=>new{nativeShieldStatus=ShieldStatus(),nativeCanTakeDamageMeaning="teleport gate only; not shield immunity",invincible=MyPlayer.Instance.isInvincible,canTakeDamage=MyPlayer.Instance.CanTakeDamage(),hp=MyPlayer.Instance.inventory.playerHealth.hp,nativeHpShield=MyPlayer.Instance.inventory.playerHealth.shield,nativeDamageCooldown=MyPlayer.Instance.inventory.playerHealth.damageCooldown,nativeDuration=shieldDuration,sourceClock=Get(Clock,"Time"),nativePaused=MyTime.paused,shields=Shields().Select(s=>new{id=s.GetInstanceID(),s.destroyTimer,active=s.gameObject.activeInHierarchy,enabled=s.renderer!=null&&s.renderer.enabled,forceOff=s.renderer!=null&&s.renderer.forceRenderingOff,material=s.renderer?.sharedMaterial?.name,alpha=NativeAlpha(s.renderer?.sharedMaterial)}).ToArray(),nativeEffects=Optional("AzerothMegabonk.MageVisuals.MageVisualBridge","NativeEffectDiagnosticsJson")};
    [HideFromIl2Cpp] void IsolatePointerModules()
    {
        foreach(var module in Resources.FindObjectsOfTypeAll<BaseInputModule>())
        {
            try
            {
                if(module==null||!module.gameObject.scene.IsValid())continue;int id=module.GetInstanceID();
                if(!inputModules.Any(v=>v.Id==id))inputModules.Add((module,module.enabled,module.name,id));module.enabled=false;
            }
            catch{pointerDestroyedSkipped++;} // Native scene teardown may invalidate an interop wrapper.
        }
    }
    [HideFromIl2Cpp] object PointerModuleSnapshot()
    {
        var states=new List<object>();
        foreach(var saved in inputModules)
        {
            bool alive=false;int current=-1;
            try{if(saved.Module!=null&&saved.Module.gameObject!=null){current=saved.Module.enabled?1:0;alive=true;}}catch{ }
            states.Add(new{name=saved.Name,id=saved.Id,originalEnabled=saved.Enabled,nativeAlive=alive,currentEnabled=current});
        }return states.ToArray();
    }
    [HideFromIl2Cpp] JsonElement Death()=>Json(Call("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge","DeathDiagnostics"));
    [HideFromIl2Cpp] bool FindCorpse(JsonElement json,out JsonElement row){foreach(var item in Prop(json,"Corpses").EnumerateArray())if(Prop(item,"EnemyId").GetUInt32()==deathId){row=item;return true;}row=default;return false;}
    [HideFromIl2Cpp] bool NativeReleased()=>fixture==null||fixture.id!=deathId||!fixture.gameObject.activeInHierarchy;
    [HideFromIl2Cpp] Vector3 CaptureTarget()=>spellId=="__native_shield__"?MyPlayer.Instance.transform.position+Vector3.up:spellId=="__corpse__"&&deathFirst.ValueKind==JsonValueKind.Object?new Vector3(Prop(deathFirst,"Position")[0].GetSingle(),Prop(deathFirst,"Position")[1].GetSingle(),Prop(deathFirst,"Position")[2].GetSingle()):fixture!=null?fixture.transform.position:MyPlayer.Instance.transform.position;
    [HideFromIl2Cpp] object PauseObservation()=>new{phase,age=Age,nativePaused=MyTime.paused,pauseUi=UiManager.Instance?.pause?.IsPaused(),levelup=LevelupScreen.isLevelingUp,gameOver=GameManager.Instance?.isGameOver,canSimulate=Get(Clock,"CanSimulate"),pauseReason=Get(Clock,"PauseReason"),combatTime=Get(Clock,"Time"),unityScale=Time.timeScale,nativeScale=MyTime.timeScale,inventoryPaused=MyPlayer.Instance?.inventory?.pause,encounterInProgress=UiManager.Instance?.encounterWindows?.encounterInProgress,nativeStatuses=MyPlayer.Instance?.inventory?.statusEffects==null?Array.Empty<string>():Enum.GetValues<EStatusEffect>().Where(e=>MyPlayer.Instance.inventory.statusEffects.HasStatusEffect(e)).Select(e=>e.ToString()).ToArray(),mapTransition=new{generating=MapGenerationController.isGenerating,teleporting=MyPlayer.Instance?.isTeleporting,cutscene=GameManager.Instance?.cutscene,runPlaying=GameManager.Instance?.IsPlaying()},inputModules=PointerModuleSnapshot()};
    [HideFromIl2Cpp] object Snapshot(object renderers=null)=>new{classId,spellId,phase,frame=Time.frameCount,combatTime=Get(Clock,"Time"),nativePaused=MyTime.paused,fixture=fixture!=null?new{id=fixture.id,fixture.hp,position=V(fixture.transform.position)}:null,vfx=Json(Get(Vfx,"SnapshotJson")),telemetry=Json(Get(Ability,"TelemetryJson")),renderers=renderers??RendererSnapshot(),detailedRenderers=Optional(Vfx,"RendererSnapshotJson")};
    [HideFromIl2Cpp] long Capture(string name,bool paired,bool groundInspection=false)
    {
        var camera=Camera.main;if(camera==null)throw new InvalidOperationException("Native world camera missing");const int width=960,height=540;
        var canvases=Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas&&c.enabled&&c.renderMode!=RenderMode.WorldSpace).ToArray();var renderers=Renderers();var hidden=renderers.Select(r=>r.forceRenderingOff).ToArray();var prior=camera.targetTexture;var active=RenderTexture.active;var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;var clockBefore=(float)Get(Clock,"Time");var rt=new RenderTexture(width,height,24);rt.Create();Texture2D image=null;long changed=0;
        try
        {
            foreach(var canvas in canvases)canvas.enabled=false;
            if(groundInspection)
            {var at=CaptureTarget();camera.transform.position=at+new Vector3(0,8,-6);camera.transform.rotation=Quaternion.LookRotation(at+Vector3.up*.5f-camera.transform.position,Vector3.up);}
            camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();var on=image.GetPixels32();WritePpm(name+"-world.ppm",on,width,height);
            if(paired)
            {
                foreach(var renderer in renderers)renderer.forceRenderingOff=true;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();var off=image.GetPixels32();WritePpm(name+"-without-spell.ppm",off,width,height);
                var ground=camera.WorldToViewportPoint(CaptureTarget());long nearGround=0,absolute=0;
                for(int i=0;i<on.Length;i++){int delta=Math.Abs(on[i].r-off[i].r)+Math.Abs(on[i].g-off[i].g)+Math.Abs(on[i].b-off[i].b);absolute+=delta;if(delta<=12)continue;changed++;float x=(i%width)/(float)width,y=(i/width)/(float)height;if(ground.z>0&&Mathf.Abs(x-ground.x)<.16f&&Mathf.Abs(y-ground.y)<.16f)nearGround++;}
                Save(name+"-pixels.json",new{width,height,groundInspection,changedPixels=changed,nearTargetGroundProjectedRoiPixels=nearGround,projectedTarget=V(ground),roiHalfSizeNormalized=.16f,totalAbsoluteRgbDelta=absolute,matchingRenderers=renderers.Length,clockBefore,clockAfter=Get(Clock,"Time"),combatClockUnchanged=Mathf.Abs((float)Get(Clock,"Time")-clockBefore)<.00001f,note="Two same-clock native camera renders; matching spell renderer flags only. Projected ground ROI is a screen region, not depth-ground intersection proof; nonzero pixels do not verify meteor appearance."});
            }
        }
        finally
        {
            for(int i=0;i<renderers.Length;i++)if(renderers[i]!=null)renderers[i].forceRenderingOff=hidden[i];foreach(var canvas in canvases)if(canvas!=null)canvas.enabled=true;camera.targetTexture=prior;camera.transform.position=cameraPosition;camera.transform.rotation=cameraRotation;RenderTexture.active=active;rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);
        }return changed;
    }
    [HideFromIl2Cpp] void WritePpm(string name,Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> pixels,int width,int height)
    {var bytes=new byte[width*height*3];int at=0;for(int y=height-1;y>=0;y--)for(int x=0;x<width;x++){var p=pixels[y*width+x];bytes[at++]=p.r;bytes[at++]=p.g;bytes[at++]=p.b;}using var stream=File.Create(Path.Combine(folder,name));stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));stream.Write(bytes);}
    [HideFromIl2Cpp] static string Arg(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
    [HideFromIl2Cpp] static Vector3 Forward(MyPlayer p){var f=p.transform.forward;f.y=0;return f.sqrMagnitude>.001f?f.normalized:Vector3.forward;}
    [HideFromIl2Cpp] static float[] V(Vector3 value)=>new[]{value.x,value.y,value.z};
    [HideFromIl2Cpp] Type Named(string name){if(types.TryGetValue(name,out var t))return t;t=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(a=>a!=null);return types[name]=t??throw new MissingMemberException(name);}
    [HideFromIl2Cpp] object Get(string type,string property)=>Named(type).GetProperty(property,BindingFlags.Public|BindingFlags.Static)?.GetValue(null)??throw new MissingMemberException(type,property);
    [HideFromIl2Cpp] object Call(string type,string method,params object[] args)=>Named(type).GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);
    [HideFromIl2Cpp] object Optional(string type,string property){try{return Json(Get(type,property));}catch(Exception error){return new{unavailable=error.Message};}}
    [HideFromIl2Cpp] static JsonElement Json(object value){using var doc=JsonDocument.Parse(value as string??JsonSerializer.Serialize(value));return doc.RootElement.Clone();}
    [HideFromIl2Cpp] static JsonElement Prop(JsonElement value,string name){foreach(var p in value.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase))return p.Value;throw new MissingMemberException(name);}
    [HideFromIl2Cpp] static string Text(JsonElement value,string name)=>Prop(value,name).GetString();
    [HideFromIl2Cpp] static double Counter(JsonElement value,string group,string id)=>Prop(value,group).TryGetProperty(id,out var count)?count.GetDouble():0;
    [HideFromIl2Cpp] void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),value as string??JsonSerializer.Serialize(value));
    [HideFromIl2Cpp] void Record(string status,string label,string detail=""){if(status=="PASS")passed++;else if(status=="FAIL")failed++;else skipped++;checks.Add(new{status,label,detail});Plugin.Note("VFX FIDELITY "+classId+" "+status+" "+label+" "+detail);}
    [HideFromIl2Cpp] void Require(bool value,string label,string detail=""){Record(value?"PASS":"FAIL",label,detail);if(!value)throw new InvalidOperationException(label+" "+detail);}
    [HideFromIl2Cpp] void Finish(Exception error)
    {
        if(done)return;done=true;if(error!=null)Record("FAIL","probe exception",error.ToString());
        try{Save("final-pause-observation.json",PauseObservation());if(UiManager.Instance?.pause?.IsPaused()==true)UiManager.Instance.pause.Resume();Call(Tome,"SetAutomaticCastingForProbe",false);}catch(Exception cleanup){Record("FAIL","cleanup",cleanup.ToString());}
        foreach(var saved in inputModules){try{if(saved.Module!=null&&saved.Module.gameObject!=null)saved.Module.enabled=saved.Enabled;}catch{pointerDestroyedSkipped++;}}
        Save("summary.json",new{pass=failed==0,classId,passed,failed,skipped,pointerDestroyedSkipped,phase,wallSeconds=Time.realtimeSinceStartup-began,checks,spells=observations,rainMeteorAppearanceVerified=false,error=error?.ToString(),coverage="Real current-class casts, sampled native world-camera pixel pairs and source VFX resource/pause evidence. Not source-engine pixel matching or all natural talent procs. Optional death uses real native Kill and original1.2s source corpse lifetime; not elemental death VFX fidelity."});Application.Quit(failed==0?0:2);
    }
}

[HarmonyPatch]
static class VfxProbeButtonIsolation
{
    static bool Prepare()=>VfxFidelityProbe.Armed;
    static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));
    static bool Prefix(ref bool __result){__result=false;return false;}
}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class VfxProbeAxisIsolation
{static bool Prepare()=>VfxFidelityProbe.Armed;static bool Prefix(ref float __result){__result=0;return false;}}
