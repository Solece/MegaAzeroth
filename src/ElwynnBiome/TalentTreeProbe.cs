using System.Reflection;
using System.Text.Json;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Utility;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// This runner is armed only in its own explicitly requested batch process.
public sealed class TalentTreeProbe:MonoBehaviour
{
    public TalentTreeProbe(IntPtr pointer):base(pointer){}
    const string Bridge="MageTalents.TalentBridge",Ui="MageTalents.TalentTreeDiagnostics",Clock="AzerothAbilities.CombatSimulation",Tome="AzerothAbilities.TomeCompatibilityDiagnostics";
    float began,at,before;int phase;bool done;string folder,relative,originalRanks;long originalXp;
    readonly List<object> checks=new();
    void Start()
    {
        if(!Plugin.TreeSmoke){enabled=false;return;}
        began=Time.realtimeSinceStartup;Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
        relative="talent-tree-"+Environment.ProcessId;folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports",relative));Directory.CreateDirectory(folder);
        Plugin.Note("TALENT TREE BEGIN "+folder);
    }
    void Update()
    {
        if(done)return;
        try
        {
            var input=Object.FindObjectOfType<MyInputManager>();if(input?.rewiredEventSystem!=null)input.rewiredEventSystem.enabled=false;
            if(Time.realtimeSinceStartup-began>100)throw new TimeoutException("Talent tree deadline at phase="+phase);
            var p=MyPlayer.Instance;if(p?.inventory?.playerHealth!=null)p.inventory.playerHealth.hp=p.inventory.playerHealth.maxHp;
            switch(phase)
            {
                case 0:
                    if(!ElwynnBridge.Ready||p?.inventory==null||!p.hasStarted||LevelupScreen.isLevelingUp||GameManager.Instance?.isGameOver==true)return;
                    Call(Tome,"SetAutomaticCastingForProbe",false);at=Time.realtimeSinceStartup;phase++;break;
                case 1:
                    if(Time.realtimeSinceStartup-at<6)return;
                    using(var state=JsonDocument.Parse((string)Get(Bridge,"StateJson")))
                    {originalXp=Property(state.RootElement,"talentXp").GetInt64();originalRanks=Property(state.RootElement,"ranks").GetRawText();}
                    if(!(bool)Call(Bridge,"SetPanelVisibleForProbe",true))return;
                    at=Time.realtimeSinceStartup;phase++;break;
                case 2:
                    if(Time.realtimeSinceStartup-at<.3f)return;
                    Require((bool)Get(Ui,"Visible") && (int)Get(Ui,"CatalogNodeCount")==39,"39 source nodes loaded");
                    Require((int)Get(Ui,"ConcurrentTrees")==3 && (int)Get(Ui,"NodeCount")==39,"three source trees and all thirty-nine nodes visible together");
                    Require(MyTime.paused&&Time.timeScale==0,"tree pauses native and source clocks");
                    before=(float)Get(Clock,"Time");Save("open-maxed.json",Describe());Capture("mage-arcane-maxed.ppm");at=Time.realtimeSinceStartup;phase++;break;
                case 3:
                    if(Time.realtimeSinceStartup-at<.7f)return;
                    Require(Mathf.Abs((float)Get(Clock,"Time")-before)<.0001f,"source simulation stays frozen while tree open");
                    Require((bool)Call(Ui,"SelectTreeForProbe",1),"select Fire native tree UI");at=Time.realtimeSinceStartup;phase++;break;
                case 4:
                    if(Time.realtimeSinceStartup-at<.25f)return;
                    Require((int)Get(Ui,"NodeCount")==39,"highlighting Fire retains all three source trees");
                    Save("fire-maxed.json",Describe());Capture("mage-fire-maxed.ppm");
                    Require((bool)Call(Ui,"SelectTreeForProbe",2),"select Frost native tree UI");at=Time.realtimeSinceStartup;phase++;break;
                case 5:
                    if(Time.realtimeSinceStartup-at<.25f)return;
                    Require((int)Get(Ui,"NodeCount")==39,"highlighting Frost retains all three source trees");
                    Require((bool)Call(Ui,"HoverForProbe","water_elemental"),"source capstone tooltip");
                    Require((bool)Call(Ui,"ScrollForProbe",0f),"all source rows fit without scrolling");at=Time.realtimeSinceStartup;phase++;break;
                case 6:
                    if(Time.realtimeSinceStartup-at<.3f)return;
                    Require(((string)Get(Ui,"TooltipText")).Contains("Water Elemental"),"capstone source name and description visible");
                    Require((int)Get(Ui,"MissingTextures")==0,"all referenced talent artwork present");
                    Save("frost-capstone.json",Describe());Capture("mage-frost-capstone.ppm");
                    Call(Ui,"BatchProbe","reset");Call(Ui,"SelectTreeForProbe",0);at=Time.realtimeSinceStartup;phase++;break;
                case 7:
                    if(Time.realtimeSinceStartup-at<.25f)return;
                    Require((int)Get(Bridge,"SpentPoints")==0,"reset clears allocation");
                    Require(!(bool)Call(Bridge,"CanSpend","arcane_power"),"deep talent remains locked without required allocation");
                    Require((bool)Call(Ui,"ClickNodeForProbe","arcane_focus",false),"real talent Button invokes purchase");
                    Require((int)Call(Bridge,"Rank","arcane_focus")==1 && (int)Get(Bridge,"SpentPoints")==1,"purchase consumes one point and raises real rank");
                    at=Time.realtimeSinceStartup;phase++;break;
                case 8:
                    if(Time.realtimeSinceStartup-at<.25f)return;
                    Save("purchased-first-node.json",Describe());Capture("mage-arcane-rank-one.ppm");
                    Require((bool)Call(Ui,"RightClickNodeForProbe","arcane_focus"),"real right-click event invokes refund");
                    Require((int)Call(Bridge,"Rank","arcane_focus")==0 && (int)Get(Bridge,"SpentPoints")==0,"refund returns point and clears real rank");
                    Require((bool)Call(Bridge,"SetProbeState",originalXp,originalRanks),"restore original maxed source allocation");
                    Require((bool)Call(Bridge,"SetPanelVisibleForProbe",false),"close tree");
                    at=Time.realtimeSinceStartup;phase++;break;
                case 9:
                    if(Time.realtimeSinceStartup-at<.25f)return;
                    Require(!MyTime.paused && Time.timeScale>0,"closing restores running native clock");
                    MyTime.Pause();Require((bool)Call(Bridge,"SetPanelVisibleForProbe",true),"open tree inside existing native pause");
                    at=Time.realtimeSinceStartup;phase++;break;
                case 10:
                    if(Time.realtimeSinceStartup-at<.2f)return;
                    Call(Bridge,"SetPanelVisibleForProbe",false);
                    Require(MyTime.paused,"closing tree preserves pre-existing native pause");MyTime.Unpause();
                    Save("final-state.json",Describe());Finish(true,null);break;
            }
        }
        catch(Exception e){Finish(false,e is TargetInvocationException t?t.InnerException??t:e);}
    }
    [HideFromIl2Cpp] static Type Type(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).First(t=>t!=null);
    [HideFromIl2Cpp] static object Get(string type,string name)=>Type(type).GetProperty(name,BindingFlags.Static|BindingFlags.Public).GetValue(null);
    [HideFromIl2Cpp] static object Call(string type,string name,params object[] args)=>Type(type).GetMethods(BindingFlags.Static|BindingFlags.Public).First(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(null,args);
    [HideFromIl2Cpp] static JsonElement Property(JsonElement element,string name)=>element.EnumerateObject().First(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).Value;
    [HideFromIl2Cpp] static string Describe()=>(string)Call(Ui,"Describe");
    [HideFromIl2Cpp] void Require(bool result,string message){if(!result)throw new InvalidOperationException(message);checks.Add(new{pass=true,message});Plugin.Note("TALENT TREE PASS "+message);}
    [HideFromIl2Cpp] void Save(string name,string text)=>File.WriteAllText(Path.Combine(folder,name),text);
    [HideFromIl2Cpp] void Capture(string name)=>BiomeCapture.Capture(relative+"/"+name);
    [HideFromIl2Cpp] void Finish(bool passed,Exception error)
    {
        done=true;
        try{if(originalRanks!=null)Call(Bridge,"SetProbeState",originalXp,originalRanks);Call(Bridge,"SetPanelVisibleForProbe",false);}catch{}
        Save("summary.json",JsonSerializer.Serialize(new{pass=passed,phase,checks,error=error?.ToString(),wallSeconds=Time.realtimeSinceStartup-began}));
        Plugin.Note("TALENT TREE "+(passed?"PASS":"FAIL "+error));Application.Quit(passed?0:2);
    }
}
