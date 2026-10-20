using System.Reflection;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Inventory__Items__Pickups;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AzerothMegabonk.LabSmoke;

[BepInPlugin("local.azerothmegabonk.labsmoke","Azeroth Megabonk Lab Smoke","0.1.0")]
public sealed class Plugin:BasePlugin
{
    internal static string Report;
    internal static bool Enabled;
    public override void Load()
    {
        Enabled=Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1";
        if(!Enabled) { Log.LogInfo("Lab smoke inactive; normal game session."); return; }
        Report=Path.GetFullPath(Path.Combine(Paths.GameRootPath,"../reports/headless-smoke.log"));
        File.WriteAllText(Report,DateTimeOffset.Now+" Headless smoke armed; no OS input.\n");
        new Harmony("local.azerothmegabonk.labsmoke").PatchAll(typeof(Plugin).Assembly);
        AddComponent<Runner>();
    }
    internal static void Note(string text){File.AppendAllText(Report,DateTimeOffset.Now+" "+text+"\n");}
}

[HarmonyPatch]
internal static class TestSaveGuard
{
    static IEnumerable<MethodBase> TargetMethods()=>AccessTools.GetDeclaredMethods(typeof(SaveManager)).Where(m=>new[]{"SaveConfig","SaveStats","SaveProgression","SaveControllers","SaveTemp"}.Contains(m.Name)).Cast<MethodBase>();
    static bool Prefix(){Plugin.Note("Test-only save write suppressed");return false;}
}

