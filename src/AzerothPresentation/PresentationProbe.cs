using System.Reflection;
using System.Text.Json;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using Assets.Scripts._Data.MapsAndStages;
using Assets.Scripts.Utility;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace AzerothPresentation;

public sealed class PresentationProbe:MonoBehaviour
{
    public PresentationProbe(IntPtr p):base(p){}
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetEnvironmentVariable("AZEROTH_LAB_RENDER_PROBE")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-presentation-smoke");
    static PresentationProbe owner;
    readonly List<object> checks=new();readonly List<JsonElement> loadingRows=new();readonly List<(BaseInputModule Module,int Id,bool Enabled)> modules=new();
    readonly HashSet<string> recorded=new();readonly HashSet<int> loadingThresholds=new();readonly List<JsonElement> fillProofs=new();
    int phase,passed,failed,skipped,loadingFrames,loadingSession,maxCaptures,previousStageScene;IntPtr previousStagePlayer;float began,entered,nextLoading,nextHeartbeat,menuReadySince=-1,maxProgress;string folder;bool done,gameStarted,firstFillCaptured,preStableCaptured;
    void Start()
    {
        if(!Armed){enabled=false;return;}owner=this;began=entered=Time.realtimeSinceStartup;
        folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports","presentation-"+Environment.ProcessId));Directory.CreateDirectory(folder);
        if(!Environment.GetCommandLineArgs().Contains("-azeroth-test-menu")){Finish(new InvalidOperationException("Use native test-menu session save guard; no automatic test-battle runner"));return;}
        Application.runInBackground=true;
    }
    void Update()
    {
        if(done||!Armed)return;
        try
        {
            if(Time.realtimeSinceStartup-began>120)throw new TimeoutException("Presentation native fixture exceeded120s, phase="+phase);
            IsolateInput();var state=J(PresentationBridge.Describe());
            if(Time.realtimeSinceStartup>=nextHeartbeat){nextHeartbeat=Time.realtimeSinceStartup+2;Save("probe-heartbeat.json",new{Phase=phase,Age,MenuGate=ReadMenuGate(),State=state});if(phase==0)Save("early-native-scene.json",PresentationBridge.NativeSceneJson());}
            if(phase<8&&MyPlayer.Instance?.inventory?.playerHealth!=null){var hp=MyPlayer.Instance.inventory.playerHealth;hp.hp=hp.maxHp;hp.damageCooldown=10;}
            if(phase is >=4 and <=7&&LevelupScreen.isLevelingUp){var level=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(x=>x.gameObject.activeInHierarchy);if(level!=null){Save("incidental-levelup-"+Time.frameCount+".json",new{Phase=phase});level.CloseLevelupScreen();}return;}
            var main=Object.FindObjectOfType<MainMenu>();
            switch(phase)
            {
                case 0:
                    if(!preStableCaptured&&main!=null&&B(state,"MenuActive")&&Age>=3){preStableCaptured=true;Save("pre-stable-menu-gate.json",ReadMenuGate());PresentationCapture.Capture(Path.Combine(folder,"pre-stable-menu.ppm"));}
                    if(main==null||!B(state,"MenuActive")||!MenuStable())return;
                    Check(B(state,"ArtReady")&&S(state,"Error")=="","source presentation ready without runtime errors");
                    Save("main-native-scene.json",PresentationBridge.NativeSceneJson());Save("main-state.json",state);CheckUnique(state,"main menu");PresentationCapture.Capture(Path.Combine(folder,"main-menu.ppm"));
                    Call("AzerothHeroes.HeroProbeApi","BatchProbe","focus");Next(1);break;
                case 1:
                    if(main==null||!main.tabCharacters.activeSelf||Age<1)return;
                    Call("AzerothHeroes.HeroProbeApi","BatchProbe","click:mage");Next(2);break;
                case 2:
                    if(Age<1.8f)return;
                    Save("character-native-scene.json",PresentationBridge.NativeSceneJson());var pair=J(PresentationCapture.HeroPair(folder,"character-menu"));Save("character-hero-contribution.json",pair);Check(B(pair,"Pass"),"actual source hero contributes visible native-camera pixels",pair.GetRawText());CheckUnique(state,"character menu");
                    PresentationBridge.SetEnabledForProbe(false);Save("presentation-disabled-native-state.json",PresentationBridge.NativeSceneJson());PresentationCapture.Capture(Path.Combine(folder,"native-background-restored.ppm"));PresentationBridge.SetEnabledForProbe(true);Next(3);break;
                case 3:
                    if(Age<.4f)return;Check(B(state,"MenuActive"),"presentation restores after explicit off/on");CheckUnique(state,"re-enabled menu");
                    Call("AzerothHeroes.HeroProbeApi","BatchProbe","confirm:mage");if(main!=null&&!main.tabMaps.activeSelf)main.GoToMapSelection();Next(4);break;
                case 4:
                    if(main==null||!main.tabMaps.activeSelf||Age<.8f)return;
                    Save("map-native-scene.json",PresentationBridge.NativeSceneJson());var mapPair=J(PresentationCapture.HeroPair(folder,"map-menu"));Save("map-hero-contribution.json",mapPair);Check(B(mapPair,"Pass"),"map selection retains actual visible source hero",mapPair.GetRawText());CheckUnique(state,"map selection");
                    if(Environment.GetCommandLineArgs().Contains("-azeroth-presentation-recon-only")){Finish(null);return;}
                    Call("AzerothAbilities.TomeCompatibilityDiagnostics","SetAutomaticCastingForProbe",false);
                    var map=main.mapSelectionUi;if(map.runConfig==null)map.Init();var desert=DataManager.Instance.GetMap(EMap.Desert);map.runConfig.mapData=desert;map.runConfig.stageData=desert.stages[0];map.runConfig.mapTierIndex=0;map.runConfig.challenge=null;map.StartMap();Next(5);break;
                case 5:
                    if(GameManager.Instance==null||MyPlayer.Instance?.inventory==null||MapGenerationController.isGenerating||LoadingScreen.isLoading)return;
                    if(Age<1)return;if(!gameStarted){gameStarted=true;MyPlayer.Instance.hasStarted=true;GameManager.Instance.StartPlaying();Check(!B(state,"MenuActive")&&!B(state,"LoadingActive"),"native gameplay has no menu/loading presentation overlay");CheckUnique(state,"gameplay");Save("gameplay-native-scene.json",PresentationBridge.NativeSceneJson());PresentationCapture.Capture(Path.Combine(folder,"gameplay.ppm"));}
                    if(UiManager.Instance?.pause==null||!UiManager.Instance.pause.CanPause())return;UiManager.Instance.pause.Pause();Next(6);break;
                case 6:
                    if(!UiManager.Instance.pause.IsPaused()){if(Age>4)throw new TimeoutException("Native pause admission");return;}
                    Check(!B(state,"MenuActive")&&!B(state,"LoadingActive"),"native pause remains free of menu/loading background");PresentationCapture.Capture(Path.Combine(folder,"native-pause.ppm"));UiManager.Instance.pause.Resume();Next(7);break;
                case 7:
                    if((MyTime.paused||UiManager.Instance.pause.IsPaused())&&Age<4)return;
                    Check(!MyTime.paused&&!UiManager.Instance.pause.IsPaused(),"native Resume restores actual gameplay");
                    previousStageScene=SceneManager.GetActiveScene().handle;previousStagePlayer=MyPlayer.Instance.Pointer;
                    Save("native-next-stage-fixture.json",new{FromStage=MapController.GetStageIndex(),OldSceneHandle=previousStageScene,OldPlayerPointer=previousStagePlayer.ToInt64(),Source="Actual MapController.LoadNextStage for bounded loading lifecycle QA; not a natural portal/boss progression claim."});MapController.LoadNextStage();Next(11);break;
                case 11:
                    if(MapController.GetStageIndex()<1||LoadingScreen.isLoading||MapGenerationController.isGenerating||GameManager.Instance==null||MyPlayer.Instance?.inventory==null||Age<1||SceneManager.GetActiveScene().handle==previousStageScene)return;
                    MyPlayer.Instance.hasStarted=true;if(!GameManager.Instance.IsPlaying())GameManager.Instance.StartPlaying();
                    Check(!B(state,"MenuActive")&&!B(state,"LoadingActive"),"second native stage removes source loading/menu layers");CheckUnique(state,"second native stage");
                    if(loadingSession>=2)Check(true,"two actual native LoadingScreen instances styled without duplicate canvases");else Record("SKIP","second native LoadingScreen not observed","Native quick-reset/transition route retained; no forced loading delay or fake instance.");
                    Save("native-next-stage-complete.json",new{SceneHandle=SceneManager.GetActiveScene().handle,PlayerPointer=MyPlayer.Instance.Pointer.ToInt64(),Stage=MapController.GetStageIndex(),ObservedLoadingSessions=loadingSession});
                    PresentationCapture.Capture(Path.Combine(folder,"second-stage-gameplay.ppm"));Save("second-stage-state.json",state);Save("pre-end-state.json",state);MyPlayer.Instance.inventory.playerHealth.KillPlayer();Next(8);break;
                case 8:
                    if(GameManager.Instance?.isGameOver!=true||Age<2)return;
                    UiManager.Instance.deathScreen.ShowStats();Next(9);break;
                case 9:
                    if(Age<.5f)return;Check(!B(state,"MenuActive")&&!B(state,"LoadingActive"),"native results remain free of menu/loading background");PresentationCapture.Capture(Path.Combine(folder,"native-results.ppm"));TransitionUI.Instance.LoadMenu();Next(10);break;
                case 10:
                    if(main==null||!B(state,"MenuActive")||!MenuStable())return;
                    CheckUnique(state,"returned menu");Check(!B(state,"LoadingActive"),"loading canvas removed after native return to menu");PresentationCapture.Capture(Path.Combine(folder,"returned-menu.ppm"));Save("returned-menu-state.json",state);
                    if(loadingRows.Count==0){Record("SKIP","native quick-reset/scene timing provided no observable loading frame","No loading/progress visual acceptance claimed.");}
                    else{Check(loadingRows.All(r=>B(P(r,"Loading"),"NativeBarReferencePreserved")),"loading retains actual native progress Transform identity");Check(loadingRows.All(r=>Math.Abs(P(P(r,"Loading"),"ActualFillWidth").GetSingle()/P(P(r,"Loading"),"FullFillWidth").GetSingle()-P(r,"NativeProgress").GetSingle())<.002f),"actual source fill rectangle fraction matches native progress");Check(loadingRows.All(r=>I(P(r,"Loading"),"RaycastTargets")==0&&I(P(r,"Loading"),"GraphicRaycasters")==0),"source loading graphics intercept no input");if(fillProofs.Count>0)Check(fillProofs.All(p=>B(p,"Pass")),"actual nonzero native-progress fill contributes on-screen pixels");else Record("SKIP","native loading provided no observable nonzero fill","No filled-bar pixel proof claimed.");}
                    Save("loading-observations.json",loadingRows);Finish(null);break;
            }
        }
        catch(Exception e){Finish(e is TargetInvocationException t?t.InnerException??e:e);}
    }
    [HideFromIl2Cpp] internal static void ObserveLoading(LoadingScreen native)
    {
        if(owner==null||owner.done||!Armed)return;
        try{owner.loadingSession++;owner.loadingThresholds.Clear();owner.firstFillCaptured=false;owner.maxProgress=-1;owner.maxCaptures=0;var row=J(PresentationBridge.Describe());owner.loadingRows.Add(row);owner.Save("loading-start-"+owner.loadingSession+".json",new{NativePath=PresentationDiagnostics.Path(native.transform),NativeBarScale=native.loadingBar?.localScale.x,Presentation=row});PresentationCapture.Capture(Path.Combine(owner.folder,"loading-start-"+owner.loadingSession+".ppm"));}catch(Exception e){owner.Record("FAIL","actual loading capture",e.ToString());}
    }
    [HideFromIl2Cpp] internal static void ObserveLoadingFrame()
    {
        if(owner==null||owner.done||!Armed||Time.realtimeSinceStartup<owner.nextLoading)return;
        try
        {
            owner.nextLoading=Time.realtimeSinceStartup+.08f;var row=J(PresentationBridge.Describe());if(!B(row,"LoadingActive"))return;
            owner.loadingRows.Add(row);owner.Save("loading-frame-"+owner.loadingFrames+++".json",row);float progress=P(row,"NativeProgress").GetSingle();
            if(progress>owner.maxProgress+.001f&&owner.maxCaptures<20){owner.maxProgress=progress;owner.maxCaptures++;PresentationCapture.Capture(Path.Combine(owner.folder,"loading-max-progress-"+owner.loadingSession+".ppm"));owner.Save("loading-max-progress-"+owner.loadingSession+".json",row);}
            foreach(int threshold in new[]{25,50,90})if(progress>=threshold*.01f&&owner.loadingThresholds.Add(threshold)){PresentationCapture.Capture(Path.Combine(owner.folder,"loading-progress-"+owner.loadingSession+"-"+threshold+".ppm"));owner.Save("loading-progress-"+owner.loadingSession+"-"+threshold+".json",row);}
            if(progress>.02f&&!owner.firstFillCaptured){owner.firstFillCaptured=true;var proof=J(PresentationCapture.LoadingFillPair(owner.folder,owner.loadingSession));owner.fillProofs.Add(proof);owner.Save("loading-fill-proof-"+owner.loadingSession+".json",new{Observed=row,Pixels=proof});}
        }
        catch(Exception e){owner.Record("FAIL","actual loading frame/pixel proof",e.ToString());}
    }
    float Age=>Time.realtimeSinceStartup-entered;
    [HideFromIl2Cpp] void Next(int value){phase=value;entered=Time.realtimeSinceStartup;menuReadySince=-1;}
    [HideFromIl2Cpp] bool MenuStable()
    {
        var gate=ReadMenuGate();bool faded=!B(gate,"IsTransitioning")&&P(gate,"EffectiveAlpha").GetSingle()<.02f;
        if(!faded){menuReadySince=-1;return false;}if(menuReadySince<0)menuReadySince=Time.realtimeSinceStartup;return Time.realtimeSinceStartup-menuReadySince>=1.2f;
    }
    // Native Start/EndTransition use Graphic.CrossFadeAlpha (slot49), which
    // tweens CanvasRenderer color and leaves Graphic.color.a unchanged.
    [HideFromIl2Cpp] static JsonElement ReadMenuGate()
    {
        var transition=TransitionUI.Instance;var overlay=transition?.overlay;
        bool busy=transition!=null&&transition.isTransitioning,active=overlay!=null&&overlay.isActiveAndEnabled;
        float graphicAlpha=overlay!=null?overlay.color.a:0,rendererAlpha=0,inheritedAlpha=0;bool culled=false,canvasActive=false;
        if(overlay!=null)
        {
            var renderer=overlay.canvasRenderer;rendererAlpha=renderer.GetColor().a;inheritedAlpha=renderer.GetInheritedAlpha();culled=renderer.cull;
            canvasActive=overlay.canvas!=null&&overlay.canvas.isActiveAndEnabled;
            foreach(var canvas in overlay.GetComponentsInParent<Canvas>(true))if(!canvas.isActiveAndEnabled)canvasActive=false;
        }
        float effective=active&&canvasActive&&!culled?graphicAlpha*rendererAlpha*inheritedAlpha:0;
        return J(new{IsTransitioning=busy,OverlayActive=active,CanvasActive=canvasActive,Culled=culled,GraphicAlpha=graphicAlpha,CanvasRendererAlpha=rendererAlpha,InheritedAlpha=inheritedAlpha,EffectiveAlpha=effective,StableSince=owner?.menuReadySince,ReadSource="Actual CanvasRenderer.CrossFadeAlpha state; native transition untouched"});
    }
    [HideFromIl2Cpp] void CheckUnique(JsonElement state,string context){Check(I(state,"OwnedMenuObjects")<=1&&I(state,"OwnedLoadingCanvases")<=1,"no duplicate owned presentation objects: "+context);}
    [HideFromIl2Cpp] void IsolateInput(){foreach(var m in Resources.FindObjectsOfTypeAll<BaseInputModule>())try{if(m==null||!m.gameObject.scene.IsValid())continue;int id=m.GetInstanceID();if(!modules.Any(v=>v.Id==id))modules.Add((m,id,m.enabled));m.enabled=false;}catch{}}
    [HideFromIl2Cpp] static object Call(string type,string method,params object[] args){var t=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(type,false)).First(x=>x!=null);return t.GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);}
    [HideFromIl2Cpp] static JsonElement J(object value){using var d=JsonDocument.Parse(value as string??JsonSerializer.Serialize(value));return d.RootElement.Clone();}
    [HideFromIl2Cpp] static JsonElement P(JsonElement e,string n)=>e.GetProperty(n);
    [HideFromIl2Cpp] static bool B(JsonElement e,string n)=>P(e,n).GetBoolean();
    [HideFromIl2Cpp] static int I(JsonElement e,string n)=>P(e,n).GetInt32();
    [HideFromIl2Cpp] static string S(JsonElement e,string n)=>P(e,n).GetString();
    [HideFromIl2Cpp] void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),value as string??JsonSerializer.Serialize(value));
    [HideFromIl2Cpp] void Check(bool pass,string label,string detail="")=>Record(pass?"PASS":"FAIL",label,detail);
    [HideFromIl2Cpp] void Record(string status,string label,string detail){if(!recorded.Add(status+"|"+label))return;checks.Add(new{Status=status,Label=label,Detail=detail});if(status=="PASS")passed++;else if(status=="FAIL")failed++;else skipped++;}
    [HideFromIl2Cpp] void Finish(Exception error)
    {
        if(done)return;done=true;if(error!=null)Record("FAIL","native presentation fixture",error.ToString());
        try{Save("final-presentation-state.json",PresentationBridge.Describe());}catch{}try{Save("final-native-scene.json",PresentationBridge.NativeSceneJson());}catch{}try{Save("final-menu-gate.json",ReadMenuGate());}catch{}
        foreach(var m in modules)try{if(m.Module!=null)m.Module.enabled=m.Enabled;}catch{}
        Save("summary.json",new{Pass=failed==0,Passed=passed,Failed=failed,Skipped=skipped,Phase=phase,WallSeconds=Time.realtimeSinceStartup-began,Checks=checks,LoadingObservations=loadingRows.Count,Error=error?.ToString(),NativeAppearanceReference="Original art/tint/overscan verified from original source; exact gradient/bar layout not claimed."});owner=null;Application.Quit(failed==0?0:1);
    }
}

[HarmonyPatch]
static class ProbeButtons
{
    static bool Prepare()=>PresentationProbe.Armed;
    static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));
    static bool Prefix(ref bool __result){__result=false;return false;}
}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class ProbeAxis{static bool Prepare()=>PresentationProbe.Armed;static bool Prefix(ref float __result){__result=0;return false;}}
