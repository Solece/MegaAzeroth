using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

public sealed class HeroMenuProbe:MonoBehaviour
{
    public HeroMenuProbe(IntPtr p):base(p){}
    readonly string[] classes={"warrior","paladin","hunter","rogue","priest","shaman","mage","warlock","druid"};
    readonly List<object> checks=new();float began,at;int phase,index;bool done;string output,relative;
    static bool TestMenu=>Environment.GetCommandLineArgs().Contains("-azeroth-test-menu");
    void Start()
    {
        if(!Plugin.HeroMenuSmoke){enabled=false;return;}
        began=Time.realtimeSinceStartup;Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
        relative="hero-menu-"+Environment.ProcessId;output=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports",relative));Directory.CreateDirectory(output);
    }
    void Update()
    {
        if(done)return;
        try
        {
            var input=Object.FindObjectOfType<MyInputManager>();if(input?.rewiredEventSystem!=null)input.rewiredEventSystem.enabled=false;
            if(Time.realtimeSinceStartup-began>70)throw new TimeoutException("Hero menu deadline at "+phase+"/"+index);
            if(Time.realtimeSinceStartup-began<5)return;
            if(phase==0)
            {
                if(DataManager.Instance==null||!Resources.FindObjectsOfTypeAll<CharacterMenu>().Any(m=>m!=null&&m.gameObject.scene.IsValid()))return;
                var main=Object.FindObjectOfType<MainMenu>();
                var go=AccessTools.GetDeclaredMethods(typeof(MainMenu)).FirstOrDefault(m=>new[]{"GoToCharacterSelection","GoToCharacterSelect","GoToCharacterMenu"}.Contains(m.Name)&&m.GetParameters().Length==0);
                if(main!=null&&go!=null)go.Invoke(main,Array.Empty<object>());
                Act("focus");
                if(TestMenu)CheckTestMenuSessionGuard();
                var menu=Resources.FindObjectsOfTypeAll<CharacterMenu>().First(m=>m!=null&&m.gameObject.scene.IsValid());
                AccessTools.Method(typeof(CharacterMenu),"TryInit")?.Invoke(menu,Array.Empty<object>());
                at=Time.realtimeSinceStartup;phase=1;return;
            }
            if(Time.realtimeSinceStartup-at<.3f)return;
            if(phase==1)
            {
                var snapshot=State();var buttons=snapshot.GetProperty("Buttons").EnumerateArray().Where(x=>x.GetProperty("Active").GetBoolean()).ToArray();
                Save("initial-roster.json",snapshot.GetRawText());
                Save("native-menu-methods.txt",string.Join("\n",AccessTools.GetDeclaredMethods(typeof(MainMenu)).Select(m=>m.ToString()))+"\n\n"+string.Join("\n",AccessTools.GetDeclaredMethods(typeof(CharacterMenu)).Select(m=>m.ToString())));
                if(buttons.Length==0&&Time.realtimeSinceStartup-at<8)return;
                Require(buttons.Length==9,"native menu displays nine choices; actual="+buttons.Length);
                Require(buttons.Select(b=>b.GetProperty("ClassId").GetString()).Distinct().Count()==9,"all nine source class IDs unique");
                Require(buttons.All(b=>b.GetProperty("PrivateClone").GetBoolean()&&b.GetProperty("NativeClickHandler").GetBoolean()&&b.GetProperty("PortraitLoaded").GetBoolean()),"all choices retain native Button and original source portrait");
                Save("roster.json",snapshot.GetRawText());phase=2;
            }
            if(phase==2)
            {
                Act("focus");at=Time.realtimeSinceStartup;phase=5;return;
            }
            if(phase==5)
            {
                Act("click:"+classes[index]);at=Time.realtimeSinceStartup;phase=3;return;
            }
            if(phase==3)
            {
                var state=State();Require(state.GetProperty("LastSelected").GetString()==classes[index],"native selection callback "+classes[index]);
                if(TestMenu)CheckTestMenuClass(state,classes[index]);
                var info=state.GetProperty("SelectedHeroPanels").EnumerateArray().First(x=>x.GetProperty("Active").GetBoolean());
                Require(info.GetProperty("Portrait").GetString().Contains(classes[index]),"selected original portrait "+classes[index]);
                Require(!string.IsNullOrWhiteSpace(info.GetProperty("SpellName").GetString())&&!string.IsNullOrWhiteSpace(info.GetProperty("SpellIcon").GetString()),"source starter name and icon "+classes[index]);
                var previewType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothMegabonk.MageVisuals.MenuHeroPreviewBridge",false)).First(t=>t!=null);
                using(var preview=JsonDocument.Parse((string)previewType.GetProperty("DiagnosticsJson").GetValue(null)))
                {
                    var v=preview.RootElement;Save(classes[index]+"-3d-preview.json",v.GetRawText());
                    Require(v.GetProperty("attached").GetBoolean()&&v.GetProperty("classId").GetString()==classes[index],"actual source 3D preview "+classes[index]);
                    Require(v.GetProperty("sourceAssetsValid").GetBoolean()&&v.GetProperty("sourceHeadClosed").GetBoolean(),"source preview body and gear retained "+classes[index]);
                    Require(v.GetProperty("nativeRenderersStillEnabled").GetInt32()==0&&v.GetProperty("idleAnimationId").GetInt32()==0,"native preview hidden and original idle active "+classes[index]);
                }
                Save(classes[index]+"-selected.json",state.GetRawText());
                BiomeCapture.Capture(relative+"/"+classes[index]+"-menu.ppm");NativeMenuCapture.Capture(Path.Combine(output,classes[index]+"-characters-raw.ppm"));
                Act("confirm:"+classes[index]);var route=Object.FindObjectOfType<MainMenu>();if(route!=null&&route.tabMaps!=null&&!route.tabMaps.activeSelf)route.GoToMapSelection();at=Time.realtimeSinceStartup;phase=4;return;
            }
            if(phase==4)
            {
                var state=State();Require(state.GetProperty("LastConfirmed").GetString()==classes[index],"native confirmation callback "+classes[index]);
                Require(state.GetProperty("SelectedClassId").GetString()==classes[index],"native persisted enum maps to source class "+classes[index]);
                var main=Object.FindObjectOfType<MainMenu>();
                Require(main!=null && main.tabMaps!=null && main.tabMaps.activeInHierarchy,"native map tab follows confirmation "+classes[index]);
                var previewType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothMegabonk.MageVisuals.MenuHeroPreviewBridge",false)).First(t=>t!=null);
                using(var preview=JsonDocument.Parse((string)previewType.GetProperty("DiagnosticsJson").GetValue(null)))
                {
                    var v=preview.RootElement;Save(classes[index]+"-map-3d-preview.json",v.GetRawText());
                    Require(v.GetProperty("attached").GetBoolean()&&v.GetProperty("classId").GetString()==classes[index],"selected source hero persists on native map tab "+classes[index]);
                    Require(v.GetProperty("nativeRenderersStillEnabled").GetInt32()==0,"native placeholder stays hidden on map tab "+classes[index]);
                    Require(!string.IsNullOrWhiteSpace(v.GetProperty("focus").GetProperty("camera").GetString()),"actual preview camera focus observed "+classes[index]);
                }
                NativeMenuCapture.Capture(Path.Combine(output,classes[index]+"-maps-raw.ppm"));
                index++;if(index<classes.Length){phase=2;return;}Save("final.json",state.GetRawText());Finish(true,null);
            }
        }
        catch(Exception ex){Finish(false,ex is TargetInvocationException t?t.InnerException??t:ex);}
    }
    [HideFromIl2Cpp] static Type TypeNamed(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null)??throw new MissingMemberException(name);
    [HideFromIl2Cpp] static object Read(string type,string property)=>TypeNamed(type).GetProperty(property,BindingFlags.Static|BindingFlags.Public)?.GetValue(null)??throw new MissingMemberException(type,property);
    [HideFromIl2Cpp] static JsonElement Json(object value){using var doc=JsonDocument.Parse(value as string??JsonSerializer.Serialize(value));return doc.RootElement.Clone();}
    [HideFromIl2Cpp] static JsonElement Prop(JsonElement value,string name)=>value.EnumerateObject().First(p=>p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).Value;
    [HideFromIl2Cpp] static string UiPath(Transform t){var names=new List<string>();for(var p=t;p!=null;p=p.parent)names.Add(p.name);names.Reverse();return string.Join("/",names);}
    [HideFromIl2Cpp] void CheckTestMenuSessionGuard()
    {
        var plugin=TypeNamed("AzerothMegabonk.TestBattle.Plugin");var runner=plugin.GetField("Runner",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)??throw new MissingFieldException(plugin.FullName,"Runner");
        Require(runner.GetValue(null)==null,"test-menu creates no automatic BattleRunner");
        var methods=AccessTools.GetDeclaredMethods(typeof(SaveManager)).Where(m=>m.Name=="SaveConfig").ToArray();
        Require(methods.Length>0,"native SaveConfig methods found");
        var patches=methods.Select(m=>new{method=m.ToString(),owners=Harmony.GetPatchInfo(m)?.Prefixes.Select(p=>p.owner).ToArray()??Array.Empty<string>()}).ToArray();
        Save("test-menu-session-guards.json",JsonSerializer.Serialize(new{flag=TestMenu,runnerAbsent=runner.GetValue(null)==null,saveConfig=patches}));
        Require(patches.All(p=>p.owners.Contains("local.azerothmegabonk.testbattle")),"test-menu native SaveConfig has active testbattle save-guard owner");
    }
    [HideFromIl2Cpp] void CheckTestMenuClass(JsonElement state,string id)
    {
        const string bridge="MageTalents.TalentBridge";
        Require((string)Read(bridge,"ClassId")==id,"test-menu talent catalog follows native class "+id);
        Require((int)Read(bridge,"TemporaryPointBudget")==999,"test-menu temporary999point budget "+id);
        var catalog=Json(Read(bridge,"CatalogJson"));var allocation=Json(Read(bridge,"StateJson"));var nodes=Prop(catalog,"Talents").EnumerateArray().ToArray();var ranks=Prop(allocation,"Ranks");
        Require(Prop(catalog,"ClassId").GetString()==id&&Prop(allocation,"ClassId").GetString()==id,"test-menu catalog/state source class identity "+id);
        Require(nodes.Length==(id is "warrior" or "paladin" or "druid"?40:39),"test-menu original node count "+id);
        Require(nodes.All(n=>ranks.TryGetProperty(Prop(n,"Id").GetString(),out var rank)&&rank.GetInt32()==Prop(n,"MaxRank").GetInt32()),"test-menu all source talent ranks maxed "+id);
        Require((int)Read(bridge,"SpentPoints")==nodes.Sum(n=>Prop(n,"MaxRank").GetInt32()),"test-menu actual source spent maximum "+id);
        Save(id+"-test-menu-talents.json",JsonSerializer.Serialize(new{catalog,state=allocation,budget=Read(bridge,"TemporaryPointBudget"),spent=Read(bridge,"SpentPoints"),available=Read(bridge,"AvailablePoints")}));
        var cosmetics=Prop(state,"NativeCosmetics");var masked=Prop(cosmetics,"maskedObjects").EnumerateArray().ToArray();var disabled=Prop(cosmetics,"disabledBehaviours").EnumerateArray().ToArray();
        Require(masked.Length>0&&masked.All(m=>!Prop(m,"currentActive").GetBoolean()),"test-menu exact native cosmetic objects hidden "+id);
        Require(disabled.Length>0&&disabled.All(m=>!Prop(m,"currentEnabled").GetBoolean()),"test-menu native cosmetic behaviours disabled "+id);
        var confirms=Prop(cosmetics,"confirms").EnumerateArray().ToArray();
        Require(confirms.Length>0&&confirms.All(c=>Prop(c,"present").GetBoolean()&&Prop(c,"active").GetBoolean()&&Prop(c,"enabled").GetBoolean()),"test-menu native confirm controls remain active "+id);
        var menu=Resources.FindObjectsOfTypeAll<CharacterMenu>().First(m=>m!=null&&m.gameObject.scene.IsValid()&&m.gameObject.activeInHierarchy);
        var protectedPaths=new List<string>();
        if(menu.b_confirm!=null)protectedPaths.Add(UiPath(menu.b_confirm.transform));if(menu.b_back!=null)protectedPaths.Add(UiPath(menu.b_back.transform));if(menu.b_purchase!=null)protectedPaths.Add(UiPath(menu.b_purchase.transform));if(menu.characterGridParent!=null)protectedPaths.Add(UiPath(menu.characterGridParent));
        foreach(var info in Resources.FindObjectsOfTypeAll<CharacterInfoUI>())if(info!=null&&info.characterMenu==menu)
        {if(info.t_rank!=null)protectedPaths.Add(UiPath(info.t_rank.transform));if(info.t_runs!=null)protectedPaths.Add(UiPath(info.t_runs.transform));if(info.progressBar!=null)protectedPaths.Add(UiPath(info.progressBar.transform));if(info.i_character!=null)protectedPaths.Add(UiPath(info.i_character.transform));}
        Require(masked.All(m=>{string path=Prop(m,"path").GetString();return protectedPaths.All(p=>p!=path&&!p.StartsWith(path+"/",StringComparison.Ordinal));}),"test-menu mask excludes confirm/grid/identity/progress ancestors "+id);
        // Guarded shared roots can be empty in a fully separate native layout. Keep
        // their exact diagnostics, rather than manufacturing a required count.
        Save(id+"-test-menu-cosmetics.json",JsonSerializer.Serialize(new{cosmetics,protectedPaths,guardedRoots=Prop(cosmetics,"guardedSharedRoots")}));
        CheckTestMenuSessionGuard();
    }
    [HideFromIl2Cpp] static Type Api()=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHeroes.HeroProbeApi",false)).First(t=>t!=null);
    [HideFromIl2Cpp] static void Act(string action)=>Api().GetMethod("BatchProbe").Invoke(null,new object[]{action});
    [HideFromIl2Cpp] static JsonElement State(){using var doc=JsonDocument.Parse((string)Api().GetProperty("SnapshotJson").GetValue(null));return doc.RootElement.Clone();}
    [HideFromIl2Cpp] void Require(bool success,string message){if(!success)throw new InvalidOperationException(message);checks.Add(new{pass=true,message});Plugin.Note("HERO MENU PASS "+message);}
    [HideFromIl2Cpp] void Save(string name,string text)=>File.WriteAllText(Path.Combine(output,name),text);
    [HideFromIl2Cpp] void Finish(bool pass,Exception error){done=true;Save("summary.json",JsonSerializer.Serialize(new{pass,phase,index,checks,error=error?.ToString(),seconds=Time.realtimeSinceStartup-began}));Plugin.Note("HERO MENU "+(pass?"PASS":"FAIL "+error));Application.Quit(pass?0:2);}
}

[HarmonyPatch]
static class HeroMenuSaveGuard
{
    static bool Prepare()=>Plugin.HeroMenuSmoke;
    static IEnumerable<MethodBase> TargetMethods()=>AccessTools.GetDeclaredMethods(typeof(SaveManager)).Where(m=>new[]{"SaveConfig","SaveStats","SaveProgression","SaveControllers","SaveTemp"}.Contains(m.Name));
    static bool Prefix()=>false;
}
