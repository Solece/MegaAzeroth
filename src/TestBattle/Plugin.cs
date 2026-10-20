using System.Reflection;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts._Data.MapsAndStages;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AzerothMegabonk.TestBattle;

[BepInPlugin("local.azerothmegabonk.testbattle","Azeroth Mage Test Battle","0.1.0")]
public sealed class Plugin:BasePlugin
{
    internal static string Report;
    internal static bool Smoke;
    public override void Load()
    {
        var args=Environment.GetCommandLineArgs();
        if(!args.Contains("-azeroth-test-battle"))return;
        Smoke=args.Contains("-azeroth-test-smoke");
        if(Smoke && !Application.isBatchMode){Log.LogError("Background smoke requires -batchmode; auto-start cancelled.");return;}
        Report=Path.GetFullPath(Path.Combine(Paths.GameRootPath,Smoke?"../reports/mage-battle-smoke.log":"../reports/mage-battle-autostart-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log"));
        File.WriteAllText(Report,DateTimeOffset.Now+" Test battle armed; Mage + Desert, native movement/spawns, no progression save writes.\n");
        new Harmony("local.azerothmegabonk.testbattle").PatchAll(typeof(Plugin).Assembly);
        AddComponent<BattleRunner>();
        Log.LogInfo("Mage test battle auto-start enabled. Native menus remain available in the Menu launcher.");
    }
    internal static void Note(string value)=>File.AppendAllText(Report,DateTimeOffset.Now+" "+value+"\n");
}

[HarmonyPatch]
internal static class TestSaveGuard
{
    private static readonly HashSet<string> reported=new();
    static IEnumerable<MethodBase> TargetMethods()=>AccessTools.GetDeclaredMethods(typeof(SaveManager)).Where(m=>new[]{"SaveConfig","SaveStats","SaveProgression","SaveControllers","SaveTemp"}.Contains(m.Name)).Cast<MethodBase>();
    static bool Prefix(MethodBase __originalMethod)
    {
        if(reported.Add(__originalMethod.Name))Plugin.Note("Test-session save suppressed: "+__originalMethod.Name);
        return false;
    }
}

