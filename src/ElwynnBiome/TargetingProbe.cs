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
    string folder,spell="fireball",talentState;JsonElement catalog,volley;int phase,passed,failed,skipped;float began,entered,sourceRange,aoeRadiusBefore;string areaId;bool done,fixturesReady;uint killedId;int volleyId;
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-targeting-smoke");
    void Start()
    {
        if(!Armed){enabled=false;return;}began=entered=Time.realtimeSinceStartup;folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports","targeting-"+Environment.ProcessId));Directory.CreateDirectory(folder);
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
                    BiomeCapture.Capture("targeting-"+Environment.ProcessId+"/native-fixtures.ppm");Finish(null);break;
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
        for(int i=0;i<fixtures.Count;i++)
        {var e=fixtures[i];if(phase>=2&&i==0)continue;if(e==null||e.IsDeadOrDyingNextFrame())continue;float distance=!fixturesReady||i<3?2+2*i:sourceRange*3+i;if(phase==2&&i==3)distance=8;if(phase>=3&&i==3)distance=sourceRange*1.1f;if(phase>=3&&i==4)distance=sourceRange*1.21f;e.transform.position=p.transform.position+Forward()*distance;}
    }
    [HideFromIl2Cpp] void DisplaceOtherEnemies()
    {
        var manager=EnemyManager.Instance;if(manager?.enemies==null)return;var at=MyPlayer.Instance.transform.position;
        foreach(var pair in manager.enemies)
        {var e=pair.Value;if(e==null||fixtures.Any(f=>f!=null&&f.id==pair.Key))continue;if(!displaced.ContainsKey(pair.Key))displaced[pair.Key]=e.transform.position;e.transform.position=at-Forward()*(sourceRange*3+20)+Vector3.right*(pair.Key%7);}
    }
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
        if(done)return;done=true;if(error!=null)Record("FAIL","fixture exception",error.ToString());
        try
        {
            Call(Tome,"RestoreOriginalTomesForProbe");if(talentState!=null){var state=J(talentState);Call(Talent,"SetProbeState",P(state,"TalentXp").GetInt64(),P(state,"Ranks").GetRawText());Call(Talent,"ApplyDebugNow");}
            if(EnemyManager.Instance?.enemies!=null)foreach(var pair in displaced)if(EnemyManager.Instance.enemies.ContainsKey(pair.Key)&&EnemyManager.Instance.enemies[pair.Key]!=null)EnemyManager.Instance.enemies[pair.Key].transform.position=pair.Value;
        }
        catch(Exception cleanup){Record("FAIL","fixture cleanup",cleanup.ToString());}
        foreach(var saved in modules){try{if(saved.Module!=null&&saved.Module.gameObject!=null)saved.Module.enabled=saved.Enabled;}catch{ }}
        Save("summary.json",new{pass=failed==0,passed,failed,skipped,phase,spell,wallSeconds=Time.realtimeSinceStartup-began,checks,error=error?.ToString(),fixturePolicy="Real native five-enemy fixture and real Quantity tome. Other native enemies temporarily moved outside acquisition range and restored; source talent procs isolated and restored. No production targeting override, saved progression or fabricated projectile telemetry."});Application.Quit(failed==0?0:2);
    }
}

[HarmonyPatch]
static class TargetingButtonIsolation
{static bool Prepare()=>TargetingProbe.Armed;static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));static bool Prefix(ref bool __result){__result=false;return false;}}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class TargetingAxisIsolation
{static bool Prepare()=>TargetingProbe.Armed;static bool Prefix(ref float __result){__result=0;return false;}}
