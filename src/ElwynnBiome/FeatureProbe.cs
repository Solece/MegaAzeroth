using System.Reflection;
using System.Text.Json;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Items;
using Assets.Scripts.Managers;
using Assets.Scripts.Objects.Pooling;
using Assets.Scripts.UI.InGame.Rewards;
using Assets.Scripts.Utility;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Actors;
using Assets.Scripts.Menu.Shop;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// An explicitly armed integration test. Production simulation modules expose
// read-only evidence or guarded APIs; this runner invokes real native actions.
public sealed class FeatureProbe : MonoBehaviour
{
    public FeatureProbe(IntPtr pointer):base(pointer){}
    const string Tome="AzerothAbilities.TomeCompatibilityDiagnostics";
    const string Hud="AzerothAbilities.HudBridge";
    const string Ui="AzerothHud.HudProbeApi";
    const string EnemyVisual="AzerothMegabonk.EnemyVisuals.EnemyVisualBridge";
    const string MageVisual="AzerothMegabonk.MageVisuals.MageVisualBridge";
    readonly Dictionary<string,Type> types=new();
    readonly List<Step> steps=new();
    readonly List<object> results=new();
    readonly HashSet<uint> durable=new();
    Dictionary<uint,float> launchHp;
    readonly string[] allSpells={"fireball","frostbolt","arcane_missiles","frost_nova","arcane_explosion","blizzard","flamestrike","fire_blast","cone_of_cold","blast_wave","pyroblast","ice_barrier","polymorph"};
    sealed class Step
    {
        internal string Name;internal Action Enter;internal Func<bool> Poll;internal float Delay,Timeout=12;
    }
    sealed class Skip : Exception {internal Skip(string reason):base(reason){}}
    int index,passed,failed,skipped,phase,castMark,createdMark,hitMark,xpMark,qualityAttempts;
    float began,entered,gateAt,firstCastAt,baselineInterval;
    bool started,done,expectNativeWindow,endingRun;
    string output,relativeOutput,originalTalent;
    Enemy golem,other,precisionTarget;
    InteractableShadyGuy merchant;
    GameObject merchantFixture;
    EnemySpawnParticles explicitlyPooledFx;
    JsonElement baseline,sample,pausedBefore,animationBefore,enemyAnimationBefore;
    float baselineNativeStat,baselineValue,nextInputIsolation;
    int baselineQuantity,expectedProjectiles;
    string activePauseCase;

