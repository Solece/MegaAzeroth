using AzerothAbilities;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Menu.Shop;
using Assets.Scripts.Inventory__Items__Pickups;
using UnityEngine;

namespace MageTalents;

internal static class LegacyTalentRuntime
{
    public static int EchoCount {get;private set;}
    public static int FreeCastCount {get;private set;}
    public static int IgniteTickCount {get;private set;}
    public static int ControlCount {get;private set;}
    public static int ChainCount {get;private set;}
    public static int SplitCount {get;private set;}
    public static int ExplosionCount {get;private set;}
    static int freeDepth,rawDepth,fireCriticals;
    static float nextNova;
    static bool runStarted,runAwarded;
    static readonly Queue<float> Pyrocasts=new(),Explosions=new();
    static readonly HashSet<(IntPtr,uint)> Killed=new();
    static int kills,bosses;
    static readonly List<Burn> Burns=new();
    sealed class Burn {internal Enemy Target;internal uint Id;internal float Damage,Next;internal int Ticks;}
    static TalentState S=>Plugin.Run;
    static TalentCatalog C=>Plugin.Catalog;
    static bool Selected(string id)=>S.Rank(id)>0;
    static bool Alive(Enemy e)=>e!=null && !e.IsDeadOrDyingNextFrame();
    public static void Clear()
    {
        freeDepth=rawDepth=fireCriticals=kills=bosses=0;nextNova=0;runStarted=runAwarded=false;
        Pyrocasts.Clear();Explosions.Clear();Killed.Clear();Burns.Clear();
        EchoCount=FreeCastCount=IgniteTickCount=ControlCount=ChainCount=SplitCount=ExplosionCount=0;
        WaterElemental.Clear();
    }
    public static string Starter(string current)
    {
        if(Selected("arcane_power"))return "arcane_missiles";
        if(Selected("water_elemental"))return "frostbolt";
        return current;
    }
    public static void AddStartingSpells()
    {
        // Source tag27 adds Pyroblast; tag28 replaces the base starter for Arcane/Frost.
        if(!AbilityRuntime.Active || !Selected("pyroclasm") || AbilityRuntime.Spells.Any(s=>s.Definition.Id=="pyroblast"))return;
        var catalog=(SpellCatalog)typeof(AbilityRuntime).GetField("Catalog",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)?.GetValue(null);
        var definition=catalog?.Spells.FirstOrDefault(s=>s.Id=="pyroblast");if(definition!=null)AbilityRuntime.Spells.Add(new LearnedSpell(definition));
    }
    public static float Radius(LearnedSpell spell)=>1+S.SpellModifier(C,spell.Definition.Id,spell.Definition.School,3);
    public static float Barrier(LearnedSpell spell)=>1+S.SpellModifier(C,spell.Definition.Id,spell.Definition.School,0);
    public static void BeforeHit(LearnedSpell spell,Enemy enemy,DamageContainer dc)
    {
        if(!AbilityRuntime.Active || dc==null || !Alive(enemy))return;
        if(MageSpellBehaviors.IsControlled(enemy))dc.damage*=1+S.Stat(C,18);
        // The ability adapter rolls native criticals once. Talent deltas enter
        // PlayerStats through StatPatch; rolling again here would apply books twice.
    }
    static bool Rate(Queue<float> q,int cap)
    {while(q.Count>0 && CombatSimulation.Time-q.Peek()>=1)q.Dequeue();if(q.Count>=cap)return false;q.Enqueue(CombatSimulation.Time);return true;}
    static bool Free(string id)
    {
        if(freeDepth>0)return false;
        freeDepth++;
        try{var cast=AbilityRuntime.CastFreeSpell(id);if(cast)FreeCastCount++;return cast;}
        finally{freeDepth--;}
    }
    public static void AfterCast(LearnedSpell spell)
    {
        if(freeDepth>0 || !AbilityRuntime.Active)return;
        if(Selected("arcane_concentration") && UnityEngine.Random.value<.3f)Free("arcane_explosion");
        var chance=Mathf.Clamp01(S.Stat(C,13));
        if(UnityEngine.Random.value>=chance)return;
        if(Free(spell.Definition.Id))EchoCount++;
        if(Selected("arcane_power"))
            for(var i=0;i<3;i++){if(UnityEngine.Random.value>=chance)break;if(Free(spell.Definition.Id))EchoCount++;}
    }
    public static void AfterHit(LearnedSpell spell,Enemy target,DamageContainer dc,float hpBefore)
    {
        if(rawDepth>0 || !AbilityRuntime.Active || target==null || dc==null)return;
        if(spell.Definition.Id=="water_elemental")return;
        var school=spell.Definition.School;
        if(school=="Fire")
        {
            if(Alive(target) && UnityEngine.Random.value<.02f*S.Rank("impact")){MageSpellBehaviors.ApplyRoot(target,1,true);ControlCount++;}
            if(dc.crit)
            {
                if(Selected("ignite") && Alive(target))Burns.Add(new Burn{Target=target,Id=target.id,Damage=dc.damage*.8f/2,Next=CombatSimulation.Time+2,Ticks=2});
                if(Selected("hot_streak"))
                {fireCriticals++;if(fireCriticals>=4){fireCriticals-=4;if(Rate(Pyrocasts,2))Free("pyroblast");}}
            }
        }
        if(school=="Frost" && Alive(target) && Selected("frostbite") && UnityEngine.Random.value<.4f){MageSpellBehaviors.ApplyRoot(target,1.5f,false);ControlCount++;}
        if(spell.Definition.Id=="arcane_missiles" && Selected("presence_of_mind"))
        {
            var next=AbilityRuntime.Targets(target.transform.position,spell.Definition.Range,false).FirstOrDefault(e=>e.Pointer!=target.Pointer);
            if(next!=null){Raw(next,dc.damage,"Arcane",target.transform.position,"presence_of_mind");ChainCount++;}
        }
        if(spell.Definition.Id=="frostbolt" && Selected("splitting_ice"))
        {
            var at=target.transform.position;
            foreach(var other in AbilityRuntime.Targets(at,5,false).Where(e=>e.Pointer!=target.Pointer)){Raw(other,dc.damage,"Frost",at,"splitting_ice");SplitCount++;}
            Visual("frostbolt","impact",at,5,.5f);
        }
        // Kill explosions are handled in the native EnemyDied hook, including periodic hits.
    }
    static void Raw(Enemy enemy,float damage,string school,Vector3 from,string id)
    {
        if(!CombatSimulation.CanSimulate || !Alive(enemy) || damage<=0)return;
        var dc=new DamageContainer(0,"AzerothTalent:"+id){damage=damage,crit=false,knockback=0,enemy=enemy,direction=(enemy.transform.position-from).normalized,element=school=="Fire"?EElement.Fire:EElement.Neutral};
        rawDepth++;
        try{enemy.DamageFromPlayerOther(dc);}
        finally{rawDepth--;}
    }
    internal static void OnDeath(Enemy enemy,DamageContainer dc)
    {
        if(!AbilityRuntime.Active || enemy==null || !Killed.Add((enemy.Pointer,enemy.id)))return;
        kills++;if(enemy.IsBoss()||enemy.IsStageBoss()||enemy.IsFinalBoss())bosses++;
        if(!Selected("pyroclasm") || dc==null || dc.element!=EElement.Fire || !Rate(Explosions,3))return;
        var at=enemy.transform.position;var damage=enemy.maxHp*.25f;
        ExplosionCount++;
        foreach(var other in AbilityRuntime.Targets(at,6,false).Where(e=>e.Pointer!=enemy.Pointer))Raw(other,damage,"Fire",at,"pyroclasm");
        Visual("blast_wave","impact",at,6,.65f);
    }
    static void Visual(string id,string phase,Vector3 at,float radius,float duration)=>MageSpellBehaviors.VisualEvent?.Invoke(id,phase,at,radius,duration);
    public static void Tick()
    {
        if(!AbilityRuntime.Active || C==null)return;
        var p=MyPlayer.Instance;var gm=GameManager.Instance;
        if(p==null || !p.hasStarted || gm==null)return;
        if(!runStarted){runStarted=true;nextNova=CombatSimulation.Time+8;Plugin.Current.Log.LogInfo($"Mage talent run snapshot:{S.Spent}points; stats damage={S.Stat(C,0):F3}; starter={Starter("fireball")}");}
        if(!CombatSimulation.CanSimulate)return;
        for(var i=Burns.Count-1;i>=0;i--)
        {
            var b=Burns[i];if(!Alive(b.Target)||b.Target.id!=b.Id){Burns.RemoveAt(i);continue;}
            if(CombatSimulation.Time<b.Next)continue;Raw(b.Target,b.Damage,"Fire",p.transform.position,"ignite");IgniteTickCount++;b.Next+=2;b.Ticks--;
            if(b.Ticks<=0)Burns.RemoveAt(i);
        }
        if(Selected("water_elemental") && CombatSimulation.Time>=nextNova){nextNova+=8;Free("frost_nova");}
        if(Selected("water_elemental"))WaterElemental.Tick(p);
    }
    internal static void EndRun(bool won)
    {
        if(!AbilityRuntime.Active || !runStarted || runAwarded || GameManager.Instance==null)return;
        runAwarded=true;var seconds=GameManager.Instance.GetAliveTime();var xp=TalentCatalog.BaselineRunXp(seconds,kills,bosses,won);
        var before=C.PointsForXp(Plugin.Saved.TalentXp);Plugin.Saved.TalentXp+=xp;
        Plugin.Current.Log.LogInfo($"Azeroth Mage talent XP:seconds={seconds:F2},kills={kills},bosses={bosses},won={won}; award={xp}; total={Plugin.Saved.TalentXp}; points={before}->{C.PointsForXp(Plugin.Saved.TalentXp)}; testSession={Plugin.TestSession}. Source first-zone baseline; no zone/perk bonus.");
        Plugin.Save();
    }
}