public sealed class Runner:MonoBehaviour
{
    public Runner(IntPtr p):base(p){}
    int stage, ticks; float next, began; bool reported;
    void Start()
    {
        began=Time.realtimeSinceStartup;
        Plugin.Note("Unity isBatchMode="+Application.isBatchMode+" graphics="+SystemInfo.graphicsDeviceType);
        if(!Application.isBatchMode){Plugin.Note("ABORT: headless test requires batchmode");Application.Quit();return;}
        Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
        next=Time.realtimeSinceStartup+10;
    }
    void Update()
    {
        if(Time.realtimeSinceStartup-began>110){Plugin.Note("TIMEOUT stage="+stage);Application.Quit();return;}
        if(Time.realtimeSinceStartup<next)return;
        next=Time.realtimeSinceStartup+2;
        try
        {
            if(stage==0)
            {
                var menu=Object.FindObjectOfType<MainMenu>();
                if(menu==null){Plugin.Note("Waiting for native MainMenu");return;}
                Plugin.Note("Tome slot call="+InventoryUtility.GetNumAvailableTomeSlots());
                menu.GoToMapSelection();stage=1;Plugin.Note("Called native MainMenu.GoToMapSelection");return;
            }
            if(stage==1)
            {
                var maps=Resources.FindObjectsOfTypeAll<MapSelectionUi>();
                if(maps.Length==0){Plugin.Note("Waiting for native MapSelectionUi");return;}
                var map=maps[0];
                if(map.runConfig==null)map.Init();
                if(map.runConfig==null || map.runConfig.mapData==null){Plugin.Note("Map config not initialized");return;}
                Plugin.Note("Starting native map="+map.runConfig.mapData.name+", tier="+map.runConfig.mapTierIndex);
                map.StartMap();stage=2;next=Time.realtimeSinceStartup+10;return;
            }
            if(stage==2)
            {
                var player=MyPlayer.Instance;var game=GameManager.Instance;
                if(player==null || player.inventory==null || game==null){Plugin.Note("Waiting player/manager");return;}
                var bridge=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothAbilities.HudBridge",false)).FirstOrDefault(t=>t!=null);
                if(bridge!=null) Plugin.Note("Select original Mage="+bridge.GetMethod("SelectClass")?.Invoke(null,new object[]{"mage"}));
                player.hasStarted=true;game.StartPlaying();stage=3;
                Plugin.Note("Native run started; tome slots="+InventoryUtility.GetNumAvailableTomeSlots()+", HP="+player.inventory.playerHealth.hp);
                return;
            }
            if(stage==3)
            {
                var enemies=Object.FindObjectsOfType<Enemy>();
                ticks++;
                Plugin.Note("Run tick="+ticks+" enemies="+enemies.Length+" aliveTime="+GameManager.Instance.GetAliveTime()+" slots="+InventoryUtility.GetNumAvailableTomeSlots());
                if(ticks==3) ProbeUpgrade();
                if(enemies.Length>0 && !reported)
                {
                    reported=true;var e=enemies[0];
                    Plugin.Note("Native spawned enemy="+e.enemyData.enemyName+" hp="+e.hp+" collider="+(e.collider!=null)+" visuals="+e.GetComponentsInChildren<MeshRenderer>().Length);
                    CaptureCreature(e);
                }
                if(ticks>=8){Plugin.Note("COMPLETE: headless native run probe; visual fidelity still requires rendered evidence");Application.Quit();}
            }
        }
        catch(Exception ex){Plugin.Note("ERROR stage="+stage+" "+ex);if(++ticks>3)Application.Quit();}
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    void ProbeUpgrade()
    {
        var bridge=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothAbilities.HudBridge",false)).FirstOrDefault(t=>t!=null);
        if(bridge==null)return;
        string Summary()=>"rank="+string.Join(",",(int[])bridge.GetProperty("SpellRanks").GetValue(null))+",damage="+string.Join(",",(float[])bridge.GetProperty("SourceDamages").GetValue(null))+",cooldown="+string.Join(",",(float[])bridge.GetProperty("SourceCooldowns").GetValue(null));
        Plugin.Note("Before original upgrade: "+Summary());
        Plugin.Note("Apply source common damage upgrade="+bridge.GetMethod("ApplyUpgradeForProbe").Invoke(null,new object[]{"fireball",0,0}));
        Plugin.Note("After original upgrade: "+Summary()+", native tome slots="+InventoryUtility.GetNumAvailableTomeSlots());
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    void CaptureCreature(Enemy enemy)
    {
        var go=new GameObject("LabEvidenceCamera");
        var cam=go.AddComponent<Camera>();
        cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.15f,.18f,1);
        cam.fieldOfView=40;cam.nearClipPlane=.1f;cam.farClipPlane=200;
        var center=enemy.collider!=null?enemy.collider.transform.TransformPoint(enemy.collider.center):enemy.transform.position+Vector3.up*1f;
        foreach(var renderer in enemy.GetComponentsInChildren<Renderer>()) Plugin.Note($"Renderer evidence name={renderer.name},enabled={renderer.enabled},bounds={renderer.bounds.center}/{renderer.bounds.size},layer={renderer.gameObject.layer}");
        float subjectHeight=enemy.collider!=null?Math.Max(1,enemy.collider.height*Math.Abs(enemy.collider.transform.lossyScale.y)):2;
        cam.transform.position=center+new Vector3(1.3f,.65f,1.3f)*subjectHeight;cam.transform.LookAt(center);
        const int size=384;
        var rt=new RenderTexture(size,size,24);rt.Create();cam.targetTexture=rt;
        var previous=RenderTexture.active;
        try
        {
            cam.Render();RenderTexture.active=rt;
            var image=new Texture2D(size,size,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,size,size),0,0);image.Apply();
            string path=Path.Combine(Path.GetDirectoryName(Plugin.Report),"headless-enemy-render.ppm");
            using(var stream=File.Create(path))
            {
                var header=System.Text.Encoding.ASCII.GetBytes($"P6\n{size} {size}\n255\n");stream.Write(header);
                for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++)
                {
                    var pixel=image.GetPixel(x,y);
                    stream.WriteByte((byte)Mathf.Clamp(pixel.r*255,0,255));
                    stream.WriteByte((byte)Mathf.Clamp(pixel.g*255,0,255));
                    stream.WriteByte((byte)Mathf.Clamp(pixel.b*255,0,255));
                }
            }
            Plugin.Note("Offscreen camera render saved "+path);
            Object.Destroy(image);
        }
        finally { RenderTexture.active=previous;cam.targetTexture=null;rt.Release();Object.Destroy(rt);Object.Destroy(go); }
    }
    void OnApplicationQuit(){Plugin.Note("Unity quit observed; runner stage="+stage+", ticks="+ticks);}
}

