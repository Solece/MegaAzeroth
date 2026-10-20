using System.Reflection;
using System.Text.Json;
using Actors.Enemies;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Managers;
using Assets.Scripts.Menu.Shop;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// Library-only, opt-in native regression fixture. It never dispatches a talent
// event/proc manually. Native healing, native Damage and actual source casts own
// all observed effects. Each run is an isolated, save-suppressed test battle.
public sealed class VitalsProbe:MonoBehaviour
{
    public VitalsProbe(IntPtr pointer):base(pointer){}
    const string Hero="AzerothHeroes.HeroBridge",Hud="AzerothAbilities.HudBridge",Combat="AzerothAbilities.ClassCombat";
    const string Ability="AzerothAbilities.ClassAbilityDiagnostics",Tome="AzerothAbilities.TomeCompatibilityDiagnostics";
    const string Talents="MageTalents.TalentBridge",TalentRuntime="MageTalents.TalentRuntime",Clock="AzerothAbilities.CombatSimulation";
    readonly Dictionary<string,Type> types=new();
    readonly List<object> checks=new();
    readonly List<SourceEffect> effects=new();
    sealed class SourceEffect {internal string TalentId;internal int Tag,Rank;internal JsonElement Payload;}
    string folder,classId,barrierSpell;
    float began,entered;
    int phase,passed,failed,skipped,initialHp;bool done,initialized,originalStatsSaved,terminalDamage;
    float initialShield,initialOverheal,initialCarry,originalEvasion,originalHealing;
    bool hadEvasion,hadHealing;
    Enemy target;
    double cheatBaseline;
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-vitals-smoke");
    PlayerHealth Health=>MyPlayer.Instance?.inventory?.playerHealth;
    float Age=>Time.realtimeSinceStartup-entered;
    void Start()
    {
        if(!Armed){enabled=false;return;}
        began=entered=Time.realtimeSinceStartup;
        try
        {
            folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports","vitals-"+Environment.ProcessId));Directory.CreateDirectory(folder);
            var args=Environment.GetCommandLineArgs();
            Require(args.Contains("-azeroth-test-battle")&&args.Contains("-azeroth-talents-max")&&args.Contains("-azeroth-talent-points=999"),"explicit isolated max999 test battle");
            Require(!args.Contains("-azeroth-feature-smoke")&&!args.Contains("-azeroth-hero-class-smoke")&&!args.Contains("-azeroth-talent-tree-smoke"),"exclusive vitals fixture owns test health");
            classId=args.FirstOrDefault(a=>a.StartsWith("-azeroth-hero=",StringComparison.Ordinal))?.Substring("-azeroth-hero=".Length);
            Require(!string.IsNullOrEmpty(classId),"explicit actual source class CLI");
            Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
            Plugin.Note("VITALS BEGIN "+classId+"; deadline120s; evidence="+folder);
        }
        catch(Exception error){Finish(error);}
    }
    void Update()
    {
        if(!Armed||done)return;
        try
        {
            if(Time.realtimeSinceStartup-began>120)throw new TimeoutException("Vitals deadline120s at phase="+phase);
            if(phase==0)
            {
                if(!ElwynnBridge.Ready||Health==null||MyPlayer.Instance?.hasStarted!=true||MapGenerationController.isGenerating||GameManager.Instance?.IsPlaying()!=true||LevelupScreen.isLevelingUp)return;
                Initialize();Next();return;
            }
            Require((bool)Get(Clock,"CanSimulate"),"native source combat clock admits scenario");
            switch(phase)
            {
                case 1:FractionalHealing("fractional-native-multiplier",null);Next();break;
                case 2:FractionalHealing("fractional-half-multiplier",.5f);Next();break;
                case 3:FullHealthOverheal();Next();break;
                case 4:NativeShieldAdmission();Next();break;
                case 5:PrepareBarrier();Next();break;
                case 6:
                    if(target!=null&&target.IsTeleporting()){if(Age>12)throw new TimeoutException("Native barrier target spawn timed out");return;}
                    if(barrierSpell!=null)
                    {Require((bool)Call(Tome,"CastSpellForProbe",barrierSpell),"actual source barrier-producing cast",barrierSpell);barrierSpell=null;}
                    if(Shield()<=.001f&&Age<3)return;
                    AbsorbedHit();Next();break;
                case 7:NativeOverhealAdmission();Next();break;
                case 8:if(FirstMortalHit())Next();else Finish(null);break;
                case 9:RepeatedMortalHit();Finish(null);break;
            }
        }
        catch(Exception error){Finish(Unwrap(error));}
    }
    [HideFromIl2Cpp] void Next(){phase++;entered=Time.realtimeSinceStartup;}
    [HideFromIl2Cpp] void Initialize()
    {
        Require((string)Get(Hero,"ActiveClassId")==classId&&(string)Get(Hud,"ClassId")==classId&&(string)Get(Combat,"ClassId")==classId,"actual spawned hero matches CLI",classId);
        Require((bool)Get(Combat,"GenericActive"),"actual generic source combat active");
        Require((int)Get(Talents,"TemporaryPointBudget")==999,"temporary test budget999");
        var catalog=J(Get(Talents,"CatalogJson"));Require(S(catalog,"ClassId")==classId,"actual current-class talent catalog");
        foreach(var node in P(catalog,"Talents").EnumerateArray())
        {
            string id=S(node,"Id");int rank=(int)Call(Talents,"Rank",id);
            Require(rank==I(node,"MaxRank"),"actual maxed source talent",id+" rank="+rank);
            foreach(var effect in P(node,"Effects").EnumerateArray())effects.Add(new SourceEffect{TalentId=id,Rank=rank,Tag=I(effect,"Tag"),Payload=effect.Clone()});
        }
        Require((bool)Call(Tome,"SetAutomaticCastingForProbe",false),"disable automatic source cast fixtures");
        Require((bool)Call(Ability,"LearnAllForProbe"),"learn actual source class catalog");
        var h=Health;initialHp=h.hp;initialShield=h.shield;initialOverheal=h.overheal;initialCarry=h.leftOverHeal;
        var stats=MyPlayer.Instance.inventory.playerStats.stats;
        hadEvasion=stats.ContainsKey(EStat.Evasion);originalEvasion=hadEvasion?stats[EStat.Evasion]:0;
        hadHealing=stats.ContainsKey(EStat.HealingMultiplier);originalHealing=hadHealing?stats[EStat.HealingMultiplier]:0;
        originalStatsSaved=true;stats[EStat.Evasion]=0;
        Require(Mathf.Abs(PlayerStats.GetStat(EStat.Evasion))<.00001f,"native evasion fixture is zero without damage-bypass flags");
        Require(!MyPlayer.Instance.inventory.statusEffects.HasStatusEffect((EStatusEffect)8),"initial native healing is not bleed-blocked");
        initialized=true;cheatBaseline=Applied("cheatDeath");
        Save("source-talent-fixtures.json",new{classId,overheal=effects.Where(e=>e.Tag==20).Select(e=>new{e.TalentId,e.Rank,e.Payload}),cheatDeath=effects.Where(e=>e.Tag==19).Select(e=>new{e.TalentId,e.Rank,e.Payload}),note="Priest has no original effect20 or19; absent source effects are skipped, not fabricated."});
        Save("initialized.json",Snapshot());
    }
    [HideFromIl2Cpp] void ResetFixture(bool full=false)
    {
        Require((bool)Call(Ability,"ClearEffectsForProbe"),"clear source effects through guarded fixture");
        var h=Health;h.hp=full?h.maxHp:Math.Max(2,h.maxHp/2);h.shield=0;h.overheal=0;h.leftOverHeal=0;h.damageCooldown=0;
    }
    [HideFromIl2Cpp] void FractionalHealing(string label,float? multiplier)
    {
        ResetFixture();var h=Health;var stats=MyPlayer.Instance.inventory.playerStats.stats;
        float prior=stats[EStat.HealingMultiplier];if(multiplier.HasValue)stats[EStat.HealingMultiplier]=multiplier.Value;
        try
        {
            float effective=PlayerStats.GetStat(EStat.HealingMultiplier);Require(effective>0&&effective<10,"bounded actual native healing multiplier",effective.ToString());
            if(multiplier.HasValue)Equal(effective,multiplier.Value,.0001,"half multiplier reaches native GetStat");
            var before=Snapshot();int beforeHp=h.hp;float carry=h.leftOverHeal,shield=Shield();double proc=Applied("overhealShield");
            double totalRequested=0,totalAccepted=0,totalReturn=0;int zeroDelta=0;var calls=new List<JsonElement>();
            for(int i=0;i<20;i++)
            {
                float actual=Convert.ToSingle(Call(Combat,"Heal",.1f,"vitals-"+label+"-"+i));
                var sample=J(Get(Combat,"LastHealJson"));calls.Add(sample);totalReturn+=actual;if(actual==0)zeroDelta++;
                totalRequested+=D(sample,"ScaledRequested");totalAccepted+=D(sample,"AcceptedFractionalHealing");
                Require(!B(sample,"NativeBleedOrDead"),"fractional native heal accepted",label+"/"+i);
                Require(D(sample,"Overheal")<=.000001,"partial fractional healing is not overheal",label+"/"+i);
            }
            Save(label+".json",new{before,after=Snapshot(),calls,totalRequested,totalAccepted,totalReturn,zeroDelta});
            Require(zeroDelta>0,"fractional fixture includes real zero-integer-HP heal calls",label);
            Equal(totalAccepted,totalRequested,.0001,"partial heal accepts all scaled fractions");
            Equal(h.hp-beforeHp+h.leftOverHeal-carry,totalRequested,.0001,"native HP plus carry conserves fractional healing");
            Equal(totalReturn,h.hp-beforeHp,.0001,"Heal return is real native integer HP gain");
            Equal(Shield(),shield,.000001,"partial fractional healing creates no source shield");
            Equal(Applied("overhealShield"),proc,0,"partial fractional healing dispatches no overheal-shield proc");
        }
        finally{stats[EStat.HealingMultiplier]=prior;}
    }
    [HideFromIl2Cpp] void FullHealthOverheal()
    {
        var original=effects.FirstOrDefault(e=>e.Tag==20&&e.Rank>0);
        if(original==null){Record("SKIP","real full-health overheal talent",classId+" source catalog contains no effect20; use Druid Tree of Life for this scenario.");return;}
        ResetFixture(true);var h=Health;var before=Snapshot();float cap=(float)D(original.Payload,"Cap")*h.maxHp;
        float multiplier=PlayerStats.GetStat(EStat.HealingMultiplier),input=h.maxHp*.05f;double proc=Applied("overhealShield");
        float actual=Convert.ToSingle(Call(Combat,"Heal",input,"vitals-full-overheal-small"));var first=J(Get(Combat,"LastHealJson"));
        Require(actual==0&&h.hp==h.maxHp,"full-health native heal adds no HP");Equal(D(first,"AcceptedFractionalHealing"),0,.000001,"full-health accepted fraction is zero");
        Equal(Shield(),Mathf.Min(cap,input*multiplier),.001,"real source overheal shield equals excess within original cap");
        Require(SourceShieldHas(original.TalentId),"source shield belongs to original selected talent",original.TalentId);
        Call(Combat,"Heal",h.maxHp*4f,"vitals-full-overheal-cap");
        Equal(Shield(),cap,.001,"real source overheal shield respects original cap");Equal(Applied("overhealShield"),proc+2,0,"two real full-health heals dispatch two original shield procs");
        Save("full-health-source-overheal.json",new{original.TalentId,original.Rank,cap,before,first,after=Snapshot()});
    }
    [HideFromIl2Cpp] void NativeShieldAdmission()
    {
        ResetFixture();var h=Health;h.shield=Mathf.Max(10,h.maxHp*.1f);int hp=h.hp;double cheat=Applied("cheatDeath");var before=Snapshot();
        var incoming=NativeHit(1000123f,"native-shield-huge-hit");
        Require(B(incoming,"NativeShieldBlocks")&&!B(incoming,"CheatDeathChecked")&&!B(incoming,"CheatDeathPrevented"),"positive native shield bypasses source lethality check");
        Require(h.hp==hp&&h.shield==0,"positive native shield absorbs complete huge admitted native hit");
        Equal(Applied("cheatDeath"),cheat,0,"native shield does not consume source cheat-death proc");
        Save("native-shield-huge-hit.json",new{before,after=Snapshot(),incoming});
    }
    [HideFromIl2Cpp] void PrepareBarrier()
    {
        ResetFixture();barrierSpell=null;
        if(classId=="priest")barrierSpell="pw_shield";
        else if(classId=="paladin")
        {
            barrierSpell="consecration";
            var p=MyPlayer.Instance;var at=p.transform.position+p.transform.forward*3;
            if(Physics.Raycast(at+Vector3.up*100,Vector3.down,out var hit,240,GameManager.Instance.whatIsGround.value,QueryTriggerInteraction.Ignore))at=hit.point+Vector3.up*.1f;
            target=EnemyManager.Instance.SpawnBoss((EEnemy)0,-1,EEnemyFlag.Boss,at,1);
            Require(target!=null,"native source-barrier cast target spawned");target.maxHp=10000000;target.hp=target.maxHp;
        }
        else if(effects.Any(e=>e.Tag==20&&e.Rank>0))
        {Health.hp=Health.maxHp;Call(Combat,"Heal",Health.maxHp*.1f,"vitals-real-overheal-barrier");}
        else Record("SKIP","real source barrier fixture",classId+" has no selected supported original barrier producer in this probe.");
        Save("source-barrier-prepare.json",Snapshot());
    }
    [HideFromIl2Cpp] void AbsorbedHit()
    {
        float source=Shield();if(source<=.001f)
        {
            if(classId is "priest" or "paladin"||effects.Any(e=>e.Tag==20&&e.Rank>0))Require(false,"actual source barrier was created before native hit");
            Record("SKIP","native hit absorbed by source barrier","No supported original source shield was active.");return;
        }
        var h=Health;h.shield=0;h.overheal=0;int hp=h.hp;var before=Snapshot();
        var incoming=NativeHit(Mathf.Max(1,source*.25f),"source-barrier-native-hit");
        Require(!B(incoming,"NativeShieldBlocks")&&D(incoming,"SourceShieldAbsorbed")>0,"actual source barrier absorbs admitted native damage");
        Require(h.hp==hp,"source barrier absorbs before native player HP");
        Require(Shield()<source&&Shield()>0,"source barrier balance actually decreases without breaking");
        Save("source-barrier-native-hit.json",new{before,after=Snapshot(),incoming});
    }
    [HideFromIl2Cpp] void NativeOverhealAdmission()
    {
        ResetFixture();var h=Health;int hp=h.hp;h.overheal=h.maxHp*4;double cheat=Applied("cheatDeath");var before=Snapshot();
        var incoming=NativeHit(h.maxHp*1.25f,"native-overheal-covered-hit");
        Require(D(incoming,"NativeOverhealCovered")>0&&!B(incoming,"CheatDeathChecked"),"native overheal coverage is excluded from source lethality view");
        Require(h.hp==hp&&h.overheal<h.maxHp*4,"real native overheal absorbs admitted hit before HP");
        Equal(Applied("cheatDeath"),cheat,0,"native overheal does not consume source cheat-death proc");
        Save("native-overheal-covered-hit.json",new{before,after=Snapshot(),incoming});
    }
    [HideFromIl2Cpp] bool FirstMortalHit()
    {
        var original=effects.FirstOrDefault(e=>e.Tag==19&&e.Rank>0);
        if(original==null){Record("SKIP","real source cheat-death and internal cooldown",classId+" source catalog contains no effect19; use Paladin Divine Intervention, Rogue, Shaman or Warrior.");return false;}
        ResetFixture();var h=Health;h.hp=Math.Max(2,h.maxHp/5);var before=Snapshot();double count=Applied("cheatDeath");
        Equal(count,cheatBaseline,0,"earlier native shields preserved unused source cheat-death proc");
        var incoming=NativeHit(h.maxHp*20f+17,"first-mortal-hit");
        Require(B(incoming,"CheatDeathChecked")&&B(incoming,"CheatDeathPrevented"),"real admitted mortal hit triggers original source cheat-death");
        Require(h.hp>0&&!h.IsDead(),"native player survives source cheat-death");
        Equal(h.hp,h.maxHp*D(original.Payload,"Value"),1.1,"surviving HP follows original source recovery fraction");
        Equal(Applied("cheatDeath"),count+1,0,"one mortal native hit causes exactly one source cheat-death proc");
        Save("first-mortal-hit.json",new{original.TalentId,original.Rank,sourceCooldown=D(original.Payload,"Interval"),before,after=Snapshot(),incoming});
        return true;
    }
    [HideFromIl2Cpp] void RepeatedMortalHit()
    {
        // Last scenario intentionally ends this isolated save-suppressed run.
        // ClearEffects resets source invulnerability/shields, not talent ICDs.
        ResetFixture();var h=Health;h.hp=Math.Max(2,h.maxHp/5);double count=Applied("cheatDeath");var before=Snapshot();
        terminalDamage=true;
        var incoming=NativeHit(h.maxHp*20f+23,"repeat-mortal-hit-within-source-cooldown");
        Require(B(incoming,"CheatDeathChecked")&&!B(incoming,"CheatDeathPrevented"),"second mortal hit inside original cooldown is not prevented");
        Equal(Applied("cheatDeath"),count,0,"second mortal native hit does not dispatch another source cheat-death proc");
        Require(h.hp<=0||h.IsDead(),"actual native second mortal hit ends isolated fixture");
        Save("repeat-mortal-hit.json",new{before,after=Snapshot(),incoming,note="Expected terminal damage in an isolated batch test with save suppression; no simulated talent dispatch."});
    }
    [HideFromIl2Cpp] JsonElement NativeHit(float amount,string id)
    {
        var h=Health;h.damageCooldown=0;
        var dc=new DamageContainer(0,"AzerothVitals:"+id){damage=amount,knockback=0,crit=false,flags=DcFlags.None,direction=Vector3.zero};
        var method=typeof(PlayerHealth).GetMethod("Damage",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(DamageContainer),typeof(bool)},null)
            ??throw new MissingMethodException("Native PlayerHealth.Damage(DamageContainer,bool)");
        string previous=(string)Get(Combat,"LastIncomingJson");method.Invoke(h,new object[]{dc,false});
        string current=(string)Get(Combat,"LastIncomingJson");
        Require(current!=previous&&current!="null","native damage passed real admission and updated source incoming diagnostics",id);
        var incoming=J(current);Require(D(incoming,"InitialDamage")>0,"admitted native incoming damage is positive",id);
        Save(id+"-native-call.json",new{inputDamage=amount,finalDamageContainer=dc.damage,flags=dc.flags.ToString(),ignoreNativeShield=false,incoming});return incoming;
    }
    [HideFromIl2Cpp] float Shield()=>Convert.ToSingle(Get(Combat,"ShieldRemaining"));
    [HideFromIl2Cpp] bool SourceShieldHas(string talent)
    {foreach(var s in P(J(Get(Ability,"SnapshotJson")),"Shields").EnumerateArray())if(S(s,"id").Contains(talent,StringComparison.Ordinal))return true;return false;}
    [HideFromIl2Cpp] double Applied(string name)
    {var data=J(Get(TalentRuntime,"DiagnosticsJson"));return TryP(data,"Applied",out var group)&&TryP(group,name,out var value)?value.GetDouble():0;}
    [HideFromIl2Cpp] object Snapshot()
    {
        var h=Health;return new{classId,phase,hp=h?.hp??-1,maxHp=h?.maxHp??-1,nativeShield=h?.shield??0,nativeOverheal=h?.overheal??0,nativeCarry=h?.leftOverHeal??0,nativeDamageCooldown=h?.damageCooldown??0,
            healingMultiplier=PlayerStats.GetStat(EStat.HealingMultiplier),nativeEvasion=PlayerStats.GetStat(EStat.Evasion),sourceShield=Shield(),
            lastHeal=J(Get(Combat,"LastHealJson")),lastIncoming=J(Get(Combat,"LastIncomingJson")),sourceCombat=J(Get(Ability,"SnapshotJson")),talents=J(Get(TalentRuntime,"DiagnosticsJson"))};
    }
    [HideFromIl2Cpp] Type Named(string name)
    {if(types.TryGetValue(name,out var t))return t;foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies()){t=assembly.GetType(name,false);if(t!=null)return types[name]=t;}throw new MissingMemberException("Vitals diagnostics type missing: "+name);}
    [HideFromIl2Cpp] object Get(string type,string property)=>Named(type).GetProperty(property,BindingFlags.Public|BindingFlags.Static)?.GetValue(null)??throw new MissingMemberException(type,property);
    [HideFromIl2Cpp] object Call(string type,string name,params object[] args)
    {foreach(var method in Named(type).GetMethods(BindingFlags.Public|BindingFlags.Static))if(method.Name==name&&method.GetParameters().Length==args.Length)return method.Invoke(null,args);throw new MissingMethodException(type,name);}
    [HideFromIl2Cpp] static JsonElement J(object value){using var doc=JsonDocument.Parse(value as string??JsonSerializer.Serialize(value));return doc.RootElement.Clone();}
    [HideFromIl2Cpp] static bool TryP(JsonElement value,string name,out JsonElement result)
    {if(value.ValueKind==JsonValueKind.Object)foreach(var p in value.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)){result=p.Value;return true;}result=default;return false;}
    [HideFromIl2Cpp] static JsonElement P(JsonElement value,string name)=>TryP(value,name,out var p)?p:throw new MissingMemberException("Vitals JSON property absent: "+name);
    [HideFromIl2Cpp] static string S(JsonElement value,string name)=>TryP(value,name,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():"";
    [HideFromIl2Cpp] static double D(JsonElement value,string name)=>P(value,name).GetDouble();
    [HideFromIl2Cpp] static int I(JsonElement value,string name)=>P(value,name).GetInt32();
    [HideFromIl2Cpp] static bool B(JsonElement value,string name)=>P(value,name).GetBoolean();
    [HideFromIl2Cpp] static Exception Unwrap(Exception error)=>error is TargetInvocationException t&&t.InnerException!=null?t.InnerException:error;
    [HideFromIl2Cpp] void Equal(double actual,double expected,double tolerance,string label)=>Require(Math.Abs(actual-expected)<=tolerance,label,actual+" vs "+expected+" +/-"+tolerance);
    [HideFromIl2Cpp] void Require(bool value,string label,string detail="")
    {Record(value?"PASS":"FAIL",label,detail);if(!value)throw new InvalidOperationException(label+" "+detail);}
    [HideFromIl2Cpp] void Record(string status,string label,string detail="")
    {if(status=="PASS")passed++;else if(status=="FAIL")failed++;else skipped++;checks.Add(new{status,label,detail,seconds=Time.realtimeSinceStartup-began});Plugin.Note("VITALS "+classId+" "+status+" "+label+" "+detail);}
    [HideFromIl2Cpp] void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),value as string??JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    [HideFromIl2Cpp] void Finish(Exception error)
    {
        if(done)return;done=true;
        try
        {
            if(error!=null)Record("FAIL","handled probe exception",Unwrap(error).ToString());
            if(initialized)
            {
                Save("final-before-cleanup.json",Snapshot());
                if(Health!=null&&!terminalDamage&&!Health.IsDead()){Health.hp=initialHp;Health.shield=initialShield;Health.overheal=initialOverheal;Health.leftOverHeal=initialCarry;}
                var stats=MyPlayer.Instance?.inventory?.playerStats?.stats;
                if(originalStatsSaved&&stats!=null)
                {if(hadEvasion)stats[EStat.Evasion]=originalEvasion;else stats.Remove(EStat.Evasion);if(hadHealing)stats[EStat.HealingMultiplier]=originalHealing;else stats.Remove(EStat.HealingMultiplier);}
                Call(Tome,"SetAutomaticCastingForProbe",false);
            }
        }
        catch(Exception cleanup){failed++;try{Plugin.Note("VITALS handled cleanup failure: "+Unwrap(cleanup));}catch{}}
        try
        {
            if(folder!=null)Save("summary.json",new{pass=failed==0,classId,phase,wallSeconds=Time.realtimeSinceStartup-began,passed,failed,skipped,terminalDamage,checks,
                coverage="Real native HP/carry/heal/shield/incoming actions with source talent counters. Absent source effect19/20 scenarios are explicit skips. Terminal repeat, if present, is the final isolated native hit. Not all354 talent proc verification.",error=error?.ToString()});
            Plugin.Note("VITALS END "+classId+" pass="+passed+" fail="+failed+" skip="+skipped);
        }
        catch(Exception report){try{Plugin.Note("VITALS handled report failure: "+report);}catch{}failed++;}
        Application.Quit(failed==0?0:2);
    }
}