    void Start()
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1" || !Environment.GetCommandLineArgs().Contains("-azeroth-feature-smoke"))
        {enabled=false;return;}
        began=Time.realtimeSinceStartup;Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
        relativeOutput="feature-"+Environment.ProcessId;
        output=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports",relativeOutput));Directory.CreateDirectory(output);
        BuildSteps();Plugin.Note("FEATURE BEGIN: native actions, guarded in-memory fixtures, no OS input; evidence="+output);
    }
    void Update()
    {
        if(done)return;
        if(Time.realtimeSinceStartup>=nextInputIsolation)
        {
            nextInputIsolation=Time.realtimeSinceStartup+1;
            var input=Object.FindObjectOfType<MyInputManager>();
            if(input?.rewiredEventSystem!=null)input.rewiredEventSystem.enabled=false;
        }
        if(Time.realtimeSinceStartup-began>210){Record("FAIL","global-timeout","210 second deadline at "+CurrentName());Finish();return;}
        var player=MyPlayer.Instance;
        if(!endingRun && player?.inventory?.playerHealth!=null)player.inventory.playerHealth.hp=player.inventory.playerHealth.maxHp;
        // Test durability is explicit: preserve native AI/colliders/spawning, but
        // prevent a random missile target dying before subsequent comparisons.
        if(!endingRun && index>0 && EnemyManager.Instance?.enemies!=null)
            foreach(var pair in EnemyManager.Instance.enemies)
                if(pair.Value!=null && !pair.Value.IsDeadOrDyingNextFrame() && durable.Add(pair.Value.id))
                {pair.Value.maxHp=Mathf.Max(pair.Value.maxHp,100000);pair.Value.hp=pair.Value.maxHp;}
        if(index>=steps.Count){Finish();return;}
        var step=steps[index];
        try
        {
            if(!started)
            {
                started=true;entered=Time.realtimeSinceStartup;phase=0;gateAt=entered;
                Plugin.Note("FEATURE ENTER "+step.Name);step.Enter?.Invoke();
            }
            if(!expectNativeWindow && index>0 && LevelupScreen.isLevelingUp){CloseNativeChoice();return;}
            if(Time.realtimeSinceStartup-entered>step.Timeout)throw new TimeoutException(step.Name+" timed out at phase="+phase+"; nativeWindows="+NativeWindows()+"; "+Read(Tome,"SnapshotJson"));
            if(Time.realtimeSinceStartup-entered<step.Delay)return;
            if(step.Poll!=null && !step.Poll())return;
            Record("PASS",step.Name,"verified");index++;started=false;
        }
        catch(Skip skip)
        {Record("SKIP",step.Name,skip.Message);Recover();index++;started=false;}
        catch(Exception error)
        {
            var actual=error is TargetInvocationException invocation && invocation.InnerException!=null?invocation.InnerException:error;
            Record("FAIL",step.Name,actual.ToString());TrySave("failure-"+index+".json",()=>Snapshot().GetRawText());Recover();index++;started=false;
        }
    }

    [HideFromIl2Cpp] void BuildSteps()
    {
        Add("native-battle-ready",null,Ready,0,55);
        Add("retained-wmo-native-facades-and-doors",null,CaptureBuildings,0,12);
        Add("golem-ownership-ground-and-pooled-spawn",null,Ownership,2.5f,12);
        Add("mage-native-motion-source-regression",()=>
        {
            var regression=Json(Call(MageVisual,"RunMotionRegressionForProbe"));Save("mage-motion-regression.json",regression.GetRawText());
            Require(regression.GetProperty("pass").GetBoolean(),"Mage motion regression did not pass");
            Require(Bool(Call(MageVisual,"SetMotionForProbe","slide")),"Slide pose override rejected");
        },()=>
        {
            var state=Json(Call(MageVisual,"MotionDiagnostics"));Save("mage-slide.json",state.GetRawText());
            Require(S(state,"currentClip")=="slide","Rendered Mage pose is not source slide");
            Capture("mage-slide.ppm");Call(MageVisual,"SetMotionForProbe","");return true;
        },.25f);
        Add("source-animation-native-slow-motion",()=>
        {
            MyTime.SetTimeScale(.2f,2);animationBefore=Animation(MageVisual);enemyAnimationBefore=Animation(EnemyVisual);
            Save("animation-slow-before.json",AnimationBundle().GetRawText());
        },()=>
        {
            var mage=Animation(MageVisual);var enemy=Animation(EnemyVisual);
            Require(!mage.GetProperty("clock").GetProperty("canAnimate").GetBoolean(),"Mage animation still enabled in native slow motion");
            Equal(F(animationBefore.GetProperty("clock"),"clock"),F(mage.GetProperty("clock"),"clock"),.00001f,"Mage slow-motion animation clock");
            Equal(F(enemyAnimationBefore.GetProperty("clock"),"clock"),F(enemy.GetProperty("clock"),"clock"),.00001f,"Enemy slow-motion animation clock");
            Save("animation-slow-after.json",AnimationBundle().GetRawText());MyTime.SetTimeScale(1,0);gateAt=Time.realtimeSinceStartup;
            return true;
        },.6f);
        Add("source-animation-resumes-after-slow-motion",()=>{animationBefore=Animation(MageVisual);enemyAnimationBefore=Animation(EnemyVisual);},()=>
        {
            Require(F(Animation(MageVisual).GetProperty("clock"),"clock")>F(animationBefore.GetProperty("clock"),"clock"),"Mage animation did not resume after native slow motion");
            Require(F(Animation(EnemyVisual).GetProperty("clock"),"clock")>F(enemyAnimationBefore.GetProperty("clock"),"clock"),"Enemy animation did not resume after native slow motion");return true;
        },.4f);
        Add("quantity-real-native-upgrades-and-projectile-hits",()=>{ResetAreasAndProjectiles();Isolate("Quantity",0);baseline=Snapshot();BeginQuantityCast();},Quantity,0,18);
        Add("damage-real-native-tome-hit",null,Damage,0,6);
        Add("projectile-speed-native-tome-movement",()=>{ResetAreasAndProjectiles();Isolate("ProjectileSpeed",0);LaunchMovementSample();},ProjectileSpeed,0,8);
        Add("cooldown-native-tome-real-cast-interval",()=>{ResetAreasAndProjectiles();Isolate("Cooldown",0);BeginCooldownInterval();},Cooldown,0,10);
        Add("size-native-tome-projectile-and-ground-area",null,Size,0,6);
        Add("duration-native-tome-ground-area-ticks",null,Duration,0,8);
        Add("precision-native-tome-real-critical-hits",null,Precision,0,7);
        Add("bounce-native-tome-real-ricochets",()=>
        {
            var nativeCatalog=Json(Read(Tome,"NativeTomeCatalogJson"));
            var bounceTome=nativeCatalog.EnumerateArray().FirstOrDefault(t=>S(t,"Stat")=="ProjectileBounces");
            if(bounceTome.ValueKind==JsonValueKind.Undefined)
                throw new Skip("Installed native TomeData catalog has no ProjectileBounces tome; ETome.Bounce enum entry alone is not installed content. See native-tome-catalog.json.");
            ResetAreasAndProjectiles();Isolate(S(bounceTome,"Id"),20,5);baseline=Snapshot();
            launchHp=HitPoints();
            Require(F(baseline.GetProperty("NativeStats"),"ProjectileBounces")>=1,"Native Bounce tome has not reached one real bounce");
            Require(Bool(Call(Tome,"CastSpellForProbe","fireball")),"Fireball bounce cast had no target");
        },()=>
        {
            var now=Snapshot();Save("bounce-after.json",now.GetRawText());
            if(I(now,"ProjectileBounces")<=I(baseline,"ProjectileBounces"))return false;
            Require(I(now,"ProjectileHits")-I(baseline,"ProjectileHits")>=2,"Bounce counter advanced without a second native hit");Require(NativeHpLost(launchHp)>0,"Ricochet flight caused no native HP loss");return true;
        },.2f,5);
        foreach(var id in new[]{"pause","levelup","shop"})
        {
            string captured=id;
            Add("flight-clock-status-and-vfx-freeze-"+captured,()=>PreparePause(captured),Pause,0,15);
        }
        Add("native-luck-book-quality-and-source-card-consistency",null,Luck,0,15);
        Add("full-source-inventory-and-native-xp",null,Inventory,0,10);
        Add("pause-inventory-layout-tooltip-and-scroll",()=>{UiAction("pause");expectNativeWindow=true;},InventoryCapture,.2f,8);
        Add("native-upgrade-inventory-source-spells",()=>
        {
            ResetAreasAndProjectiles();UiAction("resume");MyPlayer.Instance.inventory.AddLevel();expectNativeWindow=true;
        },()=>
        {
            if(!LevelupScreen.isLevelingUp || Time.realtimeSinceStartup-entered<.3f)return false;
            var rows=UiAction("refresh");Save("native-upgrade-source-inventory.json",JsonSerializer.Serialize(rows));
            Require(rows.Any(r=>r.StartsWith("EncounterSpellInventory active=True,learned=13,") && r.Contains("nativeTomesRetained=True,nativeItemsRetained=True")),"Native upgrade inventory did not replace weapon row with13 source spells while retaining tomes/items");
            Capture("native-upgrade-source-spells.ppm");CloseNativeChoice();expectNativeWindow=false;return true;
        },0,8);
        Add("native-postcombat-source-labels-and-value-invariants",()=>
        {
            ResetAreasAndProjectiles();UiAction("resume");PostCombatSourceFixtures();Call(Tome,"CastSpellForProbe","fire_blast");endingRun=true;expectNativeWindow=true;UiAction("postcombat");
        },PostCombat,0,16);
    }

    [HideFromIl2Cpp] bool Ready()
    {
        var player=MyPlayer.Instance;var game=GameManager.Instance;
        if(player?.inventory==null || game==null || !player.hasStarted || !game.IsPlaying() || MapGenerationController.isGenerating || !ElwynnBridge.Ready)return false;
        if(LevelupScreen.isLevelingUp){CloseNativeChoice();return false;}
        if(!(bool)Read(Hud,"CanSimulate"))return false;
        Require((bool)Read(Hud,"Active"),"Azeroth Mage not active");
        Require(Bool(Call(Tome,"SetAutomaticCastingForProbe",false)),"Guarded automatic-cast control rejected");
        var talent=Named("MageTalents.TalentBridge");
        originalTalent=(string)talent.GetProperty("StateJson").GetValue(null);
        float nativeLuckBefore=F(Snapshot().GetProperty("NativeStats"),"Luck"),sourceTalentLuck=0;
        var talentCatalog=Json(Read("MageTalents.TalentBridge","CatalogJson"));
        using(var saved=JsonDocument.Parse(originalTalent))
        {
            var ranks=P(saved.RootElement,"Ranks");
            foreach(var node in P(talentCatalog,"Talents").EnumerateArray())
            {
                if(!ranks.TryGetProperty(P(node,"Id").GetString(),out var rank))continue;
                foreach(var effect in P(node,"Effects").EnumerateArray())
                    if(P(effect,"Kind").GetString()=="stat" && P(effect,"Stat").GetInt32()==12)sourceTalentLuck+=P(effect,"Value").GetSingle()*rank.GetInt32();
            }
            Require(Bool(Call("MageTalents.TalentBridge","SetProbeState",P(saved.RootElement,"TalentXp").GetInt64(),"{}")),"Could not isolate talent procs for tome comparisons");
        }
        float nativeLuckWithoutTalents=F(Snapshot().GetProperty("NativeStats"),"Luck");
        Equal(nativeLuckBefore-nativeLuckWithoutTalents,sourceTalentLuck,.00001f,"Original additive source talent Luck must affect native zero-based Luck");
        Save("original-additive-talent-luck.json",new{nativeLuckBefore,nativeLuckWithoutTalents,sourceTalentLuck,source="SourceStatsRebuild VA14590B7B0,source Luck bonus starts0"});
        foreach(var id in new[]{"fireball","arcane_missiles","fire_blast","blizzard","ice_barrier"})Require(Bool(Call(Hud,"LearnSpellForProbe",id)),"Could not learn fixture spell "+id);
        Save("native-tome-catalog.json",Read(Tome,"NativeTomeCatalogJson"));
        Isolate("Quantity",0);Save("native-baseline.json",Read(Tome,"SnapshotJson"));
        var origin=player.transform.position;var forward=Camera.main!=null?Camera.main.transform.forward:player.transform.forward;forward.y=0;if(forward.sqrMagnitude<.01f)forward=Vector3.forward;forward.Normalize();
        golem=EnemyManager.Instance.SpawnBoss((EEnemy)23,-1,EEnemyFlag.StageBoss,Floor(origin+forward*11),1);
        other=EnemyManager.Instance.SpawnBoss((EEnemy)0,-1,EEnemyFlag.Boss,Floor(origin+forward*13+Vector3.right*4),1);
        Require(golem!=null && other!=null,"Native golem/foreign enemy fixture failed to spawn");
        foreach(var enemy in new[]{golem,other}){enemy.maxHp=Mathf.Max(enemy.maxHp,100000);enemy.hp=enemy.maxHp;durable.Add(enemy.id);}
        Plugin.Note($"FEATURE FIXTURE: native SpawnBoss Golem id23 stageBoss={golem.IsStageBoss()},other={other.id}; native AI/colliders unchanged; test-only HP>=100000 for every observed target, player HP replenished; talent procs isolated in memory.");
        return true;
    }
    [HideFromIl2Cpp] bool Ownership()
    {
        Require(golem!=null,"Golem fixture disappeared");if(golem.IsTeleporting())return false;
        if(phase==1){if(Time.realtimeSinceStartup-gateAt<.15f)return false;CaptureGolem();return true;}
        var before=Json(Call(EnemyVisual,"DescribeOwnership",golem));Save("golem-ownership-before.json",before.GetRawText());
        Require(before.TryGetProperty("oneSourceAndNoNative",out var one) && one.GetBoolean(),"Golem does not have exactly one source body with native bodies hidden");
        var regression=Json(Call(EnemyVisual,"RunOwnershipRegressionForProbe",golem,other));Save("golem-ownership-regression.json",regression.GetRawText());
        Require(regression.GetProperty("pass").GetBoolean() && I(regression,"checks")>=10,"Native/foreign-body ownership regression failed");
        Save("golem-ownership-after.json",Call(EnemyVisual,"DescribeOwnership",golem));
        var effects=Json(Call(EnemyVisual,"SpawnEffectsDiagnostics"));
        if(I(effects,"nativeSpawnParticleCalls")<1)
        {
            var pooled=PoolManager.Instance?.enemySpawnFxPool?.Get();
            explicitlyPooledFx=pooled!=null?pooled.GetComponent<EnemySpawnParticles>():null;
            Require(explicitlyPooledFx!=null,"Real native pooled spawn particles unavailable");
            explicitlyPooledFx.Set(golem);effects=Json(Call(EnemyVisual,"SpawnEffectsDiagnostics"));
        }
        Require(I(effects,"nativeSpawnParticleCalls")>=1,"No real EnemySpawnParticles.Set invocation observed");Save("native-spawn-effects.json",effects.GetRawText());
        // The ownership regression can create a replacement controller. Allow its
        // actual LateUpdate to compute the floor before asserting the rendered body.
        phase=1;gateAt=Time.realtimeSinceStartup;return false;
    }
    [HideFromIl2Cpp] void BeginQuantityCast()
    {
        sample=Snapshot();createdMark=I(sample,"CreatedProjectiles");hitMark=I(sample,"ProjectileHits");launchHp=HitPoints();
        int fire=QuantityOf(sample,"fireball"),missiles=QuantityOf(sample,"arcane_missiles");expectedProjectiles=fire+missiles;
        Require(Bool(Call(Tome,"CastSpellForProbe","fireball")) && Bool(Call(Tome,"CastSpellForProbe","arcane_missiles")),"Quantity casts had no real native targets");
        var after=Snapshot();Require(I(after,"CreatedProjectiles")-createdMark==expectedProjectiles,"Real created projectile count differs from the two source casts");
        Save("quantity-"+(phase==0?"baseline":"upgraded")+"-launched.json",after.GetRawText());gateAt=Time.realtimeSinceStartup;
    }
    [HideFromIl2Cpp] bool Quantity()
    {
        var now=Snapshot();if(I(now,"ProjectileHits")-hitMark<expectedProjectiles)return false;
        Require(I(now,"Hits")>I(sample,"Hits") && now.GetProperty("LastHit").GetProperty("RequestedDamage").GetSingle()>0,"Extra projectiles did not enter native damage");
        Require(NativeHpLost(launchHp)>0,"Quantity casts recorded no real native HP loss");
        Save("quantity-"+(phase==0?"baseline":"upgraded")+"-hit.json",now.GetRawText());
        Save("quantity-"+(phase==0?"baseline":"upgraded")+"-native-hp-lost.json",NativeHpLost(launchHp));
        if(phase==0)
        {
            baselineQuantity=QuantityOf(now,"fireball");int old=(int)F(now.GetProperty("NativeStats"),"Projectiles"),levels=0;
            do {Require(Bool(Call(Tome,"GrantTomeForProbe","Quantity",1,1)),"Native Quantity grant failed");now=Snapshot();levels++;}while((int)F(now.GetProperty("NativeStats"),"Projectiles")<=old && levels<30);
            Require(QuantityOf(now,"fireball")>baselineQuantity,"Quantity native upgrades did not cross an integer projectile boundary");
            Plugin.Note("FEATURE quantity native levels="+levels+",nativeProjectiles="+F(now.GetProperty("NativeStats"),"Projectiles")+",quantity="+baselineQuantity+"->"+QuantityOf(now,"fireball"));
            phase=1;Call(Tome,"ClearProjectilesForProbe");BeginQuantityCast();return false;
        }
        Require(QuantityOf(now,"fireball")>baselineQuantity,"Upgraded quantity was lost before impact");return true;
    }
    [HideFromIl2Cpp] bool Damage()
    {
        ResetAreasAndProjectiles();Isolate("Damage",0);var before=Snapshot();int hits=I(before,"Hits");
        launchHp=HitPoints();Require(Bool(Call(Tome,"CastSpellForProbe","fire_blast")),"Baseline Fire Blast cast failed");var first=Snapshot();
        Require(I(first,"Hits")>hits,"Baseline spell did not hit native enemy");
        Require(NativeHpLost(launchHp)>0,"Baseline Fire Blast produced no native HP loss");
        Isolate("Damage",5);launchHp=HitPoints();Require(Bool(Call(Tome,"CastSpellForProbe","fire_blast")),"Damage-book Fire Blast cast failed");var after=Snapshot();
        Require(F(after.GetProperty("NativeStats"),"DamageMultiplier")>F(before.GetProperty("NativeStats"),"DamageMultiplier"),"Native Damage tome did not change PlayerStats");
        Require(F(Spell(after,"fire_blast"),"Damage")>F(Spell(before,"fire_blast"),"Damage"),"Damage tome did not change effective spell damage");
        Require(I(after,"Hits")>I(first,"Hits") && after.GetProperty("LastHit").GetProperty("RequestedDamage").GetSingle()>0,"Damage-book cast did not enter native damage");
        Require(NativeHpLost(launchHp)>0,"Damage-book Fire Blast produced no native HP loss");
        Save("damage-baseline-hit.json",first.GetRawText());Save("damage-upgraded-hit.json",after.GetRawText());return true;
    }
    [HideFromIl2Cpp] void LaunchMovementSample()
    {
        Call(Tome,"ClearProjectilesForProbe");Require(Bool(Call(Tome,"LaunchSlowProjectileForProbe","fireball",.5f)),"Slow source projectile fixture unavailable");
        sample=Snapshot();gateAt=Time.realtimeSinceStartup;
    }
    [HideFromIl2Cpp] bool ProjectileSpeed()
    {
        if(Time.realtimeSinceStartup-gateAt<.25f)return false;
        var after=Snapshot();var first=FirstProjectile(sample);var flight=FirstProjectile(after);
        Require(F(flight,"Distance")>F(first,"Distance"),"Native-clock projectile did not move");
        if(phase==0)
        {baselineValue=F(flight,"Speed");baselineNativeStat=F(after.GetProperty("NativeStats"),"ProjectileSpeedMultiplier");Save("speed-baseline.json",after.GetRawText());Isolate("ProjectileSpeed",5);phase=1;LaunchMovementSample();return false;}
        float stat=F(after.GetProperty("NativeStats"),"ProjectileSpeedMultiplier");Require(stat>baselineNativeStat && F(flight,"Speed")>baselineValue,"Native speed tome did not reach actual moving projectile");
        Equal(F(flight,"Speed")/baselineValue,stat/baselineNativeStat,.001f,"Projectile-speed multiplier applied once");Save("speed-upgraded.json",after.GetRawText());return true;
    }
    [HideFromIl2Cpp] void BeginCooldownInterval()
    {
        OnlyAutomaticFireball();castMark=(int)Read(Hud,"CastCount");firstCastAt=-1;
        Require(Bool(Call(Tome,"SetAutomaticCastingForProbe",true)),"Automatic cast fixture rejected");
    }
    [HideFromIl2Cpp] bool Cooldown()
    {
        int casts=(int)Read(Hud,"CastCount");float now=(float)Read(Hud,"CombatTime");
        if(firstCastAt<0 && casts>castMark){firstCastAt=now;castMark=casts;}
        else if(firstCastAt>=0 && casts>castMark)
        {
            Call(Tome,"SetAutomaticCastingForProbe",false);float interval=now-firstCastAt;var snap=Snapshot();float expected=F(Spell(snap,"fireball"),"Cooldown");
            Equal(interval,expected,.16f,"Observed automatic Fireball interval");
            Save("cooldown-"+(phase==0?"baseline":"upgraded")+".json",JsonSerializer.Serialize(new{interval,expected,snapshot=snap}));
            if(phase==0){baselineInterval=interval;Isolate("Cooldown",8);phase=1;BeginCooldownInterval();return false;}
            Require(interval<baselineInterval-.02f,"Real native Cooldown tome did not shorten cast interval");return true;
        }
        return false;
    }
    [HideFromIl2Cpp] bool Size()
    {
        ResetAreasAndProjectiles();Isolate("Size",0);Require(Bool(Call(Tome,"LaunchSlowProjectileForProbe","fireball",.5f)),"Baseline size projectile missing");Require(Bool(Call(Tome,"CastSpellForProbe","blizzard")),"Baseline ground area missing");
        var before=Snapshot();float size=F(FirstProjectile(before),"Size"),radius=F(FirstArea(before),"Radius");
        ResetAreasAndProjectiles();Isolate("Size",1);Require(Bool(Call(Tome,"LaunchSlowProjectileForProbe","fireball",.5f)) && Bool(Call(Tome,"CastSpellForProbe","blizzard")),"Upgraded size fixtures missing");var after=Snapshot();
        Require(F(FirstProjectile(after),"Size")>size && F(FirstArea(after),"Radius")>radius,"Native Size tome did not reach real projectile/area instances");
        Save("size-baseline.json",before.GetRawText());Save("size-upgraded.json",after.GetRawText());return true;
    }
    [HideFromIl2Cpp] bool Duration()
    {
        if(phase==0)
        {
            ResetAreasAndProjectiles();Isolate("Duration",0);Require(Bool(Call(Tome,"CastSpellForProbe","blizzard")),"Baseline duration area missing");baseline=Snapshot();int ticks=I(FirstArea(baseline),"Remaining");float duration=F(Spell(baseline,"blizzard"),"Duration");
            ResetAreasAndProjectiles();Isolate("Duration",5);Require(Bool(Call(Tome,"CastSpellForProbe","blizzard")),"Duration-book area missing");sample=Snapshot();launchHp=HitPoints();
            Require(I(FirstArea(sample),"Remaining")>ticks && F(Spell(sample,"blizzard"),"Duration")>duration,"Native Duration tome did not extend real area/tick budget");
            Save("duration-baseline.json",baseline.GetRawText());Save("duration-upgraded-created.json",sample.GetRawText());phase=1;gateAt=Time.realtimeSinceStartup;return false;
        }
        if(Time.realtimeSinceStartup-gateAt<.8f)return false;var after=Snapshot();
        Require(I(after,"Hits")>I(sample,"Hits") && I(FirstArea(after),"Remaining")<I(FirstArea(sample),"Remaining"),"Longer native-book area produced no actual periodic native damage");
        Require(NativeHpLost(launchHp)>0,"Duration-book area did not reduce native HP");
        Save("duration-upgraded-ticked.json",after.GetRawText());return true;
    }
    [HideFromIl2Cpp] bool Precision()
    {
        if(phase==0)
        {
            ResetAreasAndProjectiles();Isolate("Precision",0);baseline=Snapshot();Isolate("Precision",20,5);sample=Snapshot();
            var player=MyPlayer.Instance;var forward=player.transform.forward;forward.y=0;if(forward.sqrMagnitude<.01f)forward=Vector3.forward;forward.Normalize();
            precisionTarget=EnemyManager.Instance.SpawnBoss((EEnemy)0,-1,EEnemyFlag.Boss,Floor(player.transform.position+forward*8),1);
            Require(precisionTarget!=null,"Native precision target failed to spawn");precisionTarget.maxHp=10000000;precisionTarget.hp=precisionTarget.maxHp;durable.Add(precisionTarget.id);
            Plugin.Note("FEATURE precision fixture: a fresh native target with10,000,000HP; native spawning, AI, collision and damage unchanged");
            phase=1;return false;
        }
        if(precisionTarget.IsTeleporting())return false;
        float chance=F(sample.GetProperty("NativeStats"),"CritChance");Require(chance>F(baseline.GetProperty("NativeStats"),"CritChance"),"Native Precision tome did not increase critical chance");
        int before=I(sample,"CriticalHits"),hits=I(sample,"Hits");launchHp=HitPoints();
        for(int i=0;i<30;i++)Require(Bool(Call(Tome,"CastSpellForProbe","fire_blast")),"Precision Fire Blast had no target");
        var after=Snapshot();Save("precision-real-hits.json",after.GetRawText());Require(I(after,"Hits")>hits,"Precision test produced no native hits");
        Require(NativeHpLost(launchHp)>0,"Precision casts caused no real native HP loss");
        if(I(after,"CriticalHits")==before)throw new Skip("Native chance="+chance+";30 real hits produced no critical, chance change verified but critical outcome unproven");
        return true;
    }

    [HideFromIl2Cpp] void PreparePause(string kind)
    {
        activePauseCase=kind;ResetAreasAndProjectiles();Isolate("Quantity",0);MovePlayerAwayFromFixture();OnlyAutomaticFireball();
        if(kind=="shop")PrepareMerchant();
        castMark=(int)Read(Hud,"CastCount");Call(Tome,"SetAutomaticCastingForProbe",true);expectNativeWindow=true;
    }
    [HideFromIl2Cpp] bool Pause()
    {
        if(phase==0)
        {
            if(activePauseCase=="shop" && (merchant==null || merchant.items==null || merchant.items.Count==0 || merchant.prices==null || merchant.prices.Count!=merchant.items.Count))return false;
            if((int)Read(Hud,"CastCount")<=castMark)return false;Call(Tome,"SetAutomaticCastingForProbe",false);Call(Tome,"ClearProjectilesForProbe");
            Require(Bool(Call(Tome,"LaunchSlowProjectileForProbe","fireball",.02f)),"Pause flight fixture failed");
            Call(Tome,"CastSpellForProbe","blizzard");Call(Tome,"CastSpellForProbe","ice_barrier");
            if(activePauseCase=="pause"){Save("pause-native-before-open.json",NativeWindows());UiAction("pause");Save("pause-native-immediately-after-open.json",NativeWindows());}
            else if(activePauseCase=="levelup")MyPlayer.Instance.inventory.AddLevel();
            else
            {
                // Native Interact sets currentlyInteracting and updates the real
                // item prices before it queues ShadyGuy. Its native return value
                // is false even on success (RVA4CD400); inspect the real context.
                merchant.Interact();
                Require(InteractableShadyGuy.currentlyInteracting!=null && InteractableShadyGuy.currentlyInteracting.Pointer==merchant.Pointer,"Native merchant did not establish its own item/price context");
                Save("shop-native-interaction.json",JsonSerializer.Serialize(new{merchant=merchant.name,scene=merchant.gameObject.scene.name,fixture=merchantFixture!=null,items=merchant.items.Count,prices=merchant.prices.Count,currentMerchant=InteractableShadyGuy.currentlyInteracting.name}));
            }
            phase=1;gateAt=Time.realtimeSinceStartup;return false;
        }
        if(phase==1)
        {
            bool opened=activePauseCase=="pause"?UiManager.Instance.pause.IsPaused():UiManager.Instance.encounterWindows.encounterInProgress;
            if(!opened)
            {
                if(activePauseCase=="pause" && Time.realtimeSinceStartup-gateAt>.12f)
                {
                    Save("pause-native-waiting-for-admission.json",NativeWindows());
                    if(UiManager.Instance.pause.CanPause()){UiAction("pause");gateAt=Time.realtimeSinceStartup;}
                }
                return false;
            }
            if(Time.realtimeSinceStartup-gateAt<.12f)return false;
            pausedBefore=Snapshot();Require(!pausedBefore.GetProperty("CanSimulate").GetBoolean(),"Native "+activePauseCase+" UI did not block source simulation");
            FirstProjectile(pausedBefore);
            if(activePauseCase=="shop")Require(Object.FindObjectsOfType<UpgradeButton>().Any(b=>b.gameObject.activeInHierarchy && b.isItem),"ShadyGuy encounter did not expose native item-choice cards");
            animationBefore=Animation(MageVisual);enemyAnimationBefore=Animation(EnemyVisual);
            Save(activePauseCase+"-paused-before.json",pausedBefore.GetRawText());phase=2;gateAt=Time.realtimeSinceStartup;return false;
        }
        if(phase==2)
        {
            if(Time.realtimeSinceStartup-gateAt<.7f)return false;var after=Snapshot();AssertFrozen(pausedBefore,after);
            Equal(F(animationBefore.GetProperty("clock"),"clock"),F(Animation(MageVisual).GetProperty("clock"),"clock"),.00001f,"Mage animation clock during "+activePauseCase);
            Equal(F(enemyAnimationBefore.GetProperty("clock"),"clock"),F(Animation(EnemyVisual).GetProperty("clock"),"clock"),.00001f,"Enemy animation clock during "+activePauseCase);
            Save(activePauseCase+"-paused-after.json",after.GetRawText());
            if(activePauseCase=="pause")UiAction("resume");else CloseNativeChoice();
            phase=3;gateAt=Time.realtimeSinceStartup;return false;
        }
        if(Time.realtimeSinceStartup-gateAt<.3f)return false;
        var resumed=Snapshot();if(!resumed.GetProperty("CanSimulate").GetBoolean())return false;
        Require(F(resumed,"CombatTime")>F(pausedBefore,"CombatTime"),"Source combat clock did not resume after "+activePauseCase);
        Require(F(FirstProjectile(resumed),"Distance")>F(FirstProjectile(pausedBefore),"Distance"),"Source projectile did not move after closing "+activePauseCase);
        Save(activePauseCase+"-resumed.json",resumed.GetRawText());expectNativeWindow=false;return true;
    }
    [HideFromIl2Cpp] bool Inventory()
    {
        if(phase==0)
        {
            ResetAreasAndProjectiles();foreach(var id in allSpells)Require(Bool(Call(Hud,"LearnSpellForProbe",id)),"Could not learn source spell "+id);
            var ids=(string[])Read(Hud,"SpellIds");var ranks=(int[])Read(Hud,"SpellRanks");Require(ids.Length==13 && ranks.All(r=>r>=1),"Expected13 learned source spell ranks");
            int rank=ranks[Array.IndexOf(ids,"fireball")];Require(Bool(Call(Hud,"ApplyUpgradeForProbe","fireball",0,0)),"Real source Fireball rank upgrade rejected");Require(((int[])Read(Hud,"SpellRanks"))[Array.IndexOf((string[])Read(Hud,"SpellIds"),"fireball")]==rank+1,"Source rank did not increase");
            Isolate("Damage",1);foreach(var id in new[]{"Quantity","Cooldown","Size"})Require(Bool(Call(Tome,"GrantTomeForProbe",id,1,1)),"Native tome UI fixture failed "+id);
            Require(MyPlayer.Instance.inventory.tomeInventory.GetNumTomes()==4 && InventoryUtility.GetNumAvailableTomeSlots()==4,"Four real native tomes/slots not present");
            var items=MyPlayer.Instance.inventory.itemInventory;items.AddItem(EItem.Oats);items.AddItem(EItem.BeefyRing);items.AddItem(EItem.Battery);
            MyPlayer.Instance.inventory.ChangeGold(137);baseline=Snapshot();xpMark=MyPlayer.Instance.inventory.playerXp.GetXpInt();UiAction("xp:3");phase=1;gateAt=Time.realtimeSinceStartup;return false;
        }
        if(Time.realtimeSinceStartup-gateAt<.4f)return false;
        var description=UiAction("refresh");Save("hud-combat-description.json",JsonSerializer.Serialize(description));Save("hud-all-spells-native-books.json",Read(Tome,"SnapshotJson"));
        int xp=MyPlayer.Instance.inventory.playerXp.GetXpInt();Require(xp>xpMark,"Native XP did not increase after real inventory.AddXp");Require(description.Any(s=>s.StartsWith("XP native="+xp+",")),"Custom XP readout differs from actual native XP");
        Require(description.Any(s=>s.StartsWith("NativeTimer ") && s.Contains("runEnabled=True")),"Native GameTimer run display is not enabled");
        Require(!HasCustomRunTime(),"A custom RunTime duplicate exists");
        int realGold=MyPlayer.Instance.inventory.goldInt;
        Require(description.Any(s=>s.StartsWith("NativeGold active=True,enabled=True,") && s.Contains(realGold.ToString()) && s.Contains("customGold=absent")),"Native gold widget is absent, hidden or differs from real inventory gold");
        float cluster=(float)Read("AzerothHud.HudDiagnostics","PlayerClusterTopScreenPixels"),row=(float)Read("AzerothHud.HudDiagnostics","NativeTopRowBottomScreenPixels");
        Require(cluster>=80 && cluster>=row+11.5f,"Source portrait/HP panel overlaps the native top-row counters");
        foreach(var xpBar in Resources.FindObjectsOfTypeAll<XpBarUI>())
            if(xpBar!=null && xpBar.gameObject.scene.IsValid() && xpBar.xpBar!=null)Require(!xpBar.xpBar.enabled,"Native XP fill remains visible");
        Save("native-gold-and-source-frame-safe-area.json",JsonSerializer.Serialize(description));
        Capture("combat-xp-and-all-source-spells.ppm");return true;
    }
    [HideFromIl2Cpp] bool Luck()
    {
        const string api="AzerothAbilities.LuckCompatibilityDiagnostics";
        if(phase==0)
        {
            foreach(var id in allSpells)Require(Bool(Call(Hud,"LearnSpellForProbe",id)),"Luck fixture spell missing "+id);
            Isolate("Luck",0);float old=(float)Read(api,"CurrentLuck");Isolate("Luck",5);float current=(float)Read(api,"CurrentLuck");Require(current>old,"Real native Luck tome did not change inventory Luck");
            Require(Bool(Call(api,"SampleNativeQualitiesForProbe",8192,0f,3f)),"Native common/boosted/current Luck quality distributions failed");
            var distribution=Json(Read(api,"LastSamplingJson"));Require(distribution.GetProperty("Passed").GetBoolean() && distribution.GetProperty("RandomStateRestored").GetBoolean(),"Native quality sampling did not preserve RNG state/pass checks");Save("luck-native-distributions.json",distribution.GetRawText());
            sample=Json(Read(api,"SnapshotJson"));MyPlayer.Instance.inventory.AddLevel();qualityAttempts=1;activePauseCase=null;expectNativeWindow=true;phase=1;gateAt=Time.realtimeSinceStartup;return false;
        }
        if(phase==1)
        {
            if(!LevelupScreen.isLevelingUp)return false;
            var snapshot=Json(Read(api,"SnapshotJson"));var cards=snapshot.GetProperty("BoundCards");
            if(cards.GetArrayLength()==0)
            {
                if(Time.realtimeSinceStartup-gateAt>.6f)
                {
                    Require(qualityAttempts<6,"Six real native levelups produced no source-spell card to verify");
                    CloseNativeChoice();MyPlayer.Instance.inventory.AddLevel();qualityAttempts++;gateAt=Time.realtimeSinceStartup;
                    Plugin.Note("FEATURE native levelup contained only native tome cards; retry source-card quality fixture attempt="+qualityAttempts);
                }
                return false;
            }
            Require(cards.EnumerateArray().All(c=>c.GetProperty("SourceValueMatchesQuality").GetBoolean()),"Displayed native quality disagrees with source upgrade delta");
            Save("luck-native-source-cards-before.json",snapshot.GetRawText());
            var bound=(System.Collections.IDictionary)Named("AzerothAbilities.AbilityOffers").GetField("Bound",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            var button=Object.FindObjectsOfType<UpgradeButton>().FirstOrDefault(b=>b.gameObject.activeInHierarchy && bound.Contains(b.Pointer));
            if(button==null)return false;button.SelectUpgrade();phase=2;gateAt=Time.realtimeSinceStartup;return false;
        }
        if(Time.realtimeSinceStartup-gateAt<.15f)return false;
        var selected=Json(Read(api,"SnapshotJson"));Require(I(selected,"SourceSelections")>I(sample,"SourceSelections") && I(selected,"ValueMismatches")==0,"Real source-card selection did not retain its native quality/value");Save("luck-native-source-card-selected.json",selected.GetRawText());expectNativeWindow=false;return true;
    }
    [HideFromIl2Cpp] bool InventoryCapture()
    {
        if(phase==0)
        {
            var rows=UiAction("refresh");Save("hud-pause-before-scroll.json",JsonSerializer.Serialize(rows));
            Require(rows.Any(s=>s.Contains("learned=13") && s.Contains("nativeTomesRetained=True") && s.Contains("nativeItemsRetained=True")),"Pause inventory did not retain13 source spells/native tome/item parents");
            UiAction("scroll-top");UiAction("tooltip:0");phase=1;gateAt=Time.realtimeSinceStartup;return false;
        }
        // The real ToolTipObject.OnPointerEnter starts a native hover delay.
        // Keep the UI open across Unity frames before capturing its rendered text.
        if(Time.realtimeSinceStartup-gateAt<.65f)return false;
        Save("hud-pause-tooltip-after-native-hover-delay.json",JsonSerializer.Serialize(UiAction("refresh")));Capture("pause-source-tooltip.ppm");
        var bottom=UiAction("scroll-bottom");Save("hud-pause-bottom.json",JsonSerializer.Serialize(bottom));Capture("pause-all-spells-bottom.ppm");
        Require(!HasCustomRunTime(),"Custom RunTime duplicate appeared in pause inventory");UiAction("resume");expectNativeWindow=false;return true;
    }
    [HideFromIl2Cpp] void PostCombatSourceFixtures()
    {
        using(var saved=JsonDocument.Parse(originalTalent))Require(Bool(Call("MageTalents.TalentBridge","SetProbeState",P(saved.RootElement,"TalentXp").GetInt64(),P(saved.RootElement,"Ranks").GetRawText())),"Could not restore source talent ranks for native result rows");
        Enemy target=null;
        foreach(var pair in EnemyManager.Instance.enemies)if(pair.Value!=null&&!pair.Value.IsDeadOrDyingNextFrame()){target=pair.Value;break;}
        Require(target!=null,"A real native target is required for result damage-source fixtures");
        float before=target.hp;
        foreach(var id in new[]{"AzerothTalent:ignite","AzerothTalent:presence_of_mind","AzerothTalent:splitting_ice","Azeroth:water_elemental"})
        {
            var damage=new DamageContainer(0,id){damage=37,crit=false,knockback=0,enemy=target,element=EElement.Neutral,direction=Vector3.forward};
            target.DamageFromPlayerOther(damage);
        }
        Require(target.hp<before,"Native result fixture did not apply real attributed damage");
        Save("postcombat-source-fixture.json",new{before,after=target.hp,mode="UI presentation fixture: real native attributed damage with actual observed source IDs, not a claim of natural proc occurrence"});
    }
    [HideFromIl2Cpp] bool PostCombat()
    {
        if(phase==0)
        {
            if(GameManager.Instance==null || !GameManager.Instance.isGameOver || Time.realtimeSinceStartup-entered<2)return false;
            UiAction("postcombat-open");phase=1;gateAt=Time.realtimeSinceStartup;return false;
        }
        if(Time.realtimeSinceStartup-gateAt<.4f)return false;
        var rows=UiAction("refresh");Save("postcombat-native-values-and-source-labels.json",JsonSerializer.Serialize(rows));
        Require((bool)Read("AzerothHud.PostCombatDiagnostics","Visible"),"Source post-combat adapter is not visible");
        Require((bool)Read("AzerothHud.PostCombatDiagnostics","CombatHudHidden"),"In-run source HUD was not hidden at native end-run");
        Require((bool)Read("AzerothHud.PostCombatDiagnostics","SourcePortraitShown"),"Native result screen still lacks the source Mage portrait");
        var sourceRows=rows.Where(r=>r.StartsWith("source=Azeroth:") || r.StartsWith("source=AzerothTalent:")).ToArray();
        Require(sourceRows.Length>0,"No actual source spell damage rows reached the native result table");
        foreach(var id in new[]{"AzerothTalent:ignite","AzerothTalent:presence_of_mind","AzerothTalent:splitting_ice","Azeroth:water_elemental"})
            Require(sourceRows.Any(r=>r.StartsWith("source="+id+";")&&r.Contains("sourceIconMatches=True")&&!r.Contains(";name=Azeroth")),"Native result row has a missing source name/icon: "+id);
        Require(rows.Where(r=>r.StartsWith("spellPanel ")).All(r=>r.Contains("scrolling=False,scrollbar=False")),"An inventory spell row still scrolls");
        Require(sourceRows.All(r=>r.Contains("nativeDamageTextUnchanged=True") && r.Contains("nativeDpsTextUnchanged=True")),"Source result adaptation changed native damage/DPS text");
        Require(!sourceRows.Any(r=>r.Contains(";name=Azeroth:")),"Native custom damage source ids were not mapped to spell labels");
        Capture("elwynn-feature-postcombat.ppm");return true;
    }

    [HideFromIl2Cpp] bool CaptureBuildings()
    {
        var roots=Object.FindObjectsOfType<ElwynnPropMarker>().Where(m=>m!=null && m.gameObject.activeInHierarchy && m.gameObject.scene.IsValid() && m.name.StartsWith("AzerothElwynn_building_",StringComparison.Ordinal)).ToArray();
        if(roots.Length==0)throw new Skip("This native seed retained no WMO building; no facade/door view can be claimed");
        var camera=Camera.main;Require(camera!=null,"Native camera missing for retained WMO proof");var oldPosition=camera.transform.position;var oldRotation=camera.transform.rotation;
        var evidence=new List<object>();int count=0;
        try
        {
            foreach(var marker in roots)
            {
                var visual=marker.transform.Find("OriginalElwynnBuilding");Require(visual!=null,"Retained WMO has no original visual child");
                var id=Plugin.Assets.Buildings.Keys.FirstOrDefault(k=>marker.name.Contains("building_"+k+"_variant",StringComparison.Ordinal));Require(id!=null,"Retained WMO source metadata missing for "+marker.name);
                var renderers=visual.GetComponentsInChildren<MeshRenderer>().Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();Require(renderers.Length>0,"Retained WMO has no real active render mesh");
                var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
                var entrances=Plugin.Assets.BuildingEntrances[id];Require(entrances.Length>0,"Retained WMO has no source entrance metadata");var entrance=entrances[0];
                var door=visual.TransformPoint(new Vector3(entrance.Center.x,entrance.FloorY,entrance.Center.z));var outward=visual.TransformDirection(entrance.Outward);outward.y=0;outward.Normalize();Require(outward.sqrMagnitude>.5f,"WMO door outward vector invalid");
                float height=visual.TransformVector(Vector3.up*entrance.Height).magnitude;
                camera.transform.position=door+outward*Mathf.Max(12,bounds.extents.magnitude*2.1f)+Vector3.up*Mathf.Max(2,bounds.extents.y*.45f);camera.transform.LookAt(bounds.center);
                Capture("wmo-"+count+"-"+id+"-facade-door.ppm");
                evidence.Add(new{source=id,root=marker.name,meshRenderers=renderers.Length,boundsCenter=Vec(bounds.center),boundsSize=Vec(bounds.size),door=Vec(door),outward=Vec(outward),entrance.SourcePortalIndex,doorHeight=height,camera=Vec(camera.transform.position)});
                if(count==0){camera.transform.position=door+outward*Mathf.Max(5,height*1.8f)+Vector3.up*Mathf.Max(1.6f,height*.55f);camera.transform.LookAt(door+Vector3.up*height*.45f);Capture("wmo-0-"+id+"-door-close.ppm");}
                count++;
            }
            Save("retained-wmo-native-camera-views.json",JsonSerializer.Serialize(evidence));return true;
        }
        finally{camera.transform.position=oldPosition;camera.transform.rotation=oldRotation;}
    }

    [HideFromIl2Cpp] void Add(string name,Action enter,Func<bool> poll,float delay=0,float timeout=12)=>steps.Add(new Step{Name=name,Enter=enter,Poll=poll,Delay=delay,Timeout=timeout});
    [HideFromIl2Cpp] Type Named(string name)
    {
        if(types.TryGetValue(name,out var result))return result;
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()){result=assembly.GetType(name,false);if(result!=null){types[name]=result;return result;}}
        throw new MissingMemberException("Feature API not loaded: "+name);
    }
    [HideFromIl2Cpp] object Call(string type,string method,params object[] args)
    {
        var info=Named(type).GetMethod(method,BindingFlags.Public|BindingFlags.Static);if(info==null)throw new MissingMethodException(type,method);return info.Invoke(null,args);
    }
    [HideFromIl2Cpp] object Read(string type,string property)=>Named(type).GetProperty(property,BindingFlags.Public|BindingFlags.Static)?.GetValue(null)??throw new MissingMemberException(type,property);
    [HideFromIl2Cpp] string[] UiAction(string action)=>(string[])Call(Ui,"BatchProbe",action);
    [HideFromIl2Cpp] static JsonElement Json(object text){using var doc=JsonDocument.Parse((string)text);return doc.RootElement.Clone();}
    [HideFromIl2Cpp] static JsonElement P(JsonElement element,string name)
    {foreach(var property in element.EnumerateObject())if(property.Name.Equals(name,StringComparison.OrdinalIgnoreCase))return property.Value;throw new KeyNotFoundException(name);}
    [HideFromIl2Cpp] JsonElement Snapshot()=>Json(Read(Tome,"SnapshotJson"));
    [HideFromIl2Cpp] JsonElement Animation(string type)=>Json(Call(type,"AnimationDiagnostics"));
    [HideFromIl2Cpp] JsonElement AnimationBundle()=>Json(JsonSerializer.Serialize(new{mage=Animation(MageVisual),enemies=Animation(EnemyVisual)}));
    [HideFromIl2Cpp] static float F(JsonElement json,string name)=>json.GetProperty(name).GetSingle();
    [HideFromIl2Cpp] static int I(JsonElement json,string name)=>json.GetProperty(name).GetInt32();
    [HideFromIl2Cpp] static string S(JsonElement json,string name)=>json.GetProperty(name).GetString();
    [HideFromIl2Cpp] static bool Bool(object value)=>value is bool result && result;
    [HideFromIl2Cpp] static float[] Vec(Vector3 vector)=>new[]{vector.x,vector.y,vector.z};
    [HideFromIl2Cpp] static JsonElement Spell(JsonElement snapshot,string id)=>snapshot.GetProperty("Spells").EnumerateArray().First(s=>S(s,"Id")==id);
    [HideFromIl2Cpp] static int QuantityOf(JsonElement snapshot,string id)=>I(Spell(snapshot,id),"Quantity");
    [HideFromIl2Cpp] static JsonElement FirstProjectile(JsonElement snapshot)
    {var values=snapshot.GetProperty("Projectiles");Require(values.GetArrayLength()>0,"Expected a live source projectile in evidence");return values[0];}
    [HideFromIl2Cpp] static JsonElement FirstArea(JsonElement snapshot)
    {var values=snapshot.GetProperty("Behaviors").GetProperty("Areas");Require(values.GetArrayLength()>0,"Expected an actual source ground-area instance");return values[0];}
    [HideFromIl2Cpp] static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    [HideFromIl2Cpp] static void Equal(float first,float second,float epsilon,string label)=>Require(Mathf.Abs(first-second)<=epsilon,label+": "+first+" != "+second);
    [HideFromIl2Cpp] void Isolate(string id,int levels,int rarity=1)=>Require(Bool(Call(Tome,"UseIsolatedTomeForProbe",id,levels,rarity)),"Native isolated tome fixture rejected "+id);
    [HideFromIl2Cpp] void ResetAreasAndProjectiles(){Call(Tome,"SetAutomaticCastingForProbe",false);Call(Tome,"ClearProjectilesForProbe");Call("AzerothAbilities.MageSpellBehaviors","Clear");}
    [HideFromIl2Cpp] Dictionary<uint,float> HitPoints()
    {var values=new Dictionary<uint,float>();foreach(var pair in EnemyManager.Instance.enemies)if(pair.Value!=null)values[pair.Key]=pair.Value.hp;return values;}
    [HideFromIl2Cpp] float NativeHpLost(Dictionary<uint,float> before)
    {float lost=0;foreach(var pair in before){if(EnemyManager.Instance.enemies.TryGetValue(pair.Key,out var enemy) && enemy!=null)lost+=Mathf.Max(0,pair.Value-enemy.hp);}return lost;}
    [HideFromIl2Cpp] void OnlyAutomaticFireball()
    {
        float now=(float)Read(Hud,"CombatTime");var field=Named("AzerothAbilities.AbilityRuntime").GetField("Spells",BindingFlags.Static|BindingFlags.Public);
        foreach(var spell in (System.Collections.IEnumerable)field.GetValue(null))
        {var definition=spell.GetType().GetProperty("Definition").GetValue(spell);var id=(string)definition.GetType().GetProperty("Id").GetValue(definition);spell.GetType().GetProperty("NextCast").SetValue(spell,now+(id=="fireball"?.05f:1000));}
    }
    [HideFromIl2Cpp] void CloseNativeChoice()
    {
        if(!LevelupScreen.isLevelingUp && UiManager.Instance?.encounterWindows?.encounterInProgress!=true)return;
        var card=Object.FindObjectsOfType<UpgradeButton>().FirstOrDefault(b=>b.gameObject.activeInHierarchy);
        if(activePauseCase=="shop")
        {
            if(card?.upgradePicker?.levelupScreen!=null){card.upgradePicker.levelupScreen.Leave();return;}
            var screen=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(s=>s.gameObject.activeInHierarchy);if(screen!=null){screen.Leave();return;}
        }
        if(card!=null){card.SelectUpgrade();return;}
        var level=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(s=>s.gameObject.activeInHierarchy);
        if(level!=null)level.CloseLevelupScreen();
    }
    [HideFromIl2Cpp] void AssertFrozen(JsonElement before,JsonElement after)
    {
        Equal(F(before,"CombatTime"),F(after,"CombatTime"),.00001f,"Combat clock while native UI is open");
        Require(I(before,"Casts")==I(after,"Casts") && I(before,"Hits")==I(after,"Hits"),"Source cast/damage advanced while UI was open");
        foreach(var name in new[]{"Projectiles","Spells","Behaviors"})
        {
            if(name=="Spells")
            {var a=before.GetProperty(name);var b=after.GetProperty(name);Require(a.GetArrayLength()==b.GetArrayLength(),"Spell list changed while paused");for(int i=0;i<a.GetArrayLength();i++)Equal(F(a[i],"CooldownRemaining"),F(b[i],"CooldownRemaining"),.00001f,"Cooldown remaining while paused");}
            else Require(before.GetProperty(name).GetRawText()==after.GetProperty(name).GetRawText(),name+" changed while native UI was open");
        }
        Require(before.GetProperty("Vfx").GetProperty("EffectAges").GetRawText()==after.GetProperty("Vfx").GetProperty("EffectAges").GetRawText(),"Source VFX ages changed while paused");
    }
    [HideFromIl2Cpp] Vector3 Floor(Vector3 point)
    {
        int mask=GameManager.Instance.whatIsGround.value;
        if(Physics.Raycast(point+Vector3.up*100,Vector3.down,out var hit,240,mask,QueryTriggerInteraction.Ignore))return hit.point+Vector3.up*.1f;
        throw new InvalidOperationException("No real native ground under fixture "+point);
    }
    [HideFromIl2Cpp] string NativeWindows()
    {
        var ui=UiManager.Instance;var pause=ui?.pause;var windows=ui?.encounterWindows;var game=GameManager.Instance;
        return JsonSerializer.Serialize(new{pausePresent=pause!=null,pauseActive=pause!=null && pause.IsPaused(),canNativePause=pause!=null && pause.CanPause(),encounterPresent=windows!=null,hasEncounter=windows!=null && windows.HasEncounter(),encounterInProgress=windows!=null && windows.encounterInProgress,encounterObjectActive=windows!=null && windows.gameObject.activeInHierarchy,encounterComponentEnabled=windows!=null && windows.enabled,levelup=LevelupScreen.isLevelingUp,nativePaused=MyTime.paused,playing=game!=null && game.IsPlaying(),cutscene=game!=null && game.cutscene});
    }
    [HideFromIl2Cpp] void PrepareMerchant()
    {
        var all=Resources.FindObjectsOfTypeAll<InteractableShadyGuy>();
        merchant=all.FirstOrDefault(m=>m!=null && m.gameObject.scene.IsValid() && m.gameObject.activeInHierarchy && m.CanInteract());
        if(merchant==null)
        {
            var prefab=all.FirstOrDefault(m=>m!=null && !m.gameObject.scene.IsValid());
            if(prefab==null)throw new Skip("No real native ShadyGuy instance or prefab is loaded in this map; cannot fabricate shop items/context");
            merchantFixture=Object.Instantiate(prefab.gameObject);merchantFixture.name="Feature_NativeShadyGuyFixture";
            merchantFixture.transform.position=Floor(MyPlayer.Instance.transform.position+Vector3.right*4);merchantFixture.SetActive(true);
            merchant=merchantFixture.GetComponent<InteractableShadyGuy>();
        }
        Require(merchant!=null,"Native merchant prefab has no InteractableShadyGuy component");
        // Invoke the same native initializer used by its Start-delayed callback;
        // no item list, price, rarity or currentlyInteracting flag is fabricated.
        if(merchant.items==null || merchant.items.Count==0)merchant.FindItems();
        Save("shop-native-initialized.json",JsonSerializer.Serialize(new{name=merchant.name,fixture=merchantFixture!=null,canInteract=merchant.CanInteract(),items=merchant.items?.Count??0,prices=merchant.prices?.Count??0}));
    }
    [HideFromIl2Cpp] void MovePlayerAwayFromFixture()
    {
        if(golem==null)return;var player=MyPlayer.Instance;var away=player.transform.position-golem.transform.position;away.y=0;if(away.sqrMagnitude<.01f)away=Vector3.back;away.Normalize();
        if(Vector3.Distance(player.transform.position,golem.transform.position)<9)player.playerMovement.TeleportPlayer(Floor(golem.transform.position+away*13)+Vector3.up*1.1f);
    }
    [HideFromIl2Cpp] void CaptureGolem()
    {
        var camera=Camera.main;Require(camera!=null,"Native combat camera missing for golem evidence");var position=camera.transform.position;var rotation=camera.transform.rotation;
        try
        {
            var at=golem.collider.bounds.center;float height=Mathf.Max(3,golem.collider.bounds.size.y);camera.transform.position=at+new Vector3(height*1.4f,height*.5f,-height*2.7f);camera.transform.LookAt(at);
            var description=Json(Call(EnemyVisual,"Describe",golem));Save("golem-ground-and-visibility.json",description.GetRawText());
            Require(description.GetProperty("groundFound").GetBoolean() && F(description,"groundClearance")>=-.08f,"Source Golem is below its native ground");
            Require(description.GetProperty("canVisible").GetBoolean(),"Golem source body is absent from the native camera view");Capture("golem-native-floor.ppm");
        }
        finally{camera.transform.position=position;camera.transform.rotation=rotation;}
    }
    [HideFromIl2Cpp] bool HasCustomRunTime()
    {
        foreach(var transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if(transform==null || !transform.gameObject.scene.IsValid())continue;
            if(transform.name is "AzerothRunTime" or "AzerothRunTimer")return true;
            if(transform.name!="RunTime")continue;
            for(var parent=transform.parent;parent!=null;parent=parent.parent)if(parent.name.StartsWith("Azeroth",StringComparison.Ordinal))return true;
        }
        return false;
    }
    [HideFromIl2Cpp] void Save(string name,object content)=>File.WriteAllText(Path.Combine(output,name),content as string??JsonSerializer.Serialize(content));
    [HideFromIl2Cpp] void TrySave(string name,Func<string> content){try{Save(name,content());}catch{}}
    [HideFromIl2Cpp] void Capture(string name){BiomeCapture.Capture(relativeOutput+"/"+name);Plugin.Note("FEATURE CAPTURE "+Path.Combine(output,name));}
    [HideFromIl2Cpp] void Record(string outcome,string name,string detail)
    {
        if(outcome=="PASS")passed++;else if(outcome=="SKIP")skipped++;else failed++;
        results.Add(new{outcome,name,detail,wallSeconds=Time.realtimeSinceStartup-began});Plugin.Note("FEATURE "+outcome+" "+name+": "+detail);
        Save("summary.json",JsonSerializer.Serialize(new{passed,failed,skipped,results}));
    }
    [HideFromIl2Cpp] string CurrentName()=>index<steps.Count?steps[index].Name:"finished";
    [HideFromIl2Cpp] void Recover()
    {
        try{MyTime.SetTimeScale(1,0);}catch{}
        try{Call(MageVisual,"SetMotionForProbe","");}catch{}
        try{CloseNativeChoice();}catch{}
        try{UiAction("resume");}catch{}
        try{ResetAreasAndProjectiles();}catch{}
        expectNativeWindow=false;activePauseCase=null;
    }
    [HideFromIl2Cpp] void Finish()
    {
        if(done)return;done=true;Recover();
        try{Call(Tome,"RestoreOriginalTomesForProbe");Call(Tome,"SetAutomaticCastingForProbe",true);}catch(Exception error){Plugin.Note("FEATURE cleanup "+error.Message);}
        try
        {
            if(originalTalent!=null){using var saved=JsonDocument.Parse(originalTalent);Call("MageTalents.TalentBridge","SetProbeState",P(saved.RootElement,"TalentXp").GetInt64(),P(saved.RootElement,"Ranks").GetRawText());}
        }
        catch(Exception error){Plugin.Note("FEATURE talent fixture restore "+error.Message);}
        if(explicitlyPooledFx!=null)try{explicitlyPooledFx.Release();}catch{}
        if(merchantFixture!=null)Object.Destroy(merchantFixture);
        Plugin.Note("FEATURE COMPLETE passed="+passed+",failed="+failed+",skipped="+skipped+"; evidence="+output);Application.Quit(failed>0?2:0);
    }
}
