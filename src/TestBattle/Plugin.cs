using System.Reflection;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Items;
using Assets.Scripts._Data.MapsAndStages;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Managers;
using EEnemy = Actors.Enemies.EEnemy;
using Assets.Scripts.Menu.Shop;
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
                if(Plugin.Smoke && Environment.GetCommandLineArgs().Contains("-azeroth-talents-max"))ProbeStartupMax();
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
                if(Plugin.Smoke)Plugin.Note("Native crit stats: chance="+PlayerStats.GetStat(EStat.CritChance)+" damage="+PlayerStats.GetStat(EStat.CritDamage));
                if(Plugin.Smoke)Plugin.Note("Available shaders="+string.Join(";",Resources.FindObjectsOfTypeAll<Shader>().Select(s=>s.name).OrderBy(s=>s)));
                if(!Plugin.Smoke){stage=4;enabled=false;}return;
            }
            if(stage==3)
            {
                ticks++;
                if(ticks==1 && Environment.GetEnvironmentVariable("AZEROTH_LAB_UI_PROBE")=="1")
                {
                    var items=MyPlayer.Instance.inventory.itemInventory;
                    items.AddItem(EItem.Oats);items.AddItem(EItem.BeefyRing);items.AddItem(EItem.Battery);
                    Plugin.Note("UI probe native items granted through ItemInventory: Oats, BeefyRing, Battery");
                }
                if(ticks==2 && Environment.GetEnvironmentVariable("AZEROTH_LAB_TALENT_PROBE")=="1")ProbeBudget();
                if(ticks==6 && Environment.GetEnvironmentVariable("AZEROTH_LAB_ENEMY_SCALE_PROBE")=="1")ProbeEnemySizes();
                if(ticks==3)
                {
                    var health=MyPlayer.Instance.inventory.playerHealth;health.hp=Math.Max(1,health.maxHp/2);
                    Plugin.Note("HP frame probe current/max="+health.hp+"/"+health.maxHp);
                    var learn=bridge.GetMethod("LearnSpellForProbe");if(learn!=null)Plugin.Note("Learn IceBarrier for live shield row="+learn.Invoke(null,new object[]{"ice_barrier"}));
                }
                if(ticks==12 && bridge.GetMethod("LearnSpellForProbe") is MethodInfo learnAll)
                {
                    foreach(var id in new[]{"fireball","frostbolt","arcane_missiles","frost_nova","arcane_explosion","blizzard","flamestrike","fire_blast","cone_of_cold","blast_wave","pyroblast","ice_barrier","polymorph"})learnAll.Invoke(null,new object[]{id});
                    Plugin.Note("Batch-only complete Mage spellset exercised; learned="+((string[])bridge.GetProperty("SpellIds").GetValue(null)).Length);
                }
                if(ticks==14 && Environment.GetEnvironmentVariable("AZEROTH_LAB_TALENT_PROBE")=="1")ProbeTalents();
                if(ticks==21 && Environment.GetEnvironmentVariable("AZEROTH_LAB_TALENT_PROBE")=="1")ProbeAllTalents();
                if(ticks==29 && Environment.GetEnvironmentVariable("AZEROTH_LAB_TALENT_PROBE")=="1")CaptureCanvas("mage-full-debug-build.ppm");
                if(LevelupScreen.isLevelingUp)
                {
                    var upgrade=Object.FindObjectsOfType<UpgradeButton>().FirstOrDefault(b=>b.gameObject.activeInHierarchy);
                    if(upgrade!=null){upgrade.SelectUpgrade();Plugin.Note("Batch-only native level-up choice applied");}
                }
                var enemies=Object.FindObjectsOfType<Enemy>();
                Plugin.Note("Combat tick="+ticks+" nativeTime="+GameManager.Instance.GetAliveTime()+" enemies="+enemies.Length+" casts="+bridge.GetProperty("CastCount").GetValue(null)+" hits="+bridge.GetProperty("HitCount").GetValue(null)+" slots="+InventoryUtility.GetNumAvailableTomeSlots());
                if(ticks==5){GC.Collect();GC.WaitForPendingFinalizers();Plugin.Note("Managed GC exercised after texture/UI creation");}
                if(!captured && ticks>=8){CaptureCanvas();captured=true;}
                if(ticks==17)CaptureCanvas("mage-all-spells-canvas.ppm");
                if(ticks==19 && Environment.GetEnvironmentVariable("AZEROTH_LAB_TALENT_PROBE")=="1")
                {
                    var talent=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.TalentBridge",false)).First(t=>t!=null);
                    bool shown=(bool)talent.GetMethod("SetPanelVisibleForProbe").Invoke(null,new object[]{true});
                    if(!shown)throw new InvalidOperationException("Talent tree panel did not open");
                    CaptureCanvas("mage-talents-panel.ppm");talent.GetMethod("SetPanelVisibleForProbe").Invoke(null,new object[]{false});Plugin.Note("Native talent panel rendered and closed without desktop input");
                }
                if(Time.realtimeSinceStartup-runBegan>=40)
                {
                    if(errors>0)throw new InvalidOperationException("Runtime probe recorded errors; refusing success");
                    if(!captured)throw new InvalidOperationException("No complete Azeroth HUD render captured");
                    if((int)bridge.GetProperty("HitCount").GetValue(null)<1)throw new InvalidOperationException("No Mage hits observed");
                    if(InventoryUtility.GetNumAvailableTomeSlots()!=4)throw new InvalidOperationException("Expected four tome slots");
                    if(Environment.GetEnvironmentVariable("AZEROTH_LAB_TALENT_PROBE")=="1")
                    {
                        var elemental=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.WaterElemental",false)).First(t=>t!=null);
                        int shots=(int)elemental.GetProperty("ShotCount").GetValue(null);Plugin.Note("Water Elemental source attacks="+shots+" lastDamage="+elemental.GetProperty("LastDamage").GetValue(null));
                        if(shots<1)throw new InvalidOperationException("Water Elemental attack was not exercised");
                        var runtime=elemental.Assembly.GetType("MageTalents.TalentRuntime");
                        Plugin.Note("Talent runtime procs: "+string.Join(",",new[]{"EchoCount","FreeCastCount","IgniteTickCount","ControlCount","ChainCount","SplitCount","ExplosionCount"}.Select(n=>n+"="+runtime.GetProperty(n)?.GetValue(null))));
                    }
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
    void ProbeEnemySizes()
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge",false)).First(t=>t!=null);
        var describe=type.GetMethod("Describe");
        var regular=Object.FindObjectsOfType<Enemy>().FirstOrDefault(e=>e.hp>0 && e.gameObject.activeInHierarchy && !e.IsBoss()&&!e.IsStageBoss()&&!e.IsFinalBoss());
        if(regular==null)throw new InvalidOperationException("No native regular enemy for scale probe");
        CheckEnemySize((string)describe.Invoke(null,new object[]{regular}),false);
        var position=MyPlayer.Instance.transform.position;
        foreach(var id in new[]{25,34,50})
        {
            var flag=id==34?EEnemyFlag.StageBoss:EEnemyFlag.Boss;
            var spawned=EnemyManager.Instance.SpawnBoss((EEnemy)id,-1,flag,position+new Vector3(80+(id%3)*12,0,80),1);
            if(spawned==null)throw new InvalidOperationException("Native boss spawn failed "+id);
            CheckEnemySize((string)describe.Invoke(null,new object[]{spawned}),true);
        }
        Plugin.Note("PASS native enemy size probe: regular2/3; Golem/Pharaoh/CalciumDad original Azeroth models at native Megabonk boss height");
    }
    [HideFromIl2Cpp]
    void CheckEnemySize(string description,bool boss)
    {
        Plugin.Note("Enemy size probe "+description);
        using var doc=System.Text.Json.JsonDocument.Parse(description);var data=doc.RootElement;
        float ratio=data.GetProperty("sizeMultiplier").GetSingle(), expected=data.GetProperty("expectedInitialHeight").GetSingle(),actual=data.GetProperty("renderedHeight").GetSingle();
        if(data.GetProperty("boss").GetBoolean()!=boss || Math.Abs(ratio-(boss?1f:2f/3f))>.001f || Math.Abs(actual-expected)>Math.Max(.02f,expected*.01f) || !data.GetProperty("nativeRendererHidden").GetBoolean())
            throw new InvalidOperationException("Azeroth enemy sizing/replacement mismatch "+description);
    }
    [HideFromIl2Cpp]
    void ProbeStartupMax()
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.TalentBridge",false)).First(t=>t!=null);
        int spent=(int)type.GetProperty("SpentPoints").GetValue(null),free=(int)type.GetProperty("AvailablePoints").GetValue(null);
        if(spent!=121 || free!=878)throw new InvalidOperationException($"Startup maxed build mismatch: spent={spent},free={free}");
        Plugin.Note("PASS startup all39 talents BEFORE battle: spent121,free878,budget999");
    }
    [HideFromIl2Cpp]
    void ProbeTalents()
    {
        var talents=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.TalentBridge",false)).FirstOrDefault(t=>t!=null);
        if(talents==null)throw new InvalidOperationException("Mage talent plugin missing");
        const string ranks="{\"arcane_focus\":5,\"arcane_subtlety\":5,\"imp_arcane_explosion\":3,\"magic_absorption\":5,\"arcane_concentration\":1,\"arcane_meditation\":3,\"arcane_mind\":5,\"imp_blink\":3,\"presence_of_mind\":1,\"arcane_instability\":3,\"arcane_fortitude\":3,\"arcane_reach\":3,\"arcane_power\":1}";
        bool accepted=(bool)talents.GetMethod("SetProbeState").Invoke(null,new object[]{23205L,ranks});
        if(!accepted)throw new InvalidOperationException("Source Arcane talent allocation rejected");
        var runtime=bridge.Assembly.GetType("AzerothAbilities.AbilityRuntime");runtime.GetMethod("ResetRun").Invoke(null,null);
        var starters=(string[])bridge.GetProperty("SpellIds").GetValue(null);
        Plugin.Note("Arcane talent probe snapshot: spent="+talents.GetProperty("SpentPoints").GetValue(null)+" starter="+string.Join(",",starters)+" crit="+PlayerStats.GetStat(EStat.CritChance));
        if(!starters.Contains("arcane_missiles"))throw new InvalidOperationException("Arcane capstone did not replace starter");
        foreach(var id in new[]{"fireball","frostbolt","arcane_missiles","frost_nova","arcane_explosion","blizzard","flamestrike","fire_blast","cone_of_cold","blast_wave","pyroblast","ice_barrier","polymorph"})bridge.GetMethod("LearnSpellForProbe").Invoke(null,new object[]{id});
    }
    [HideFromIl2Cpp]
    void ProbeBudget()
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.TalentBridge",false)).First(t=>t!=null);
        if(Environment.GetCommandLineArgs().Contains("-azeroth-talents-max")){ProbeStartupMax();return;}
        int before=(int)type.GetProperty("AvailablePoints").GetValue(null);if(before!=999)throw new InvalidOperationException("Expected 999 free test talent points; got "+before);
        var ranks=(int[])bridge.GetProperty("SpellRanks").GetValue(null);
        if(!(bool)type.GetMethod("Spend").Invoke(null,new object[]{"arcane_focus"}))throw new InvalidOperationException("Test talent could not be purchased");
        int after=(int)type.GetProperty("AvailablePoints").GetValue(null);if(after!=998)throw new InvalidOperationException("Talent purchase did not debit one point");
        if(!(bool)type.GetMethod("Refund").Invoke(null,new object[]{"arcane_focus"}))throw new InvalidOperationException("Test talent could not be refunded");
        if((int)type.GetProperty("AvailablePoints").GetValue(null)!=999)throw new InvalidOperationException("Talent refund did not restore999");
        if(!ranks.SequenceEqual((int[])bridge.GetProperty("SpellRanks").GetValue(null)))throw new InvalidOperationException("Live test talent application changed learned spell ranks");
        Plugin.Note("PASS999 test points: spend999->998, live application, refund998->999, spell ranks retained");
    }
    [HideFromIl2Cpp]
    void ProbeAllTalents()
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("MageTalents.TalentBridge",false)).First(t=>t!=null);
        using var catalog=System.Text.Json.JsonDocument.Parse((string)type.GetProperty("CatalogJson").GetValue(null));
        var nodes=catalog.RootElement.GetProperty("Talents");var all=new Dictionary<string,int>();
        foreach(var node in nodes.EnumerateArray())all[node.GetProperty("Id").GetString()]=node.GetProperty("MaxRank").GetInt32();
        bool accepted=(bool)type.GetMethod("SetProbeState").Invoke(null,new object[]{23205L,System.Text.Json.JsonSerializer.Serialize(all)});
        if(!accepted)throw new InvalidOperationException("999-point complete Mage test build rejected");
        bridge.Assembly.GetType("AzerothAbilities.AbilityRuntime").GetMethod("ResetRun").Invoke(null,null);
        foreach(var id in new[]{"fireball","frostbolt","arcane_missiles","frost_nova","arcane_explosion","blizzard","flamestrike","fire_blast","cone_of_cold","blast_wave","pyroblast","ice_barrier","polymorph"})bridge.GetMethod("LearnSpellForProbe").Invoke(null,new object[]{id});
        Plugin.Note("999-point debug build: nodes="+all.Count+" ranks="+all.Values.Sum()+" remaining="+type.GetProperty("AvailablePoints").GetValue(null));
    }
    [HideFromIl2Cpp]
    void CaptureCanvas(string fileName="mage-battle-canvas.ppm")
    {
        var hud=GameObject.Find("AzerothHudCanvas");
        if(hud==null || !hud.activeInHierarchy)throw new InvalidOperationException("Azeroth gameplay HUD missing");
        var hudImages=Object.FindObjectsOfType<RawImage>().Count(i=>i.transform.IsChildOf(hud.transform));
        if(hudImages<12)throw new InvalidOperationException("Azeroth HUD incomplete; image nodes="+hudImages);
        Plugin.Note("Native Azeroth HUD active: imageNodes="+hudImages+" totalNodes="+hud.transform.childCount);
        if(Environment.GetEnvironmentVariable("AZEROTH_LAB_UI_PROBE")=="1")
        {
            var timer=hud.transform.Find("RunTime").GetComponent<TMPro.TextMeshProUGUI>();
            var plate=hud.transform.Find("ActionBarBackground").GetComponent<RawImage>();
            if(timer.fontSize<98 || plate.texture==null || hud.transform.Find("ActionBarTopRail")==null)
                throw new InvalidOperationException("Restored action bar artwork or 3.5x timer missing");
            var itemLayout=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHud.NativeItemLayoutDiagnostics",false)).First(t=>t!=null);
            int count=(int)itemLayout.GetProperty("VisibleItemCount").GetValue(null);
            float lowest=(float)itemLayout.GetProperty("LowestVisibleItemScreenY").GetValue(null);
            float inset=(float)itemLayout.GetProperty("BottomInsetScreenPixels").GetValue(null);
            Plugin.Note($"UI layout probe: timer={timer.fontSize}/28=3.5x; source backing={plate.texture.name}; items={count}; lowestY={lowest}; inset={inset}");
            if(Environment.GetEnvironmentVariable("AZEROTH_LAB_UI_TRACE")!="1" && (count<3 || lowest+1<inset))throw new InvalidOperationException("Native items still obscured by bottom action bar");
        }
        var modelBridge=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothMegabonk.MageVisuals.MageVisualBridge",false)).FirstOrDefault(t=>t!=null);
        if(modelBridge!=null)
        {
            bool attached=(bool)modelBridge.GetProperty("IsAttached").GetValue(null);
            bool portrait=modelBridge.GetProperty("PortraitTexture").GetValue(null)!=null;
            Plugin.Note("Source Mage model attached="+attached+" portrait="+portrait+" clip="+modelBridge.GetProperty("CurrentClip").GetValue(null));
            if(!attached || !portrait)throw new InvalidOperationException("Source Mage model or portrait missing");
            if(Environment.GetEnvironmentVariable("AZEROTH_LAB_UI_PROBE")=="1")
            {
                bool closed=(bool)modelBridge.GetProperty("SourceHeadClosed").GetValue(null);int vertices=(int)modelBridge.GetProperty("SourceVertexCount").GetValue(null);
                Plugin.Note($"Mage original scalp restored: headClosed={closed},vertices={vertices}");
                if(!closed || vertices!=810)throw new InvalidOperationException("Corrected original Mage head pack not active");
            }
        }
        if(bridge.GetProperty("SourceVfxSpawned")!=null)
        {
            int spawned=(int)bridge.GetProperty("SourceVfxSpawned").GetValue(null);
            Plugin.Note("Source Mage VFX instances="+spawned+" live="+bridge.GetProperty("SourceVfxLive").GetValue(null));
            if(spawned<1)throw new InvalidOperationException("Source Mage VFX not exercised");
        }
        var hudBridge=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHud.HudDiagnostics",false)).FirstOrDefault(t=>t!=null);
        if(hudBridge!=null)
        {
            Plugin.Note("Native HUD graphic nodes masked="+hudBridge.GetProperty("NativeGraphicsHidden")?.GetValue(null));
            var world=hudBridge.GetProperty("PlayerWorldHealthGraphicsHidden");
            if(world!=null){int count=(int)world.GetValue(null);Plugin.Note("Native player world HP graphics hidden="+count);if(count<1)throw new InvalidOperationException("Native player world health bar still visible");}
        }
        foreach(var inventory in Object.FindObjectsOfType<InventoryHud>())
        {
            foreach(var propName in new[]{"weaponParent","tomeParent"})
            {
                var parent=(Component)inventory.GetType().GetProperty(propName)?.GetValue(inventory);
                if(parent==null)continue;
                var graphics=Resources.FindObjectsOfTypeAll<Graphic>().Where(g=>g.transform.IsChildOf(parent.transform)).ToArray();
                var shown=graphics.Count(g=>g.enabled && g.gameObject.activeInHierarchy);
                Plugin.Note("Native inventory "+propName+" graphics="+graphics.Length+" shown="+shown);
                if(shown>0)throw new InvalidOperationException("Native inventory grid remains visible: "+propName);
            }
        }
        if(fileName=="mage-battle-canvas.ppm")DescribeItems("normal-screen");
        var camera=Camera.main;
        if(camera==null)throw new InvalidOperationException("Native battle camera missing");
        var canvases=Object.FindObjectsOfType<Canvas>().Where(c=>c.isRootCanvas && c.isActiveAndEnabled).ToArray();
        var states=canvases.Select(c=>(Canvas:c,Mode:c.renderMode,Camera:c.worldCamera,Distance:c.planeDistance)).ToArray();
        var previousTarget=camera.targetTexture;var previousActive=RenderTexture.active;int previousMask=camera.cullingMask;
        const int width=960,height=540;
        var rt=new RenderTexture(width,height,24);rt.Create();
        Texture2D image=null;
        try
        {
            camera.targetTexture=rt;
            // Native overlay UI bypasses the world camera mask. Include its layers only
            // for this offscreen camera conversion, then restore the real gameplay mask.
            foreach(var graphic in Resources.FindObjectsOfTypeAll<Graphic>())
                if(graphic!=null && graphic.gameObject.activeInHierarchy)camera.cullingMask|=1<<graphic.gameObject.layer;
            Plugin.Note($"Capture camera culling mask {previousMask}->{camera.cullingMask}");
            foreach(var canvas in canvases)if(canvas.renderMode==RenderMode.ScreenSpaceOverlay){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
            Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;
            if(fileName=="mage-battle-canvas.ppm")DescribeItems("capture-rendered");
            image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();
            var path=Path.Combine(Path.GetDirectoryName(Plugin.Report),fileName);
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
            camera.cullingMask=previousMask;camera.targetTexture=previousTarget;RenderTexture.active=previousActive;rt.Release();Object.Destroy(rt);if(image!=null)Object.Destroy(image);
        }
    }
    [HideFromIl2Cpp]
    void DescribeItems(string stage)
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHud.NativeItemLayoutDiagnostics",false)).FirstOrDefault(t=>t!=null);
        var description=type?.GetMethod("DescribeItems")?.Invoke(null,null) as string[];
        if(description!=null)foreach(var line in description)Plugin.Note("Items "+stage+": "+line);
    }
    void OnApplicationQuit(){Plugin.Note("Unity quit stage="+stage+" combatTicks="+ticks);}
}
