using System.Reflection;
using System.Text.Json;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Managers;
using Assets.Scripts.Utility;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.ElwynnBiome;

// One real native run per class. All fixture mutations are gated by batchmode,
// environment and an explicit CLI flag; nothing writes progression or save files.
public sealed class HeroClassProbe:MonoBehaviour
{
    public HeroClassProbe(IntPtr pointer):base(pointer){}
    const string Hero="AzerothHeroes.HeroBridge",HeroProbe="AzerothHeroes.HeroProbeApi",Visual="AzerothHeroes.HeroVisualBridge";
    const string Ability="AzerothAbilities.ClassAbilityDiagnostics",Hud="AzerothAbilities.HudBridge",Tome="AzerothAbilities.TomeCompatibilityDiagnostics";
    const string Talent="MageTalents.TalentBridge",Tree="MageTalents.TalentTreeDiagnostics",Clock="AzerothAbilities.CombatSimulation";
    static readonly string[] Classes={"warrior","paladin","hunter","rogue","priest","shaman","mage","warlock","druid"};
    readonly Dictionary<string,Type> types=new();readonly List<object> results=new(),castResults=new();readonly List<JsonElement> spells=new(),talents=new();
    string classId,folder,relative,originalRanks,firstTalent;long originalXp;float began,entered,clockBefore,treeOldScale;
    bool armed,done,castAccepted,originalNativePause;int phase,spellIndex,pass,fail,skip,nativeLevelupsResolved;Enemy fixture;
    JsonElement spellBefore,pauseBefore,talentPauseBefore,animationPauseBefore;Dictionary<uint,float> hpBefore,initialHp;float fixtureHpBefore;int representativeHits;
    float hpLost;int sourceCastsBefore,sourceHitsBefore;
    int casterHpBeforeCast,casterHpAfterCast;float invulnerableAfterCast;
    public static bool Armed=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1"&&Environment.GetCommandLineArgs().Contains("-azeroth-hero-class-smoke");
    void Start()
    {
        if(!Armed){enabled=false;return;}armed=true;began=Time.realtimeSinceStartup;
        relative="hero-class-"+Environment.ProcessId;folder=Path.GetFullPath(Path.Combine(BepInEx.Paths.GameRootPath,"../reports",relative));Directory.CreateDirectory(folder);
        Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
        try
        {
            classId=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-azeroth-hero=",StringComparison.Ordinal))?.Substring("-azeroth-hero=".Length);
            Require(Classes.Contains(classId),"explicit valid class CLI",classId??"missing");
            Require(Environment.GetCommandLineArgs().Contains("-azeroth-test-battle"),"explicit isolated test battle");
            Require(Environment.GetCommandLineArgs().Contains("-azeroth-talents-max")&&Environment.GetCommandLineArgs().Contains("-azeroth-talent-points=999"),"explicit temporary max-talent fixture");
            Plugin.Note("HERO CLASS BEGIN "+classId+" "+folder+"; bounded120s; no progression saves");entered=began;
        }
        catch(Exception e){Finish(e);}
    }
    void Update()
    {
        if(!armed||done)return;
        try
        {
            if(Time.realtimeSinceStartup-began>120)throw new TimeoutException("Hero class deadline120s at phase="+phase+",spell="+spellIndex);
            var p=MyPlayer.Instance;
            bool observingHeal=phase==8&&spellIndex<spells.Count&&I(spells[spellIndex],"StatusKind")==6;
            if(p?.inventory?.playerHealth!=null&&!MyTime.paused&&!observingHeal)p.inventory.playerHealth.hp=p.inventory.playerHealth.maxHp;
            // The native hurt cooldown protects this healing-only observation
            // from unrelated enemy hits; the source/native Heal paths are unchanged.
            if(observingHeal&&p?.inventory?.playerHealth!=null)p.inventory.playerHealth.damageCooldown=10;
            // Incidental natural XP choices must not stall this bounded class
            // fixture. Close only its actual native level-up window, without
            // fabricating/selecting an upgrade. Record retained XP/books verbatim.
            if((phase<=1||phase>=5&&phase<=9)&&LevelupScreen.isLevelingUp)
            {
                var window=Object.FindObjectsOfType<LevelupScreen>().FirstOrDefault(w=>w.gameObject.activeInHierarchy);
                if(window!=null)
                {
                    int incident=++nativeLevelupsResolved;Save("incidental-xp-"+incident+"-before.json",NativeProgress());
                    window.CloseLevelupScreen();Save("incidental-xp-"+incident+"-after.json",NativeProgress());
                    Record("SKIP","incidental native XP window","Closed actual LevelupScreen without selecting an upgrade; native earned level/XP/books retained in incident evidence. This is fixture recovery, not an assertion pass.");
                }
                return;
            }
            switch(phase)
            {
                case 0:
                    if(!ElwynnBridge.Ready||p?.inventory==null||!p.hasStarted||MapGenerationController.isGenerating||GameManager.Instance?.IsPlaying()!=true||LevelupScreen.isLevelingUp||GameManager.Instance?.isGameOver==true)return;
                    Require((bool)Get(Hud,"Active")&&(string)Get(Hero,"ActiveClassId")==classId,"actual player class matches CLI");
                    Require((string)Get(Hud,"ClassId")==classId&&(string)Get(Ability,"ActiveClassId")==classId,"ability class follows actual hero");
                    Require((bool)Call(Tome,"SetAutomaticCastingForProbe",false),"disable automatic spell fixtures");
                    ValidateCatalogs();ValidateInventory();ValidateTalents();entered=Time.realtimeSinceStartup;phase=1;break;
                case 1:
                    if(!(bool)Get(Visual,"IsAttached")){if(Age>12)throw new TimeoutException("Source class body never attached");return;}
                    if(Age<1)return;ValidateVisual();
                    originalNativePause=MyTime.paused;treeOldScale=Time.timeScale;
                    Require((bool)Call(Talent,"SetPanelVisibleForProbe",true),"open real talent panel");entered=Time.realtimeSinceStartup;phase=2;break;
                case 2:
                    if(Age<.35f)return;
                    Require((bool)Get(Tree,"Visible")&&(int)Get(Tree,"ConcurrentTrees")==3,"three actual source trees open together");
                    Require((int)Get(Tree,"NodeCount")==talents.Count&&(int)Get(Tree,"CatalogNodeCount")==talents.Count,"all source talent nodes visible");
                    Require((int)Get(Tree,"MissingTextures")==0,"talent textures complete");
                    for(int i=0;i<3;i++)Require((bool)Call(Tree,"SelectTreeForProbe",i),"select source tree "+i);
                    Require((bool)Call(Tree,"HoverForProbe",firstTalent),"real source talent tooltip");
                    Require(MyTime.paused&&Time.timeScale==0,"source tree pauses native and Unity clocks");
                    Save("talent-tree-maxed.json",Call(Tree,"Describe"));Capture("talents-all-three-maxed.ppm");
                    clockBefore=(float)Get(Clock,"Time");pauseBefore=J(Get(Tome,"SnapshotJson"));RememberFrozenExtras();entered=Time.realtimeSinceStartup;phase=3;break;
                case 3:
                    if(Age<.65f)return;AssertFrozen(pauseBefore,J(Get(Tome,"SnapshotJson")),"tree");
                    Require(Mathf.Abs((float)Get(Clock,"Time")-clockBefore)<.0001f,"tree source clock frozen");
                    Call(Tree,"BatchProbe","reset");Require((int)Get(Talent,"SpentPoints")==0,"real reset cleared allocation");
                    Require((bool)Call(Tree,"ClickNodeForProbe",firstTalent,false),"real source talent Button purchase");
                    Require((int)Call(Talent,"Rank",firstTalent)==1&&(int)Get(Talent,"SpentPoints")==1,"real rank/point increment");entered=Time.realtimeSinceStartup;phase=4;break;
                case 4:
                    if(Age<.25f)return;Save("talent-purchased.json",Call(Tree,"Describe"));Capture("talent-real-purchase.ppm");
                    Require((bool)Call(Tree,"RightClickNodeForProbe",firstTalent),"real pointer right-click refund");
                    Require((int)Call(Talent,"Rank",firstTalent)==0&&(int)Get(Talent,"SpentPoints")==0,"real rank/point refund");
                    RestoreTalents();Require((bool)Call(Talent,"SetPanelVisibleForProbe",false),"close source tree");entered=Time.realtimeSinceStartup;phase=5;break;
                case 5:
                    if(Age<.3f)return;
                    Require(MyTime.paused==originalNativePause&&Mathf.Abs(Time.timeScale-treeOldScale)<.0001f,"source tree restores previous pause state");
                    Require((bool)Call(Ability,"LearnAllForProbe"),"learn real active source catalog");
                    var learned=(string[])Get(Hud,"SpellIds");Require(spells.All(s=>learned.Contains(S(s,"Id"))),"all current-class source spells learned");
                    Save("source-learned.json",Get(Tome,"SnapshotJson"));
                    fixture=EnemyManager.Instance.SpawnBoss((EEnemy)0,-1,EEnemyFlag.Boss,Floor(p.transform.position+Forward(p)*2.5f),1);
                    Require(fixture!=null,"actual native Skeleton fixture spawned");fixture.maxHp=10000000;fixture.hp=fixture.maxHp;
                    entered=Time.realtimeSinceStartup;phase=6;break;
                case 6:
                    if(fixture==null)throw new InvalidOperationException("Native Skeleton fixture disappeared");
                    if(fixture.IsTeleporting()){if(Age>12)throw new TimeoutException("Skeleton native spawn never finished");return;}
                    initialHp=Hp();sourceCastsBefore=(int)Get(Hud,"CastCount");sourceHitsBefore=(int)Get(Hud,"HitCount");
                    Save("native-fixture.json",new{id=fixture.id,fixture.hp,fixture.maxHp,nativeBoss=fixture.IsBoss(),fixtureName=fixture.name,position=V(fixture.transform.position),note="Native SpawnBoss(EEnemy0) with explicit10MHP durability; native AI/colliders/damage retained."});
                    entered=Time.realtimeSinceStartup;phase=7;break;
                case 7:
                    if(spellIndex>=spells.Count){entered=Time.realtimeSinceStartup;phase=9;break;}
                    if(!(bool)Get(Clock,"CanSimulate")){Save("cast-clock-blocked-"+spellIndex+".json",Bundle());if(Age>10)throw new TimeoutException("Native source cast admission remained blocked: "+Get(Clock,"PauseReason"));return;}
                    PrepareCast();entered=Time.realtimeSinceStartup;phase=8;break;
                case 8:
                    if(Age<1.8f)return;CompleteCast();spellIndex++;entered=Time.realtimeSinceStartup;phase=7;break;
                case 9:
                    if(Age<2)return;
                    hpLost=Lost(initialHp);Require(representativeHits>0,"representative catalog spells produced actual native hits",representativeHits.ToString());
                    Require(hpLost>0,"real native enemy HP decreased",hpLost.ToString("F3",System.Globalization.CultureInfo.InvariantCulture));
                    Save("combat-after.json",Bundle());Capture("combat-source-class.ppm");
                    if(!(bool)Get(Clock,"CanSimulate")){Save("pause-entry-clock-blocked.json",Bundle());if(Age>12)throw new TimeoutException("Native pause fixture admission remained blocked: "+Get(Clock,"PauseReason"));return;}
                    Require(UiManager.Instance?.pause!=null,"native pause UI exists");UiManager.Instance.pause.Pause();entered=Time.realtimeSinceStartup;phase=10;break;
                case 10:
                    if(UiManager.Instance.pause.IsPaused()!=true){if(Age>4)throw new TimeoutException("Native pause UI not admitted");return;}
                    if(Age<.3f)return;
                    pauseBefore=J(Get(Tome,"SnapshotJson"));RememberFrozenExtras();Save("native-pause-before.json",Bundle());entered=Time.realtimeSinceStartup;phase=11;break;
                case 11:
                    if(Age<.7f)return;AssertFrozen(pauseBefore,J(Get(Tome,"SnapshotJson")),"native pause");Save("native-pause-after.json",Bundle());Capture("native-pause-inventory.ppm");
                    Require((bool)Call(Talent,"SetPanelVisibleForProbe",true),"open talent tree inside existing native pause");entered=Time.realtimeSinceStartup;phase=12;break;
                case 12:
                    if(Age<.25f)return;Call(Talent,"SetPanelVisibleForProbe",false);
                    Require(MyTime.paused&&UiManager.Instance.pause.IsPaused(),"closing talent tree preserves native pause owner");
                    UiManager.Instance.pause.Resume();entered=Time.realtimeSinceStartup;phase=13;break;
                case 13:
                    if(Age<.4f)return;
                    Save("native-resume-observation.json",new{age=Age,nativePaused=MyTime.paused,pauseUi=UiManager.Instance.pause.IsPaused(),levelup=LevelupScreen.isLevelingUp,gameOver=GameManager.Instance?.isGameOver,unityScale=Time.timeScale});
                    if((MyTime.paused||UiManager.Instance.pause.IsPaused())&&Age<4)return;
                    Require(!MyTime.paused&&!UiManager.Instance.pause.IsPaused(),"native resume restores gameplay");
                    ValidateTalents(false);Save("final.json",Bundle());Finish(null);break;
            }
        }
        catch(Exception e){Finish(Unwrap(e));}
    }
    float Age=>Time.realtimeSinceStartup-entered;
    [HideFromIl2Cpp] void ValidateInventory()
    {
        var evidence=J(Get(HeroProbe,"SnapshotJson"));Save("native-inventory.json",evidence);
        Require(B(evidence,"InventoryPrivateClone"),"private runtime character clone");
        Require(S(evidence,"NativePassive")=="None"&&S(evidence,"NativePassiveType").EndsWith("PassiveAbilityMissing",StringComparison.Ordinal),"native passive is neutral missing implementation");
        Require(B(evidence,"NativeActiveAbsent"),"native active ability absent");
        Require(I(evidence,"NativeClassStatModifiers")==0,"native character stat modifiers neutral");
        Require(InventoryUtility.GetNumAvailableTomeSlots()==4,"all four native book slots immediately unlocked");
        if((bool)Get(Ability,"UsesGenericRuntime"))
        {
            var catalog=J(Get(Ability,"ActiveSourceCatalogJson"));float sourceBase=(float)D(P(catalog,"SourceClass"),"SourceBaseHealth");
            float hpBonus=(float)Call("AzerothAbilities.ClassCombat","StatBonus",2)+(float)Call("MageTalents.TalentRuntime","SourceStat",2);
            int nativeLevel=MyPlayer.Instance.inventory.playerXp.level;float expected=(sourceBase+9*Math.Max(0,nativeLevel))*(1+hpBonus);
            float actual=(float)D(P(J(Get(Tome,"SnapshotJson")),"NativeStats"),"MaxHealth");
            Save("source-health-initialization.json",new{sourceBase,nativeLevel,hpBonus,expected,nativeComputedMaximum=actual,actualHealthMaximum=MyPlayer.Instance.inventory.playerHealth.maxHp});
            Require(Mathf.Abs(actual-expected)<.02f,"native maximum health uses source class/race/talent baseline",actual+" vs "+expected);
            Require(Mathf.Abs(MyPlayer.Instance.inventory.playerHealth.maxHp-expected)<1,"actual cached native health maximum includes startup talents",MyPlayer.Instance.inventory.playerHealth.maxHp+" vs "+expected);
        }

        var p=MyPlayer.Instance;var capsules=p.GetComponentsInChildren<CapsuleCollider>(true).Where(c=>c.enabled&&!c.isTrigger).ToArray();
        Require(p.playerMovement!=null&&capsules.Length>0,"real native movement and solid capsule retained");
        Save("native-capsules.json",capsules.Select(c=>new{id=c.GetInstanceID(),name=c.name,c.radius,c.height,center=V(c.center),size=V(c.bounds.size)}).ToArray());
    }
    [HideFromIl2Cpp] void ValidateCatalogs()
    {
        var coverage=J(Get(Ability,"DispatchCoverageJson"));Save("abilities-global-schema.json",coverage);
        Require(I(coverage,"SourceClasses")==9&&I(coverage,"SourceClassSpells")==102,"nine source classes/102 class-spell catalog records");
        var catalog=J(Get(Ability,"ActiveSourceCatalogJson"));Save("active-ability-catalog.json",catalog);
        Require(S(catalog,"ClassId")==classId,"actual active source ability catalog");spells.AddRange(P(catalog,"Spells").EnumerateArray().Select(s=>s.Clone()));
        Require(spells.Count>0&&spells.Select(s=>S(s,"Id")).Distinct().Count()==spells.Count,"current source spell IDs unique");
        string starter=S(catalog,"StartingSpell");Require(starter==(string)Get(Hero,"StartingSpellId")&&spells.Any(s=>S(s,"Id")==starter),"real source starter matches hero catalog");
        string effectiveStarter=(string)Call("MageTalents.TalentRuntime","Starter",starter);
        Require(spells.Any(s=>S(s,"Id")==effectiveStarter)&&((string[])Get(Hud,"SpellIds")).Contains(effectiveStarter),"actual source starter after real selected talent override",starter+" -> "+effectiveStarter);
        bool expectedGeneric=classId!="mage"||Environment.GetCommandLineArgs().Contains("-azeroth-generic-mage-probe");
        Require((bool)Get(Ability,"UsesGenericRuntime")==expectedGeneric,"explicit legacy/generic class branch policy",expectedGeneric.ToString());
        var global=J(Get(Talent,"CatalogDiagnosticsJson"));Save("talents-global-schema.json",global);var classes=P(global,"Classes").EnumerateArray().ToArray();
        Require(classes.Length==9&&classes.Sum(c=>I(c,"Nodes"))==354,"nine source classes/354 talent catalog records");
        Require((string)Get(Talent,"ClassId")==classId,"talent profile follows active class");
        var talentCatalog=J(Get(Talent,"CatalogJson"));Save("active-talent-catalog.json",talentCatalog);talents.AddRange(P(talentCatalog,"Talents").EnumerateArray().Select(n=>n.Clone()));
        Require(talents.Count==(classId is "warrior" or "paladin" or "druid"?40:39),"active class original39/40talents");
        Require(P(talentCatalog,"Trees").GetArrayLength()==3,"source class three tree descriptors");
        firstTalent=S(talents.First(t=>I(t,"Tier")==0&&string.IsNullOrEmpty(S(t,"Prerequisite"))),"Id");
        var state=J(Get(Talent,"StateJson"));originalXp=L(state,"TalentXp");originalRanks=P(state,"Ranks").GetRawText();
    }
    [HideFromIl2Cpp] void ValidateTalents(bool initial=true)
    {
        Require((int)Get(Talent,"TemporaryPointBudget")==999,"temporary999point budget active");
        Require(talents.All(t=>(int)Call(Talent,"Rank",S(t,"Id"))==I(t,"MaxRank")),"all actual current-class ranks maxed");
        Require((int)Get(Talent,"SpentPoints")==talents.Sum(t=>I(t,"MaxRank")),"actual spent-point total matches source maximum ranks");
        if(initial)Record("SKIP","individual natural talent procs","Catalog/allocation coverage is not proof of354individual natural procs; conditional and chance-based effects require dedicated scenarios.");
    }
    [HideFromIl2Cpp] void ValidateVisual()
    {
        var visual=J(Call(Visual,"AppearanceDiagnostics"));Save("source-appearance.json",visual);
        Require(S(visual,"activeClassId")==classId&&B(visual,"attached"),"actual source class body attached");
        bool animalForm=classId=="druid"&&I(visual,"activeFormSpellId") is 768 or 9634;
        Require((animalForm||B(visual,"sourceHeadClosed"))&&I(visual,"sourceVertexCount")>0,"original source body geometry and applicable humanoid head coverage retained");
        Require(B(visual,"nativeMovementRetained")&&B(visual,"castPosesDisabled"),"native movement policy and source movement-only clips");
        var equipment=P(visual,"equipment");Require(S(equipment,"classId")==classId&&B(equipment,"sourceAssetsValid"),"actual source class equipment assets valid");
        Require(I(equipment,"visibleNativeWeaponRenderers")==0,"native placeholder weapon invisible");
        var parts=P(equipment,"equipment");
        if(animalForm)Require(!B(equipment,"active"),"source humanoid equipment hidden during actual animal form");
        else if(parts.GetArrayLength()>0)Require(B(equipment,"attached")&&B(equipment,"active"),"source class gear actually attached and visible");
        else Record("SKIP","source equipment visibility","This class has no exported source equipment parts; evidence retained.");
        var motion=J(Call(Visual,"RunMotionRegressionForProbe"));Save("source-motion-regression.json",motion);Require(B(motion,"pass"),"actual source movement regression");
        Save("source-animation.json",Call(Visual,"AnimationDiagnostics"));
    }
    [HideFromIl2Cpp] void PrepareCast()
    {
        var spell=spells[spellIndex];string id=S(spell,"Id");
        Require((bool)Get(Clock,"CanSimulate"),"source simulation available before "+id);
        if(fixture==null||fixture.IsDeadOrDyingNextFrame())throw new InvalidOperationException("Durable native fixture died before "+id);
        // Keep this native fixture in real close-range/cone reach; no target or hit is fabricated.
        var player=MyPlayer.Instance;fixture.transform.position=player.transform.position+Forward(player)*2.5f;
        Save("spell-"+id+"-spatial-fixture.json",new{player=V(player.transform.position),target=V(fixture.transform.position),distance=Vector3.Distance(player.transform.position,fixture.transform.position),note="Real native target positioned in explicit2.5m cast reach, avoiding a cliff floor sample outside spell range; no hit is fabricated."});
        int status=I(spell,"StatusKind");
        if(status==7&&(bool)Get(Ability,"UsesGenericRuntime"))
        {
            var combatType=Named("AzerothAbilities.ClassCombat");
            var shields=combatType.GetField("shields",BindingFlags.NonPublic|BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
            Require(shields!=null,"source shield fixture state available");
            Save("spell-"+id+"-prior-shield.json",Get(Ability,"SnapshotJson"));shields.Clear();
            Record("SKIP",id+" pre-existing shield fixture","Cleared only existing source shield state to exercise a fresh real cast; native normal recast guard remains unchanged.");
        }
        if(status==6){player.inventory.playerHealth.hp=Math.Max(1,(int)(player.inventory.playerHealth.maxHp*.60f));player.inventory.playerHealth.damageCooldown=10;}
        else if(status==11)player.inventory.playerHealth.hp=Math.Max(1,(int)(player.inventory.playerHealth.maxHp*.25f));
        casterHpBeforeCast=player.inventory.playerHealth.hp;
        spellBefore=J(Get(Ability,"TelemetryJson"));hpBefore=Hp();fixtureHpBefore=fixture.hp;
        castAccepted=(bool)Call(Tome,"CastSpellForProbe",id);
        casterHpAfterCast=player.inventory.playerHealth.hp;
        invulnerableAfterCast=!(bool)Get(Ability,"UsesGenericRuntime")?0:(float)D(J(Get(Ability,"SnapshotJson")),"InvulnerableFor");
        Save("spell-"+id+"-before.json",Bundle());
    }
    [HideFromIl2Cpp] void CompleteCast()
    {
        var spell=spells[spellIndex];string id=S(spell,"Id"),kind=S(spell,"Kind");var after=J(Get(Ability,"TelemetryJson"));
        int casts=(int)(Counter(after,"CastsBySpell",id)-Counter(spellBefore,"CastsBySpell",id));
        int hits=(int)(Counter(after,"HitsBySpell",id)-Counter(spellBefore,"HitsBySpell",id));double damage=Counter(after,"DamageBySpell",id)-Counter(spellBefore,"DamageBySpell",id);
        float lost=Lost(hpBefore),fixtureLost=fixture==null?0:Mathf.Max(0,fixtureHpBefore-fixture.hp);representativeHits+=Math.Max(0,hits);
        castResults.Add(new{id,kind,casterHpBeforeCast,casterHpAfterCast,invulnerableAfterCast,accepted=castAccepted,castsDelta=casts,hitsDelta=hits,sourceDamageDelta=damage,nativeHpLossAcrossObservedEnemies=lost,nativeFixtureHpLoss=fixtureLost,note="HP deltas are observed during this window, not exclusive per-spell attribution; other real pets/DoTs/talent procs may also hit."});
        Save("spell-"+id+"-after.json",Bundle());
        if(kind=="passive"){Record("SKIP",id+" natural passive proc","Passive is learned and catalogued; explicit free cast is not a natural conditional trigger. accepted="+castAccepted+",hits="+hits);return;}
        Check(castAccepted&&casts>0,id+" actual cast dispatch","accepted="+castAccepted+",castsDelta="+casts);
        if(I(spell,"StatusKind")==6)
        {
            if(kind=="selfBuff"&&D(spell,"Duration")>0&&(bool)Get(Ability,"UsesGenericRuntime"))
            {
                var heal=J(Get("AzerothAbilities.ClassCombat","LastHealJson"));
                int afterWindow=MyPlayer.Instance.inventory.playerHealth.hp;
                Save("spell-"+id+"-healing.json",new{casterHpBeforeCast,casterHpAfterCast,afterWindow,lastSourceHeal=heal,note="Native hurt cooldown isolates the healing window from unrelated incoming attacks; HP gain may also include normal regeneration."});
                MyPlayer.Instance.inventory.playerHealth.damageCooldown=0;
                Check(afterWindow>casterHpBeforeCast&&S(heal,"Origin")=="spell:"+id&&D(heal,"AcceptedFractionalHealing")>0,id+" periodic native HP actually restored",casterHpBeforeCast+" -> "+afterWindow);
            }
            else Check(casterHpAfterCast>casterHpBeforeCast,id+" native HP actually restored",casterHpBeforeCast+" -> "+casterHpAfterCast);
        }
        if(I(spell,"StatusKind")==11&&(bool)Get(Ability,"UsesGenericRuntime"))Check(invulnerableAfterCast>0,id+" source invulnerability actually active",invulnerableAfterCast.ToString());
        if(hits>0)Record("PASS",id+" observed native hits","hits="+hits+",sourceDamage="+damage+",nativeHpWindow="+lost);
        else Record("SKIP",id+" immediate damage observation",kind+": no per-spell hit observed in1.8s; heals/buffs/control/passives and delayed/channel ticks are not required to deal immediate damage.");
    }
    [HideFromIl2Cpp] void RememberFrozenExtras()
    {talentPauseBefore=J(OptionalJson("MageTalents.TalentRuntime","DiagnosticsJson"));animationPauseBefore=J(Call(Visual,"AnimationDiagnostics"));}
    [HideFromIl2Cpp] void AssertFrozen(JsonElement before,JsonElement after,string owner)
    {
        Require(Math.Abs(D(before,"CombatTime")-D(after,"CombatTime"))<.00001,owner+" freezes source clock");
        Require(I(before,"Casts")==I(after,"Casts")&&I(before,"Hits")==I(after,"Hits"),owner+" freezes casts and hits");
        foreach(string name in new[]{"Projectiles","Spells","Behaviors","SourceCombat"})
        {
            if(!TryP(before,name,out var a)||!TryP(after,name,out var b)){Record("SKIP",owner+" "+name,"Snapshot field unavailable; not asserted.");continue;}
            Require(a.GetRawText()==b.GetRawText(),owner+" freezes "+name+" including cooldowns/pets/status");
        }
        var talentAfter=J(OptionalJson("MageTalents.TalentRuntime","DiagnosticsJson"));
        if(!TryP(talentPauseBefore,"unavailable",out _))Require(talentPauseBefore.GetRawText()==talentAfter.GetRawText(),owner+" freezes talent runtime counters/context");
        else Record("SKIP",owner+" talent context","Public talent runtime diagnostics unavailable.");
        var animationAfter=J(Call(Visual,"AnimationDiagnostics"));
        if(TryP(animationPauseBefore,"clock",out var ca)&&TryP(animationAfter,"clock",out var cb)&&TryP(ca,"clock",out var ac)&&TryP(cb,"clock",out var bc))Require(Math.Abs(ac.GetDouble()-bc.GetDouble())<.00001,owner+" freezes actual source animation clock");
        else Record("SKIP",owner+" animation clock","Public clock schema unavailable; raw animation evidence retained.");
        var vfxBefore=P(before,"Vfx");var vfxAfter=P(after,"Vfx");
        if(TryP(vfxBefore,"EffectAges",out var agesA)&&TryP(vfxAfter,"EffectAges",out var agesB))Require(agesA.GetRawText()==agesB.GetRawText(),owner+" freezes source VFX ages");
        else Record("SKIP",owner+" VFX ages","No public EffectAges snapshot.");
        if(TryP(after,"SourceCombat",out var source)&&source.ValueKind==JsonValueKind.Object&&TryP(source,"Pets",out var pets)&&pets.ValueKind==JsonValueKind.Array&&pets.GetArrayLength()==0)
            Record("SKIP",owner+" active pet freeze","No source pets were active; empty stable list does not prove pet movement freeze.");
    }
    [HideFromIl2Cpp] void RestoreTalents()
    {
        if(originalRanks==null)return;Require((bool)Call(Talent,"SetProbeState",originalXp,originalRanks),"restore original in-memory maxed allocation");Call(Talent,"ApplyDebugNow");
    }
    [HideFromIl2Cpp] object Bundle()=>new{classId,phase,spellIndex,nativeProgress=NativeProgress(),hero=J(Get(HeroProbe,"SnapshotJson")),source=J(Get(Tome,"SnapshotJson")),telemetry=J(Get(Ability,"TelemetryJson")),talents=J(Get(Talent,"StateJson")),talentRuntime=OptionalJson("MageTalents.TalentRuntime","DiagnosticsJson"),appearance=J(Call(Visual,"AppearanceDiagnostics")),animation=J(Call(Visual,"AnimationDiagnostics")),nativePause=UiManager.Instance?.pause?.IsPaused(),nativeHp=Hp()};
    [HideFromIl2Cpp] static object NativeProgress()
    {
        var inv=MyPlayer.Instance?.inventory;var books=new List<object>();var all=DataManager.Instance?.GetAllTomes();
        if(inv?.tomeInventory!=null&&all!=null)foreach(var t in all)if(t!=null&&inv.tomeInventory.HasTome(t.eTome))books.Add(new{id=t.eTome.ToString(),rank=inv.tomeInventory.GetTomeLevel(t.eTome)});
        return new{level=inv?.playerXp?.level??-1,xp=inv?.playerXp?.GetXpInt()??-1,books,nativeLevelupOpen=LevelupScreen.isLevelingUp};
    }
    [HideFromIl2Cpp] object OptionalJson(string type,string property){try{return J(Get(type,property));}catch(Exception e){return new{unavailable=Unwrap(e).Message};}}
    [HideFromIl2Cpp] Type Named(string name)
    {if(types.TryGetValue(name,out var t))return t;t=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(a=>a!=null);if(t==null)throw new MissingMemberException("Required public diagnostics type absent: "+name);return types[name]=t;}
    [HideFromIl2Cpp] object Get(string type,string name)=>Named(type).GetProperty(name,BindingFlags.Static|BindingFlags.Public)?.GetValue(null)??throw new MissingMemberException(type,name);
    [HideFromIl2Cpp] object Call(string type,string name,params object[] args)
    {var method=Named(type).GetMethods(BindingFlags.Public|BindingFlags.Static).FirstOrDefault(m=>m.Name==name&&m.GetParameters().Length==args.Length)??throw new MissingMethodException(type,name);return method.Invoke(null,args);}
    [HideFromIl2Cpp] static JsonElement J(object value){using var doc=JsonDocument.Parse(value as string??JsonSerializer.Serialize(value));return doc.RootElement.Clone();}
    [HideFromIl2Cpp] static bool TryP(JsonElement value,string name,out JsonElement property)
    {if(value.ValueKind==JsonValueKind.Object)foreach(var p in value.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)){property=p.Value;return true;}property=default;return false;}
    [HideFromIl2Cpp] static JsonElement P(JsonElement value,string name)=>TryP(value,name,out var p)?p:throw new MissingMemberException("JSON property missing: "+name);
    [HideFromIl2Cpp] static string S(JsonElement value,string name)=>TryP(value,name,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():"";
    [HideFromIl2Cpp] static bool B(JsonElement value,string name)=>P(value,name).GetBoolean();
    [HideFromIl2Cpp] static int I(JsonElement value,string name)=>P(value,name).GetInt32();
    [HideFromIl2Cpp] static long L(JsonElement value,string name)=>P(value,name).GetInt64();
    [HideFromIl2Cpp] static double D(JsonElement value,string name)=>P(value,name).GetDouble();
    [HideFromIl2Cpp] static double Counter(JsonElement value,string group,string id)=>TryP(value,group,out var g)&&TryP(g,id,out var v)&&v.TryGetDouble(out var n)?n:0;
    [HideFromIl2Cpp] static Exception Unwrap(Exception e)=>e is TargetInvocationException t&&t.InnerException!=null?t.InnerException:e;
    [HideFromIl2Cpp] static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
    [HideFromIl2Cpp] static Vector3 Forward(MyPlayer p){var f=p.transform.forward;f.y=0;if(f.sqrMagnitude<.001f)f=Vector3.forward;return f.normalized;}
    [HideFromIl2Cpp] static Vector3 Floor(Vector3 at)
    {if(Physics.Raycast(at+Vector3.up*100,Vector3.down,out var hit,240,GameManager.Instance.whatIsGround.value,QueryTriggerInteraction.Ignore))return hit.point+Vector3.up*.1f;throw new InvalidOperationException("No native floor under fixture "+at);}
    [HideFromIl2Cpp] static Dictionary<uint,float> Hp()
    {var hp=new Dictionary<uint,float>();if(EnemyManager.Instance?.enemies!=null)foreach(var pair in EnemyManager.Instance.enemies)if(pair.Value!=null)hp[pair.Key]=pair.Value.hp;return hp;}
    [HideFromIl2Cpp] static float Lost(Dictionary<uint,float> prior)
    {var now=Hp();return prior==null?0:prior.Sum(p=>now.TryGetValue(p.Key,out var current)?Mathf.Max(0,p.Value-current):0);}
    [HideFromIl2Cpp] void Check(bool value,string label,string detail="")=>Record(value?"PASS":"FAIL",label,detail);
    [HideFromIl2Cpp] void Require(bool value,string label,string detail="")
    {Record(value?"PASS":"FAIL",label,detail);if(!value)throw new InvalidOperationException(label+" "+detail);}
    [HideFromIl2Cpp] void Record(string status,string label,string detail="")
    {if(status=="PASS")pass++;else if(status=="FAIL")fail++;else skip++;results.Add(new{status,label,detail,seconds=Time.realtimeSinceStartup-began});Plugin.Note("HERO CLASS "+classId+" "+status+" "+label+" "+detail);}
    [HideFromIl2Cpp] void Save(string name,object value)=>File.WriteAllText(Path.Combine(folder,name),value as string??JsonSerializer.Serialize(value));
    [HideFromIl2Cpp] void Capture(string name)=>BiomeCapture.Capture(relative+"/"+name);
    [HideFromIl2Cpp] void Finish(Exception error)
    {
        if(done)return;done=true;if(error!=null)Record("FAIL","probe exception",Unwrap(error).ToString());
        try{if(originalRanks!=null){Call(Talent,"SetProbeState",originalXp,originalRanks);Call(Talent,"ApplyDebugNow");}Call(Talent,"SetPanelVisibleForProbe",false);if(UiManager.Instance?.pause?.IsPaused()==true)UiManager.Instance.pause.Resume();Call(Tome,"SetAutomaticCastingForProbe",false);}catch(Exception cleanup){Record("FAIL","probe cleanup",Unwrap(cleanup).ToString());}
        try{Save("summary.json",new{pass=fail==0,classId,phase,wallSeconds=Time.realtimeSinceStartup-began,passed=pass,failed=fail,skipped=skip,checks=results,spells=castResults,nativeHpLoss=hpLost,representativeHits,sourceCastsBefore,sourceHitsBefore,nativeLevelupsResolved,error=error?.ToString(),coverage="Nine-class schema/allocation plus representative real native casts/hits/pause/render evidence. Not354individual natural talent proc proof. Per-window native HP changes are not exclusive per-spell attribution."});}catch(Exception write){Plugin.Note("HERO CLASS report failed: "+write);}
        Plugin.Note("HERO CLASS END "+classId+" pass="+pass+" fail="+fail+" skip="+skip);Application.Quit(fail==0?0:2);
    }
}
