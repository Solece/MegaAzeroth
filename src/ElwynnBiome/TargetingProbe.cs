using System.Reflection;
using System.Text.Json;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Fixture only: real native enemies/tome/casts; no production acquisition override.
public sealed class TargetingProbe:MonoBehaviour
{
    public TargetingProbe(IntPtr pointer):base(pointer){}
    const string Target="AzerothAbilities.TargetAcquisitionDiagnostics",Ability="AzerothAbilities.ClassAbilityDiagnostics",Tome="AzerothAbilities.TomeCompatibilityDiagnostics",Talent="MageTalents.TalentBridge",Clock="AzerothAbilities.CombatSimulation";
    readonly List<Enemy> fixtures=new();readonly Dictionary<uint,Vector3> displaced=new();readonly List<object> checks=new();readonly Dictionary<string,Type> types=new();
    readonly List<(BaseInputModule Module,bool Enabled,int Id)> modules=new();
    internal static TargetingProbe Current;
    sealed class FlightActor {internal Enemy Enemy;internal uint Id;internal Vector3 Position;}
    readonly List<FlightActor> flightActors=new();readonly HashSet<uint> oldFixtureIds=new();
    bool creatingFlightActor,killedInFlight,sawMotion,sawSurvival,sawRetarget;int flightProjectile;uint flightKilled,flightVictim;float victimHp,flightHits,flightDamage;
    Vector3 flightStart,flightLast,fixedDestination;JsonElement flightLastSnapshot;readonly Dictionary<uint,float> survivorHealth=new();
    string folder,spell="fireball",talentState;JsonElement catalog,volley;int phase,passed,failed,skipped;float began,entered,sourceRange,aoeRadiusBefore;string areaId;bool done,fixturesReady;uint killedId;int volleyId;
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-targeting-smoke");
    void Start()
    {
        if(!Armed){enabled=false;return;}Current=this;began=entered=Time.realtimeSinceStartup;folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports","targeting-"+Environment.ProcessId));Directory.CreateDirectory(folder);
        try{Require(Environment.GetCommandLineArgs().Contains("-azeroth-test-battle"),"explicit isolated native battle/save guard");spell=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-azeroth-targeting-spell="))?.Substring("-azeroth-targeting-spell=".Length)??"fireball";}catch(Exception error){Finish(error);}
    }
    void Update()
    {
        if(done||!Armed)return;
        try
        {
            if(Time.realtimeSinceStartup-began>90)throw new TimeoutException("Target fixture exceeded90s");IsolateInput();var player=MyPlayer.Instance;
            if(player?.inventory?.playerHealth!=null){player.inventory.playerHealth.hp=player.inventory.playerHealth.maxHp;player.inventory.playerHealth.damageCooldown=10;}
            if(LevelupScreen.isLevelingUp)
            {var window=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(w=>w.gameObject.activeInHierarchy);if(window!=null){Save("incidental-native-xp-"+Time.frameCount+".json",new{level=player?.inventory?.playerXp?.level,xp=player?.inventory?.playerXp?.GetXpInt()});window.CloseLevelupScreen();Record("SKIP","incidental native XP choice","Closed real window; no upgrade selected.");}return;}
            if(phase>0){DisplaceOtherEnemies();PositionFixtures();}
            switch(phase)
            {
                case 0:
                    if(!ElwynnBridge.Ready||player?.inventory==null||!player.hasStarted||MapGenerationController.isGenerating||GameManager.Instance?.IsPlaying()!=true||GameManager.Instance.isGameOver)return;
                    Require((bool)Call(Tome,"SetAutomaticCastingForProbe",false),"disable automatic spell casts");
                    talentState=(string)Get(Talent,"StateJson");var state=J(talentState);Require((bool)Call(Talent,"SetProbeState",P(state,"TalentXp").GetInt64(),"{}"),"isolate source talent procs in memory");Require((bool)Call(Talent,"ApplyDebugNow"),"apply isolated talent fixture");
                    Require((bool)Call(Ability,"LearnAllForProbe"),"learn active source catalog");Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear prior source effects");catalog=J(Get(Ability,"ActiveSourceCatalogJson"));
                    var definition=P(catalog,"Spells").EnumerateArray().First(s=>S(s,"Id")==spell);sourceRange=P(definition,"Range").GetSingle();Require(sourceRange>8,"projectile fixture has usable source range");
                    areaId=P(catalog,"Spells").EnumerateArray().Where(s=>P(s,"Radius").GetSingle()>0).Select(s=>S(s,"Id")).FirstOrDefault();
                    if(areaId!=null)aoeRadiusBefore=P(J(Call(Target,"QueryTargetsForProbe",areaId,player.transform.position)),"DamageRadius").GetSingle();
                    Require((bool)Call(Tome,"UseIsolatedTomeForProbe","Quantity",3,5),"real native Quantity tome grants");Save("native-tome-after.json",Get(Tome,"SnapshotJson"));Save("target-policy.json",Get(Target,"PolicyJson"));
                    for(int i=0;i<5;i++){var enemy=EnemyManager.Instance.SpawnBoss((EEnemy)0,-1,EEnemyFlag.Boss,player.transform.position+Forward()*(2+2*i),1);Require(enemy!=null,"native durable target "+i);enemy.maxHp=10000000;enemy.hp=enemy.maxHp;fixtures.Add(enemy);}phase=1;entered=Time.realtimeSinceStartup;break;
                case 1:
                    if(fixtures.Any(e=>e.IsTeleporting())){if(Age>12)throw new TimeoutException("Native target spawn admission");return;}
                    if(Age<.5f||!(bool)Get(Clock,"CanSimulate"))return;
                    Save("all-five-native-ready.json",Positions());fixturesReady=true;PositionFixtures();
                    Require(fixtures.All(e=>!e.IsTeleporting()),"all five warmed native targets remain ready before cast");
                    Require((bool)Call(Tome,"CastSpellForProbe",spell),"real source multi-projectile cast");volley=J(Get(Target,"LastVolleyJson"));Save("actual-first-volley.json",Get(Target,"SnapshotJson"));ValidateVolley();
                    volleyId=P(volley,"VolleyId").GetInt32();killedId=fixtures[0].id;
                    // Introduce a real previously-out-of-range spare before native projectile Tick.
                    fixtures[3].transform.position=player.transform.position+Forward()*8;
                    var reserveQuery=J(Call(Target,"QueryTargetsForProbe",spell,player.transform.position));Save("reserve-ready-before-kill.json",new{query=reserveQuery,positions=Positions()});
                    Require(!fixtures[3].IsTeleporting()&&P(reserveQuery,"EligibleIds").EnumerateArray().Any(id=>id.GetUInt32()==fixtures[3].id),"real unclaimed reserve is ready and eligible before native Kill");
                    fixtures[0].Kill("TargetingProbe:RetargetFixture");phase=2;entered=Time.realtimeSinceStartup;break;
                case 2:
                    if(Age<.45f)return;Save("actual-retarget-events.json",Get(Target,"SnapshotJson"));var events=J(Get(Target,"RecentEventsJson")).EnumerateArray().Where(e=>P(e,"VolleyId").GetInt32()==volleyId).ToArray();
                    var retarget=events.Where(e=>S(e,"Kind")=="retarget-dead"&&P(e,"OldEnemy").GetUInt32()==killedId).ToArray();
                    Require(retarget.Length>0,"actual native projectile retargets dead target");Require(P(retarget[0],"NewEnemy").GetUInt32()==fixtures[3].id,"retarget chooses nearest unclaimed reserve target");
                    Require(retarget.All(e=>P(e,"RemainingRange").GetSingle()>0),"retarget retains finite remaining travel budget");
                    Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear projectiles before range query");phase=3;entered=Time.realtimeSinceStartup;break;
                case 3:
                    var query=J(Call(Target,"QueryTargetsForProbe",spell,player.transform.position));Save("range-boundaries.json",new{query,insideId=fixtures[3].id,outsideId=fixtures[4].id,positions=Positions()});
                    float raw=P(query,"SourceRange").GetSingle(),detection=P(query,"DetectionRange").GetSingle();Require(Mathf.Abs(detection-raw*1.2f)<.001f,"source detection range increased exactly20percent");
                    var eligible=P(query,"EligibleIds").EnumerateArray().Select(x=>x.GetUInt32()).ToArray();Require(eligible.Contains(fixtures[3].id),"real target at1.1sourceRange acquired");Require(!eligible.Contains(fixtures[4].id),"real target at1.21sourceRange excluded");
                    if(areaId!=null){var area=J(Call(Target,"QueryTargetsForProbe",areaId,player.transform.position));Save("area-radius-invariance.json",new{areaId,before=aoeRadiusBefore,after=area});Require(Mathf.Abs(P(area,"DamageRadius").GetSingle()-aoeRadiusBefore)<.001f,"Quantity and detection policy do not inflate computed AoE damage radius");Record("SKIP","AoE boundary hit proof","This fixture verifies the actual computed radius contract; it does not claim every natural AoE boundary hit was tested.");}
                    else Record("SKIP","AoE radius invariant","No active-class source radius spell.");
                    BiomeCapture.Capture("targeting-"+Environment.ProcessId+"/native-fixtures.ppm");
                    foreach(var e in fixtures)if(e!=null&&!e.IsDeadOrDyingNextFrame())oldFixtureIds.Add(e.id);
                    Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear old effects before arrival regression");
                    Require((bool)Call(Tome,"UseIsolatedTomeForProbe","Quantity",0,1),"isolate one-projectile arrival fixture using real empty tome inventory");
                    phase=4;PositionFixtures();DisplaceOtherEnemies();SpawnFlightActor(10);SpawnFlightActor(14);entered=Time.realtimeSinceStartup;break;
                case 4:
                    if(!FlightReady())return;BeginFlight(false);phase=5;entered=Time.realtimeSinceStartup;break;
                case 5:
                    if(!KillAfterRealMovement(false))return;phase=6;entered=Time.realtimeSinceStartup;break;
                case 6:
                    if(!ObserveFlight(false))return;
                    Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear completed retarget flight before destination-only fixture");
                    foreach(var actor in flightActors)if(Alive(actor))actor.Position=MyPlayer.Instance.transform.position-Forward()*(sourceRange*4+40);
                    SpawnFlightActor(10);phase=7;entered=Time.realtimeSinceStartup;break;
                case 7:
                    if(!FlightReady())return;BeginFlight(true);phase=8;entered=Time.realtimeSinceStartup;break;
                case 8:
                    if(!KillAfterRealMovement(true))return;phase=9;entered=Time.realtimeSinceStartup;break;
                case 9:
                    if(!ObserveFlight(true))return;Finish(null);break;
            }
        }
        catch(Exception error){Finish(error is TargetInvocationException invocation?invocation.InnerException??error:error);}
    }
    float Age=>Time.realtimeSinceStartup-entered;
    [HideFromIl2Cpp] void ValidateVolley()
    {
        Require(S(volley,"Spell")==spell,"actual diagnostic volley belongs to requested spell");int quantity=P(volley,"Quantity").GetInt32();var eligible=P(volley,"EligibleIds").EnumerateArray().Select(x=>x.GetUInt32()).ToArray();var assigned=P(volley,"AssignedIds").EnumerateArray().Select(x=>x.GetUInt32()).ToArray();
        Require(quantity>=4,"native Quantity tome produces at least4 projectiles");Require(eligible.Length==3,"only three arranged native targets in range",string.Join(",",eligible));
        Require(eligible.SequenceEqual(fixtures.Take(3).Select(e=>e.id)),"eligible targets sorted by actual nearest distance");Require(assigned.Take(3).Distinct().Count()==3&&assigned.Take(3).SequenceEqual(eligible),"first3 projectiles assigned to distinct nearest targets");
        Require(assigned.Length==quantity&&assigned.Select((id,i)=>id==eligible[i%eligible.Length]).All(v=>v)&&P(volley,"RoundRobinUsed").GetBoolean(),"excess projectiles assigned round robin");
        int id=P(volley,"VolleyId").GetInt32();var created=J(Get(Target,"RecentEventsJson")).EnumerateArray().Where(e=>S(e,"Kind")=="created"&&P(e,"VolleyId").GetInt32()==id).OrderBy(e=>P(e,"ProjectileIndex").GetInt32()).ToArray();
        Require(created.Length==quantity&&created.Select(e=>P(e,"NewEnemy").GetUInt32()).SequenceEqual(assigned),"actual projectile constructors match planned target IDs");Save("arranged-targets.json",Positions());
    }
    [HideFromIl2Cpp] void PositionFixtures()
    {
        var p=MyPlayer.Instance;if(p==null)return;
        if(phase>=4)
        {
            if(EnemyManager.Instance?.enemies!=null)foreach(uint id in oldFixtureIds)if(EnemyManager.Instance.enemies.ContainsKey(id)){var actor=EnemyManager.Instance.enemies[id];if(actor!=null&&!actor.IsDeadOrDyingNextFrame())actor.transform.position=p.transform.position-Forward()*(sourceRange*4+40)+Vector3.right*(id%7);}
            foreach(var actor in flightActors)if(Alive(actor))actor.Enemy.transform.position=actor.Position;
            return;
        }
        for(int i=0;i<fixtures.Count;i++)
        {var e=fixtures[i];if(phase>=2&&i==0)continue;if(e==null||e.IsDeadOrDyingNextFrame())continue;float distance=!fixturesReady||i<3?2+2*i:sourceRange*3+i;if(phase==2&&i==3)distance=8;if(phase>=3&&i==3)distance=sourceRange*1.1f;if(phase>=3&&i==4)distance=sourceRange*1.21f;e.transform.position=p.transform.position+Forward()*distance;}
    }
    [HideFromIl2Cpp] void DisplaceOtherEnemies()
    {
        var manager=EnemyManager.Instance;if(manager?.enemies==null)return;var at=MyPlayer.Instance.transform.position;
        foreach(var pair in manager.enemies)
        {var e=pair.Value;if(e==null||(phase<4?fixtures.Any(f=>f!=null&&f.id==pair.Key):flightActors.Any(f=>f.Id==pair.Key&&Alive(f))))continue;Displace(e);}
    }
    // Spawn interception is confined to the armed regression phases. It only
    // closes the fixture race where a natural spawn enters acquisition between
    // this Update and the real AbilityBehaviour.Tick; targeting itself is untouched.
    [HideFromIl2Cpp] internal static void IsolateSpawn(Enemy enemy){var p=Current;if(!Armed||p==null||p.done||p.phase<4||p.creatingFlightActor||enemy==null)return;p.Displace(enemy);}
    [HideFromIl2Cpp] void Displace(Enemy enemy){if(!displaced.ContainsKey(enemy.id))displaced[enemy.id]=enemy.transform.position;enemy.transform.position=MyPlayer.Instance.transform.position-Forward()*(sourceRange*4+40)+Vector3.right*(enemy.id%7);}
    [HideFromIl2Cpp] static bool Alive(FlightActor a){try{return a.Enemy!=null&&a.Enemy.id==a.Id&&!a.Enemy.IsDeadOrDyingNextFrame();}catch{return false;}}
    [HideFromIl2Cpp] void SpawnFlightActor(float distance)
    {
        var data=Resources.FindObjectsOfTypeAll<EnemyData>().FirstOrDefault(d=>d!=null&&(int)d.enemyName==0);Require(data!=null,"ordinary native Skeleton data available for arrival fixture");
        var at=MyPlayer.Instance.transform.position+Forward()*distance;Enemy enemy;
        creatingFlightActor=true;try{enemy=EnemyManager.Instance.SpawnEnemy(data,at,0,true,EEnemyFlag.None,false,1);}finally{creatingFlightActor=false;}
        Require(enemy!=null,"actual ordinary native flight target spawned",distance.ToString());enemy.maxHp=10000000;enemy.hp=enemy.maxHp;
        flightActors.Add(new FlightActor{Enemy=enemy,Id=enemy.id,Position=at});
    }
    [HideFromIl2Cpp] bool FlightReady()
    {
        if(Age>12)throw new TimeoutException("Ordinary flight targets not admitted: "+Get(Tome,"SnapshotJson"));
        return Age>=.5f&&flightActors.Where(Alive).All(a=>!a.Enemy.IsTeleporting())&&(bool)Get(Clock,"CanSimulate");
    }
    [HideFromIl2Cpp] void BeginFlight(bool destinationOnly)
    {
        var active=flightActors.Where(Alive).Where(a=>Vector3.Distance(MyPlayer.Instance.transform.position,a.Position)<sourceRange).OrderBy(a=>Vector3.Distance(MyPlayer.Instance.transform.position,a.Position)).ToArray();
        Require(active.Length==(destinationOnly?1:2),"controlled ready native arrival targets",active.Length.ToString());
        flightKilled=active[0].Id;flightVictim=destinationOnly?0:active[1].Id;victimHp=destinationOnly?0:active[1].Enemy.hp;
        var query=J(Call(Target,"QueryTargetsForProbe",spell,MyPlayer.Instance.transform.position));
        Require(P(query,"EligibleIds").EnumerateArray().Select(x=>x.GetUInt32()).SequenceEqual(active.Select(a=>a.Id)),"arrival fixture actual acquisition excludes natural bystanders");
        Require((bool)Call(Tome,"CastSpellWithSpeedForProbe",spell,3f),"real guarded slow source cast for "+(destinationOnly?"last destination":"retarget arrival"));
        var last=J(Get(Target,"LastVolleyJson"));int id=P(last,"VolleyId").GetInt32();var created=J(Get(Target,"RecentEventsJson")).EnumerateArray().Where(e=>S(e,"Kind")=="created"&&P(e,"VolleyId").GetInt32()==id).ToArray();
        Require(P(last,"Quantity").GetInt32()==1&&created.Length==1,"actual source cast constructs exactly one flight projectile");
        flightProjectile=P(created[0],"ProjectileId").GetInt32();Require(P(created[0],"NewEnemy").GetUInt32()==flightKilled,"real constructor chooses nearest controlled native target");
        flightStart=V(P(created[0],"Position"));flightLast=flightStart;fixedDestination=V(P(created[0],"Destination"));
        var counters=J(Get(Tome,"SnapshotJson"));flightHits=P(counters,"ProjectileHits").GetSingle();flightDamage=P(counters,"Damage").GetSingle();
        survivorHealth.Clear();if(EnemyManager.Instance?.enemies!=null)foreach(var pair in EnemyManager.Instance.enemies)if(pair.Key!=flightKilled&&pair.Value!=null&&!pair.Value.IsDeadOrDyingNextFrame())survivorHealth[pair.Key]=pair.Value.hp;
        killedInFlight=sawMotion=sawSurvival=sawRetarget=false;Save(FlightName(destinationOnly)+"-cast.json",new{query,targets=flightActors.Where(Alive).Select(a=>new{a.Id,a.Enemy.hp,position=A(a.Position)}),snapshot=J(Get(Target,"SnapshotJson")),counters});
    }
    [HideFromIl2Cpp] bool KillAfterRealMovement(bool destinationOnly)
    {
        if(Age>8)throw new TimeoutException("Projectile did not provide a controllable midflight sample");
        var live=LiveFlight();if(live.ValueKind==JsonValueKind.Undefined)throw new InvalidOperationException("Projectile vanished before controlled native Kill");
        GuardUnchanged(destinationOnly);var pos=LivePosition(live);if(Vector3.Distance(pos,flightStart)<.35f)return false;
        Require(Vector3.Distance(pos,V(P(live,"Destination")))>3,"native target killed strictly during travel, before endpoint");
        Require(P(live,"VisualActive").GetBoolean(),"real projectile visual active before native target death");
        fixedDestination=V(P(live,"LastValidDestination"));flightLast=pos;var actor=flightActors.First(a=>a.Id==flightKilled&&Alive(a));
        Save(FlightName(destinationOnly)+"-before-native-kill.json",new{live,counters=J(Get(Tome,"SnapshotJson")),nativeId=actor.Id,nativeHp=actor.Enemy.hp});
        actor.Enemy.Kill("TargetingProbe:"+(destinationOnly?"LastDestination":"ArrivalRetarget"));killedInFlight=true;return true;
    }
    [HideFromIl2Cpp] bool ObserveFlight(bool destinationOnly)
    {
        if(Age>14)throw new TimeoutException("Controlled native flight arrival not observed; "+Get(Tome,"SnapshotJson"));
        var events=J(Get(Target,"RecentEventsJson")).EnumerateArray().Where(e=>P(e,"ProjectileId").GetInt32()==flightProjectile).ToArray();
        var arrivals=events.Where(e=>S(e,"Kind")=="arrival-impact").ToArray();var live=LiveFlight();
        if(arrivals.Length==0)
        {
            if(live.ValueKind==JsonValueKind.Undefined)throw new InvalidOperationException("Same projectile despawned before any real arrival-impact event");
            GuardUnchanged(destinationOnly);
            if(events.Any(e=>S(e,"Kind") is "hit" or "hit-dot"))throw new InvalidOperationException("Real hit callback occurred before arrival");
            var pos=LivePosition(live);if(Vector3.Distance(pos,flightLast)>.02f)sawMotion=true;flightLast=pos;
            if(destinationOnly)
            {
                if(P(live,"DestinationOnly").GetBoolean())
                {if(Vector3.Distance(V(P(live,"Destination")),fixedDestination)>.002f)throw new InvalidOperationException("Last destination changed after native target death");if(P(live,"TargetAlive").GetBoolean())throw new InvalidOperationException("Destination-only flight retained a living target");sawRetarget=events.Any(e=>S(e,"Kind")=="ballistic-destination");}
            }
            else if(events.Any(e=>S(e,"Kind")=="retarget-dead"&&P(e,"OldEnemy").GetUInt32()==flightKilled&&P(e,"NewEnemy").GetUInt32()==flightVictim))sawRetarget=true;
            if(Age>.2f&&sawRetarget&&sawMotion)
            {
                if(!P(live,"VisualActive").GetBoolean())throw new InvalidOperationException("Projectile visual became inactive during continued travel");
                if(Vector3.Distance(V(P(live,"VisualPosition")),pos)>.05f)throw new InvalidOperationException("Visual no longer follows real projectile position");
                if(!sawSurvival){var meshes=NativeFlightMeshes(pos);if(meshes.Length==0)throw new InvalidOperationException("Active projectile wrapper has no enabled native mesh renderer with geometry/material");Save(FlightName(destinationOnly)+"-continued-flight.json",new{live,events,meshes,counters=J(Get(Tome,"SnapshotJson"))});BiomeCapture.Capture("targeting-"+Environment.ProcessId+"/"+FlightName(destinationOnly)+"-flight.ppm");}sawSurvival=true;
            }
            return false;
        }
        var arrival=arrivals[0];Save(FlightName(destinationOnly)+"-arrival.json",new{events,snapshot=J(Get(Target,"SnapshotJson")),counters=J(Get(Tome,"SnapshotJson")),survivingHealth=NativeSurvivorHealth()});
        Require(killedInFlight&&sawRetarget&&sawMotion&&sawSurvival,"same projectile survives target death and visibly travels before "+FlightName(destinationOnly)+" arrival");
        Require(Vector3.Distance(V(P(arrival,"ArrivalProjectilePosition")),V(P(arrival,"Destination")))<.002f,"actual projectile reaches exact destination before impact callback");
        Require(P(arrival,"VisualActive").GetBoolean()&&Vector3.Distance(V(P(arrival,"VisualPosition")),V(P(arrival,"Destination")))<.05f,"actual visible wrapper reaches destination before impact callback");
        Require(events.Any(e=>S(e,"Kind")=="target-lost"&&P(e,"OldEnemy").GetUInt32()==flightKilled),"actual native target loss recorded for same projectile");
        if(destinationOnly)
        {
            Require(!P(arrival,"AppliesDamage").GetBoolean()&&!P(arrival,"TargetAlive").GetBoolean()&&S(arrival,"Outcome")=="last-destination-no-damage","destination-only arrival is a real nondamaging impact");
            Require(Vector3.Distance(V(P(arrival,"Destination")),fixedDestination)<.002f,"arrival uses original last valid world destination");
            Require(!events.Any(e=>S(e,"Kind") is "hit" or "hit-dot"),"no real hit callback exists for the dead-target destination");GuardUnchanged(true);Require(true,"destination-only flight and arrival cause no native survivor HP loss or source damage");
        }
        else
        {
            Require(P(arrival,"AppliesDamage").GetBoolean()&&P(arrival,"TargetAlive").GetBoolean()&&P(arrival,"NewEnemy").GetUInt32()==flightVictim,"retargeted arrival applies to actual live reserve");
            Require(events.Any(e=>S(e,"Kind")=="hit"&&P(e,"NewEnemy").GetUInt32()==flightVictim),"actual same-projectile hit callback follows retargeted arrival");
            var victim=flightActors.First(a=>a.Id==flightVictim);var counters=J(Get(Tome,"SnapshotJson"));
            Require(Alive(victim)&&victim.Enemy.hp<victimHp&&P(counters,"ProjectileHits").GetSingle()>flightHits&&P(counters,"Damage").GetSingle()>flightDamage,"native reserve HP and real source damage change only after arrival");
        }
        BiomeCapture.Capture("targeting-"+Environment.ProcessId+"/"+FlightName(destinationOnly)+"-arrival.ppm");return true;
    }
    [HideFromIl2Cpp] void GuardUnchanged(bool destinationOnly)
    {
        var counters=J(Get(Tome,"SnapshotJson"));if(P(counters,"ProjectileHits").GetSingle()!=flightHits||P(counters,"Damage").GetSingle()!=flightDamage)throw new InvalidOperationException("Source hit/damage counter changed before permitted arrival");
        if(!destinationOnly){var actor=flightActors.First(a=>a.Id==flightVictim);if(!Alive(actor)||actor.Enemy.hp!=victimHp)throw new InvalidOperationException("Native reserve HP changed before projectile arrived");}
        else foreach(var row in NativeSurvivorHealth())if(survivorHealth.TryGetValue(row.Key,out float hp)&&row.Value<hp)throw new InvalidOperationException("Native surviving actor lost HP during nondamaging destination flight: "+row.Key);
    }
    [HideFromIl2Cpp] Dictionary<uint,float> NativeSurvivorHealth(){var result=new Dictionary<uint,float>();if(EnemyManager.Instance?.enemies!=null)foreach(var pair in EnemyManager.Instance.enemies)if(pair.Key!=flightKilled&&pair.Value!=null&&!pair.Value.IsDeadOrDyingNextFrame())result[pair.Key]=pair.Value.hp;return result;}
    [HideFromIl2Cpp] object[] NativeFlightMeshes(Vector3 position)
    {
        var rows=new List<object>();foreach(var renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            if(renderer==null||!renderer.enabled||!renderer.gameObject.activeInHierarchy||renderer.forceRenderingOff)continue;
            var parent=renderer.transform;while(parent!=null&&parent.name!="AzerothProjectileVfx_"+spell)parent=parent.parent;
            if(parent==null||Vector3.Distance(parent.position,position)>.05f)continue;
            var filter=renderer.GetComponent<MeshFilter>();var mesh=filter?.sharedMesh;var material=renderer.sharedMaterial;
            if(mesh==null||mesh.vertexCount==0||material==null||material.shader==null)continue;
            rows.Add(new{renderer=renderer.name,vertices=mesh.vertexCount,shader=material.shader.name,position=A(parent.position),boundsCenter=A(renderer.bounds.center),boundsSize=A(renderer.bounds.size),renderer.isVisible});
        }
        return rows.ToArray();
    }
    [HideFromIl2Cpp] JsonElement LiveFlight(){var state=J(Get(Target,"SnapshotJson"));flightLastSnapshot=state;return P(state,"LiveProjectiles").EnumerateArray().FirstOrDefault(e=>P(e,"ProjectileId").GetInt32()==flightProjectile);}
    [HideFromIl2Cpp] static string FlightName(bool destinationOnly)=>destinationOnly?"last-destination":"retarget-arrival";
    [HideFromIl2Cpp] static Vector3 V(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    [HideFromIl2Cpp] static float[] A(Vector3 p)=>new[]{p.x,p.y,p.z};
    [HideFromIl2Cpp] static Vector3 LivePosition(JsonElement e)=>new(P(e,"X").GetSingle(),P(e,"Y").GetSingle(),P(e,"Z").GetSingle());
    [HideFromIl2Cpp] object Positions()=>fixtures.Where(e=>e!=null).Select(e=>new{e.id,e.hp,dead=e.IsDeadOrDyingNextFrame(),teleporting=e.IsTeleporting(),position=new[]{e.transform.position.x,e.transform.position.y,e.transform.position.z},distance=Vector3.Distance(MyPlayer.Instance.transform.position,e.transform.position)}).ToArray();
    [HideFromIl2Cpp] void IsolateInput(){foreach(var m in Resources.FindObjectsOfTypeAll<BaseInputModule>()){try{if(m==null||!m.gameObject.scene.IsValid())continue;int id=m.GetInstanceID();if(!modules.Any(s=>s.Id==id))modules.Add((m,m.enabled,id));m.enabled=false;}catch{ }}}
    [HideFromIl2Cpp] static Vector3 Forward(){var f=MyPlayer.Instance.transform.forward;f.y=0;return f.sqrMagnitude>.001f?f.normalized:Vector3.forward;}
    [HideFromIl2Cpp] Type Named(string name){if(types.TryGetValue(name,out var type))return type;return types[name]=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null)??throw new MissingMemberException(name);}
    [HideFromIl2Cpp] object Get(string type,string property)=>Named(type).GetProperty(property)?.GetValue(null)??throw new MissingMemberException(type,property);
    [HideFromIl2Cpp] object Call(string type,string method,params object[] args)=>Named(type).GetMethods(BindingFlags.Public|BindingFlags.Static).First(m=>m.Name==method&&m.GetParameters().Length==args.Length).Invoke(null,args);
    [HideFromIl2Cpp] static JsonElement J(object value){using var d=JsonDocument.Parse(value as string??JsonSerializer.Serialize(value));return d.RootElement.Clone();}
    [HideFromIl2Cpp] static JsonElement P(JsonElement value,string name){foreach(var p in value.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase))return p.Value;throw new MissingMemberException(name);}
    [HideFromIl2Cpp] static string S(JsonElement value,string name)=>P(value,name).GetString();
    [HideFromIl2Cpp] void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),value as string??JsonSerializer.Serialize(value));
    [HideFromIl2Cpp] void Record(string status,string label,string detail=""){if(status=="PASS")passed++;else if(status=="FAIL")failed++;else skipped++;checks.Add(new{status,label,detail});Plugin.Note("TARGETING "+status+" "+label+" "+detail);}
    [HideFromIl2Cpp] void Require(bool value,string label,string detail=""){Record(value?"PASS":"FAIL",label,detail);if(!value)throw new InvalidOperationException(label+" "+detail);}
    [HideFromIl2Cpp] void Finish(Exception error)
    {
        if(done)return;done=true;Current=null;if(error!=null){Record("FAIL","fixture exception",error.ToString());try{Save("failure-flight-context.json",new{phase,flightProjectile,killedInFlight,sawMotion,sawSurvival,sawRetarget,last=flightLastSnapshot.ValueKind==JsonValueKind.Undefined?(object)null:flightLastSnapshot,current=J(Get(Target,"SnapshotJson")),nativeClock=J(Get(Tome,"SnapshotJson")),levelup=LevelupScreen.isLevelingUp});}catch{}}
        try
        {
            Call(Tome,"RestoreOriginalTomesForProbe");if(talentState!=null){var state=J(talentState);Call(Talent,"SetProbeState",P(state,"TalentXp").GetInt64(),P(state,"Ranks").GetRawText());Call(Talent,"ApplyDebugNow");}
            if(EnemyManager.Instance?.enemies!=null)foreach(var pair in displaced)if(EnemyManager.Instance.enemies.ContainsKey(pair.Key)&&EnemyManager.Instance.enemies[pair.Key]!=null)EnemyManager.Instance.enemies[pair.Key].transform.position=pair.Value;
        }
        catch(Exception cleanup){Record("FAIL","fixture cleanup",cleanup.ToString());}
        foreach(var saved in modules){try{if(saved.Module!=null&&saved.Module.gameObject!=null)saved.Module.enabled=saved.Enabled;}catch{ }}
        Save("summary.json",new{pass=failed==0,passed,failed,skipped,phase,spell,wallSeconds=Time.realtimeSinceStartup-began,checks,error=error?.ToString(),fixturePolicy="Real native five-enemy Quantity fixture, then ordinary Skeleton targets and genuine single-projectile source casts with guarded speed3 constructor override. Actual Enemy.Kill, same projectile movement/visual positions, arrival events, native survivor HP and source counters. Other native enemies temporarily moved outside acquisition range and restored; source talent procs isolated and restored. Armed regression-only spawn displacement closes natural-spawn fixture races. No production targeting override, saved progression or fabricated projectile telemetry."});Application.Quit(failed==0?0:2);
    }
}

[HarmonyPatch(typeof(EnemyManager),nameof(EnemyManager.SpawnEnemy),new[]{typeof(EnemyData),typeof(Vector3),typeof(int),typeof(bool),typeof(EEnemyFlag),typeof(bool),typeof(float)})]
static class TargetingFlightSpawnIsolation
{static bool Prepare()=>TargetingProbe.Armed;static void Postfix(Enemy __result)=>TargetingProbe.IsolateSpawn(__result);}

[HarmonyPatch]
static class TargetingButtonIsolation
{static bool Prepare()=>TargetingProbe.Armed;static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));static bool Prefix(ref bool __result){__result=false;return false;}}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class TargetingAxisIsolation
{static bool Prepare()=>TargetingProbe.Armed;static bool Prefix(ref float __result){__result=0;return false;}}