public sealed class BattleRunner:MonoBehaviour
{
    public BattleRunner(IntPtr pointer):base(pointer){}
    int stage,ticks,errors;
    float began,next,runBegan;
    bool captured,menuChecked;
    Type bridge;
    void Start()
    {
        began=Time.realtimeSinceStartup;
        next=began+8;
        Plugin.Note("Unity batch="+Application.isBatchMode+" graphics="+SystemInfo.graphicsDeviceType);
        if(Plugin.Smoke){Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;}
    }
    void Update()
    {
        if(stage==4)return;
        if(Time.realtimeSinceStartup-began>100 && stage<3){Fail("Timed out waiting for native battle, stage="+stage);return;}
        if(Time.realtimeSinceStartup<next)return;
        next=Time.realtimeSinceStartup+1;
        try
        {
            bridge??=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothAbilities.HudBridge",false)).FirstOrDefault(t=>t!=null);
            if(stage==0)
            {
                var menu=Object.FindObjectOfType<MainMenu>();
                if(menu==null || bridge==null)return;
                if(!(bool)bridge.GetMethod("SelectClass").Invoke(null,new object[]{"mage"}))throw new InvalidOperationException("Mage selection failed");
                Plugin.Note("Mage selected before map load; tome slots="+InventoryUtility.GetNumAvailableTomeSlots());
                menu.GoToMapSelection();stage=1;next=Time.realtimeSinceStartup+2;return;
            }
            if(stage==1)
            {
                var maps=Resources.FindObjectsOfTypeAll<MapSelectionUi>();
                if(maps.Length==0)return;
                var map=maps[0];
                if(map.runConfig==null)map.Init();
                var desert=DataManager.Instance?.GetMap(EMap.Desert);
                if(map.runConfig==null || desert==null || desert.stages.Length==0)return;
                if(Plugin.Smoke && !menuChecked){CheckMenuAndIcons();menuChecked=true;}
                map.runConfig.mapData=desert;map.runConfig.stageData=desert.stages[0];map.runConfig.mapTierIndex=0;map.runConfig.challenge=null;
                Plugin.Note("Starting native map="+desert.name+" stage="+desert.stages[0].name+" tierIndex=0");
                map.StartMap();stage=2;next=Time.realtimeSinceStartup+8;return;
            }
            if(stage==2)
            {
                var player=MyPlayer.Instance;var game=GameManager.Instance;
                if(player==null || player.inventory==null || game==null)return;
                if(!(bool)bridge.GetProperty("Active").GetValue(null))throw new InvalidOperationException("Mage inactive after native scene load");
                player.hasStarted=true;game.StartPlaying();
                stage=3;runBegan=Time.realtimeSinceStartup;
                Plugin.Note("BATTLE READY: class="+bridge.GetProperty("ClassId").GetValue(null)+"; spells="+string.Join(",",(string[])bridge.GetProperty("SpellIds").GetValue(null))+"; tomeSlots="+InventoryUtility.GetNumAvailableTomeSlots()+"; HP="+player.inventory.playerHealth.hp);
                if(!Plugin.Smoke){stage=4;enabled=false;}return;
            }
            if(stage==3)
            {
                ticks++;
                var enemies=Object.FindObjectsOfType<Enemy>();
                Plugin.Note("Combat tick="+ticks+" nativeTime="+GameManager.Instance.GetAliveTime()+" enemies="+enemies.Length+" casts="+bridge.GetProperty("CastCount").GetValue(null)+" hits="+bridge.GetProperty("HitCount").GetValue(null)+" slots="+InventoryUtility.GetNumAvailableTomeSlots());
                if(ticks==5){GC.Collect();GC.WaitForPendingFinalizers();Plugin.Note("Managed GC exercised after texture/UI creation");}
                if(!captured && ticks>=8){CaptureCanvas();captured=true;}
                if(Time.realtimeSinceStartup-runBegan>=30)
                {
                    if(!captured)throw new InvalidOperationException("No complete Azeroth HUD render captured");
                    if((int)bridge.GetProperty("HitCount").GetValue(null)<1)throw new InvalidOperationException("No Mage hits observed");
                    if(InventoryUtility.GetNumAvailableTomeSlots()!=4)throw new InvalidOperationException("Expected four tome slots");
                    Plugin.Note("PASS: auto-start battle, Mage damage, four tome slots, native Canvas exercised; no desktop window/input.");
                    stage=4;Application.Quit();
                }
            }
        }
        catch(Exception ex){Plugin.Note("ERROR stage="+stage+" "+ex);if(++errors>=3)Fail("Repeated native battle startup failure");}
    }
    [HideFromIl2Cpp]
    void Fail(string reason){Plugin.Note("FAIL: "+reason);stage=4;enabled=false;if(Plugin.Smoke)Application.Quit();}
    [HideFromIl2Cpp]
    void CheckMenuAndIcons()
    {
        var selection=GameObject.Find("AzerothMageSelection");
        Plugin.Note("Mage menu panel active="+(selection!=null && selection.activeInHierarchy));
        if(selection==null)throw new InvalidOperationException("Mage selector did not build");
        var assembly=bridge.Assembly;
        var runtime=assembly.GetType("AzerothAbilities.AbilityRuntime");
        var catalog=runtime.GetField("Catalog",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var spells=(System.Collections.IEnumerable)catalog.GetType().GetProperty("Spells").GetValue(catalog);
        var iconMethod=assembly.GetType("AzerothAbilities.AbilityOffers").GetMethod("Icon",BindingFlags.Static|BindingFlags.NonPublic);
        int count=0;
        foreach(var spell in spells)
        {
            var value=spell.GetType().GetProperty("Value")?.GetValue(spell)??spell;
            var icon=(Texture2D)iconMethod.Invoke(null,new[]{value});
            if(icon==null || icon.width!=64 || icon.height!=64)throw new InvalidOperationException("Original Mage icon upload failed");
            count++;
        }
        Plugin.Note("Original Mage raw RGBA icons loaded="+count);
    }
    [HideFromIl2Cpp]
    void CaptureCanvas()
    {
        var hud=GameObject.Find("AzerothHudCanvas");
        if(hud==null || !hud.activeInHierarchy)throw new InvalidOperationException("Azeroth gameplay HUD missing");
        var hudImages=Object.FindObjectsOfType<RawImage>().Count(i=>i.transform.IsChildOf(hud.transform));
        if(hudImages<50)throw new InvalidOperationException("Azeroth HUD incomplete; image nodes="+hudImages);
        Plugin.Note("Native Azeroth HUD active: imageNodes="+hudImages+" totalNodes="+hud.transform.childCount);
        var camera=Camera.main;
        if(camera==null)throw new InvalidOperationException("Native battle camera missing");
        var canvases=Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas && c.isActiveAndEnabled).ToArray();
        var states=canvases.Select(c=>(Canvas:c,Mode:c.renderMode,Camera:c.worldCamera,Distance:c.planeDistance)).ToArray();
        var previousTarget=camera.targetTexture;var previousActive=RenderTexture.active;
        const int width=960,height=540;
        var rt=new RenderTexture(width,height,24);rt.Create();
        Texture2D image=null;
        try
        {
            camera.targetTexture=rt;
            foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            var path=Path.Combine(Path.GetDirectoryName(Plugin.Report),"mage-battle-canvas.ppm");
            using var stream=File.Create(path);stream.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
            for(int y=height-1;y>=0;y--)for(int x=0;x<width;x++)
            {
                var pixel=image.GetPixel(x,y);stream.WriteByte((byte)Mathf.Clamp(pixel.r*255,0,255));stream.WriteByte((byte)Mathf.Clamp(pixel.g*255,0,255));stream.WriteByte((byte)Mathf.Clamp(pixel.b*255,0,255));
            }
            Plugin.Note("Offscreen native Canvas render saved; canvases="+canvases.Length+" RawImages="+Object.FindObjectsOfType<RawImage>().Length+" path="+path);
        }
        finally
        {
            foreach(var state in states){state.Canvas.renderMode=state.Mode;state.Canvas.worldCamera=state.Camera;state.Canvas.planeDistance=state.Distance;}
            camera.targetTexture=previousTarget;RenderTexture.active=previousActive;rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);
        }
    }
    void OnApplicationQuit(){Plugin.Note("Unity quit stage="+stage+" combatTicks="+ticks);}
}
