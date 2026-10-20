using System.Reflection;
using System.Text.Json;
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

// Source catalogs select behavior; original enum payloads never fall through to an
// unrelated Mage effect. The old Mage runtime remains available until native probes
// explicitly enable its generic replacement.
public static class TalentRuntime
{
    public static bool GenericMageEnabled=>C?.ClassId=="mage"&&UsesGenericRuntime;
    public static bool UsesGenericRuntime
    {
        get
        {
            var authoritative=CombatType()?.GetProperty("GenericActive",BindingFlags.Public|BindingFlags.Static);
            if(authoritative!=null)return authoritative.GetValue(null) is bool active&&active;
            return Plugin.Catalog!=null&&Plugin.Catalog.ClassId!="mage";
        }
    }
    public static int EchoCount=>UsesGenericRuntime?echoCount:LegacyTalentRuntime.EchoCount;
    public static int FreeCastCount=>UsesGenericRuntime?freeCount:LegacyTalentRuntime.FreeCastCount;
    public static int IgniteTickCount=>UsesGenericRuntime?dotTicks:LegacyTalentRuntime.IgniteTickCount;
    public static int ControlCount=>UsesGenericRuntime?controlCount:LegacyTalentRuntime.ControlCount;
    public static int ChainCount=>UsesGenericRuntime?chainCount:LegacyTalentRuntime.ChainCount;
    public static int SplitCount=>UsesGenericRuntime?splashCount:LegacyTalentRuntime.SplitCount;
    public static int ExplosionCount=>UsesGenericRuntime?explosionCount:LegacyTalentRuntime.ExplosionCount;
    static TalentState S=>Plugin.Run;
    static TalentCatalog C=>Plugin.Catalog;
    static float Now=>CombatSimulation.Time;
    static int depth,rawDepth,healDepth,echoCount,freeCount,dotTicks,controlCount,chainCount,splashCount,explosionCount,kills,bosses,petSerial;
    static bool started,awarded;
    static Type combat;
    static readonly Dictionary<string,float> Next=new();
    static readonly Dictionary<string,int> Counters=new();
    static readonly Dictionary<string,Queue<float>> Rates=new();
    static readonly Dictionary<(IntPtr,uint),Stack<LearnedSpell>> HitContexts=new();
    static readonly HashSet<(IntPtr,uint)> Killed=new();
    static readonly Dictionary<(IntPtr,uint),List<Affliction>> Afflictions=new();
    static readonly Dictionary<string,List<Pet>> Pets=new();
    static readonly HashSet<string> InitialEffects=new();
    static readonly HashSet<string> Failures=new();
    static readonly Dictionary<int,int> Events=new();
    static readonly Dictionary<string,int> Applied=new();
    sealed class Affliction {internal LearnedSpell Spell;internal float Expires;}
    sealed class Pet {internal string Id;internal float Expires;}
    static bool Alive(Enemy e)=>e!=null&&!e.IsDeadOrDyingNextFrame();
    static IEnumerable<(TalentDefinition Talent,TalentEffect Effect,int Rank)> Effects=>C==null?Enumerable.Empty<(TalentDefinition,TalentEffect,int)>():S.Selected(C);
    static string Key(TalentDefinition t,TalentEffect e)=>C.ClassId+":"+t.Id+":"+e.SourceOffset;
    static void Count(string kind){Applied.TryGetValue(kind,out var n);Applied[kind]=n+1;}
    static void Fail(string message){if(Failures.Add(message))Plugin.Current?.Log.LogWarning("Source talent adapter: "+message);}
    static Type CombatType()=>combat??=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothAbilities.ClassCombat")).FirstOrDefault(t=>t!=null);
    static bool Call(string name,object[] args,out object result)
    {
        result=null;
        try
        {
            var method=CombatType()?.GetMethods(BindingFlags.Public|BindingFlags.Static).FirstOrDefault(m=>m.Name==name&&m.GetParameters().Length==args.Length);
            if(method==null){Fail("Missing ClassCombat."+name);return false;}
            result=method.Invoke(null,args);return true;
        }
        catch(Exception ex){Fail(name+":"+(ex.InnerException??ex).Message);return false;}
    }
    static bool Call(string name,params object[] args)=>Call(name,args,out _);
    static float Number(string name)
    {
        try{return Convert.ToSingle(CombatType()?.GetProperty(name,BindingFlags.Public|BindingFlags.Static)?.GetValue(null)??0);}
        catch{return 0;}
    }
    static float MaxHp=>Number("MaximumHealth");
    static float Hp=>Number("CurrentHealth");
    static Vector3 At=>MyPlayer.Instance?.transform.position??Vector3.zero;
    static float Heal(float value,string id)
    {if(value<=0)return 0;return Call("Heal",new object[]{value,id},out var result)&&result!=null?Convert.ToSingle(result):0;}
    static bool Rate(string key,int cap)
    {
        if(cap<=0)return true;
        if(!Rates.TryGetValue(key,out var q))Rates[key]=q=new();
        while(q.Count>0&&Now-q.Peek()>=1)q.Dequeue();
        if(q.Count>=cap)return false;q.Enqueue(Now);return true;
    }
    static bool Ready(string key,float interval)
    {if(Next.TryGetValue(key,out var next)&&Now<next)return false;Next[key]=Now+Mathf.Max(0,interval);return true;}
    static bool Free(string id,LearnedSpell trigger=null)
    {
        if(depth>0)return false;if(string.IsNullOrEmpty(id))id=trigger?.Definition.Id;if(string.IsNullOrEmpty(id))return false;
        depth++;
        try{var result=AbilityRuntime.CastFreeSpell(id);if(result){freeCount++;Count("freeCast");}return result;}
        finally{depth--;}
    }
    static int SourceKind(LearnedSpell spell)=>spell?.Definition.GetType().GetProperty("SourceKind")?.GetValue(spell.Definition) is int n?n:0;
    static void Visual(string id,string phase,Vector3 at,float radius,float duration)=>MageSpellBehaviors.VisualEvent?.Invoke(id,phase,at,radius,duration);
    public static float SourceStat(int id)=>C==null?0:S.Stat(C,id);
    public static float SourceSpellModifier(LearnedSpell spell,int kind)=>C==null?0:S.SpellModifier(C,spell,kind);
    public static bool ChainNoFalloff=>Effects.Any(t=>t.Effect.Tag==23);
    public static string DiagnosticsJson=>JsonSerializer.Serialize(new{ClassId=C?.ClassId,UsesGenericRuntime,GenericMageEnabled,Events,Applied,Failures=Failures.ToArray(),Kills=kills,Bosses=bosses,ActiveHitContexts=HitContexts.Sum(p=>p.Value.Count),ActivePets=Pets.Sum(p=>p.Value.Count),EchoCount,FreeCastCount,IgniteTickCount,ControlCount,ChainCount,SplitCount,ExplosionCount});
    public static void Clear()
    {
        LegacyTalentRuntime.Clear();
        foreach(var list in Pets.Values)foreach(var pet in list)if(CombatType()!=null)Call("DespawnPet",pet.Id);
        Next.Clear();Counters.Clear();Rates.Clear();HitContexts.Clear();Killed.Clear();Afflictions.Clear();Pets.Clear();InitialEffects.Clear();Events.Clear();Applied.Clear();Failures.Clear();
        depth=rawDepth=healDepth=echoCount=freeCount=dotTicks=controlCount=chainCount=splashCount=explosionCount=kills=bosses=petSerial=0;started=awarded=false;
    }
    public static string Starter(string current)
    {
        if(!UsesGenericRuntime)return LegacyTalentRuntime.Starter(current);
        foreach(var selected in Effects)if(selected.Effect.Tag==28)return selected.Effect.Spell;
        return current;
    }
    public static void AddStartingSpells()
    {
        if(!UsesGenericRuntime){LegacyTalentRuntime.AddStartingSpells();return;}
        if(!AbilityRuntime.Active)return;
        foreach(var t in Effects.Where(t=>t.Effect.Tag==27))AbilityRuntime.LearnStartingSpell(t.Effect.Spell);
    }
    public static void AddDebugStartingSpells()
    {if(!UsesGenericRuntime)return;foreach(var t in Effects.Where(t=>t.Effect.Tag is 27 or 28))AbilityRuntime.LearnStartingSpell(t.Effect.Spell);}
    public static float Radius(LearnedSpell spell)=>UsesGenericRuntime?1:LegacyTalentRuntime.Radius(spell);
    public static float Barrier(LearnedSpell spell)=>UsesGenericRuntime?1:LegacyTalentRuntime.Barrier(spell);
    public static void BeforeHit(LearnedSpell spell,Enemy enemy,DamageContainer dc)
    {
        if(!UsesGenericRuntime){LegacyTalentRuntime.BeforeHit(spell,enemy,dc);return;}
        PushContext(enemy,spell);
    }
    public static void AfterHit(LearnedSpell spell,Enemy enemy,DamageContainer dc,float hpBefore)
    {if(!UsesGenericRuntime)LegacyTalentRuntime.AfterHit(spell,enemy,dc,hpBefore);else PopContext(enemy);}
    static void PushContext(Enemy enemy,LearnedSpell spell)
    {
        if(enemy==null)return;
        var key=(enemy.Pointer,enemy.id);
        if(!HitContexts.TryGetValue(key,out var stack))HitContexts[key]=stack=new();
        stack.Push(spell);
    }
    static void PopContext(Enemy enemy)
    {
        if(enemy==null)return;var key=(enemy.Pointer,enemy.id);
        if(!HitContexts.TryGetValue(key,out var stack))return;
        if(stack.Count>0)stack.Pop();if(stack.Count==0)HitContexts.Remove(key);
    }
    public static void AfterCast(LearnedSpell spell)
    {if(!UsesGenericRuntime)LegacyTalentRuntime.AfterCast(spell);}
    static LearnedSpell Synthetic(string id,string school,int dbc=0,int cast=6)
    {
        var d=new SpellDefinition{Id=id,Name=id,School=school,Kind="damageOverTime",CastKindId=cast,Range=40,Radius=8,Targets=1};
        d.GetType().GetProperty("SourceSpellId")?.SetValue(d,dbc);return new LearnedSpell(d);
    }
    static void Raw(Enemy enemy,float damage,string school,Vector3 from,string id)
    {
        if(!CombatSimulation.CanSimulate||!Alive(enemy)||damage<=0)return;
        var dc=new DamageContainer(0,"AzerothTalent:"+id){damage=damage,crit=false,knockback=0,enemy=enemy,direction=(enemy.transform.position-from).normalized,element=school=="Fire"?EElement.Fire:EElement.Neutral};
        // Generated talent damage must not inherit a surrounding cast's kill
        // filter; nested source hits restore their own previous context.
        PushContext(enemy,null);rawDepth++;try{enemy.DamageFromPlayerOther(dc);}finally{rawDepth--;PopContext(enemy);}
    }
    static void Area(Vector3 center,float radius,float damage,string school,string id,Enemy exclude=null)
    {
        if(radius<=0||damage<=0)return;
        foreach(var enemy in AbilityRuntime.Targets(center,radius,false).Where(e=>exclude==null||e.Pointer!=exclude.Pointer))Raw(enemy,damage,school,center,id);
        Visual(id,"impact",center,radius,.6f);
    }
    static bool Afflicted(Enemy enemy,TalentEffect filter)
    {
        if(enemy==null)return false;
        if(!Afflictions.TryGetValue((enemy.Pointer,enemy.id),out var list))return false;
        foreach(var affliction in list.Where(a=>filter.Matches(a.Spell)))
            if(Call("HasDot",new object[]{enemy,affliction.Spell.Definition.Id},out var exists)&&exists is bool live&&live)return true;
        return false;
    }
    static void MarkAffliction(LearnedSpell spell,Enemy enemy)
    {
        if(spell==null||enemy==null||spell.Definition.CastKindId!=6)return;
        if(!Afflictions.TryGetValue((enemy.Pointer,enemy.id),out var list))Afflictions[(enemy.Pointer,enemy.id)]=list=new();
        var duration=Mathf.Max(.1f,spell.Definition.Duration*1.4f*spell.DurationFactor*(1+SourceSpellModifier(spell,4)));
        var prior=list.FirstOrDefault(a=>a.Spell.Definition.Id==spell.Definition.Id);
        if(prior==null)list.Add(new Affliction{Spell=spell,Expires=Now+duration});else prior.Expires=Now+duration;
    }
    public static void NotifySourceEvent(int eventId,LearnedSpell spell,Enemy enemy,float value)
    {
        if(!UsesGenericRuntime||!AbilityRuntime.Active||C==null||(eventId!=6&&(depth>0||rawDepth>0)))return;
        if(eventId==6&&enemy!=null&&!Killed.Add((enemy.Pointer,enemy.id)))return;
        Events.TryGetValue(eventId,out var count);Events[eventId]=count+1;
        if(eventId is 1 or 4)MarkAffliction(spell,enemy);
        if(eventId==4&&spell!=null&&C.Find(spell.Definition.Id)?.Effects.Any(e=>e.Tag==9)==true)dotTicks++;
        if(eventId==1&&value>0)Heal(value*Mathf.Max(0,SourceStat(14)),"talent-lifesteal");
        if(eventId==6){kills++;if(enemy!=null&&(enemy.IsBoss()||enemy.IsStageBoss()||enemy.IsFinalBoss()))bosses++;Heal(SourceStat(15),"talent-kill-heal");}
        if(eventId==8&&enemy!=null)Raw(enemy,value*Mathf.Max(0,SourceStat(22)),"Physical",At,"talent-reflect");
        // Shield amount modifiers for the same source cast share one cap, rather
        // than creating independent Fortified/Shield Mastery shields.
        var shields=Effects.Where(t=>t.Effect.Tag==18&&t.Effect.TriggerEvent==eventId&&t.Effect.Matches(spell)).ToArray();
        if(shields.Length>0&&MaxHp>0)
        {
            Call("ApplyShieldCapped",shields.Sum(t=>t.Effect.Value*t.Rank)*MaxHp,shields.Max(t=>t.Effect.Cap),"talent-shield:"+eventId+":"+(spell?.Definition.Id??"all"));Count("triggeredShield");
        }
        foreach(var (talent,e,rank) in Effects.ToArray())
        {
            var key=Key(talent,e);var matches=e.Matches(spell);
            switch(e.Tag)
            {
                case 0:
                    if(!EventMatches(e.TriggerEvent,eventId)||!matches||(eventId==9&&(MaxHp<=0||Hp/MaxHp>=e.EventValue)))break;
                    if(Next.TryGetValue(key,out var next)&&Now<next)break;
                    if(UnityEngine.Random.value>=Mathf.Clamp01(e.Chance*rank))break;
                    if(Free(e.Spell,spell))Next[key]=Now+Mathf.Max(0,e.InternalCooldown);break;
                case 1:
                    if(!EventMatches(e.TriggerEvent,eventId)||!matches)break;
                    Counters.TryGetValue(key,out var n);n++;
                    if(n>=Math.Max(1,e.Count)){n-=Math.Max(1,e.Count);if(Rate(key,e.MaxPerSecond))Free(e.Spell,spell);}Counters[key]=n;break;
                case 2:
                    if(eventId==6&&UnityEngine.Random.value<Mathf.Clamp01(e.Chance*rank))Spawn(talent,e,e.Duration,e.MaxPets);break;
                case 4:
                    if(eventId==1&&matches&&enemy!=null){Area(enemy.transform.position,e.Radius,value*e.Value*rank,spell?.Definition.School??"Physical",talent.Id,enemy);splashCount++;}break;
                case 5:
                    if(eventId==1&&matches&&Alive(enemy))Raw(enemy,enemy.maxHp*e.Value*rank,spell?.Definition.School??"Physical",At,talent.Id);break;
                case 6:
                    if(eventId!=1||!matches||enemy==null)break;
                    foreach(var other in AbilityRuntime.Targets(enemy.transform.position,e.Radius,false).Where(t=>t.Pointer!=enemy.Pointer).Take(Math.Max(0,e.Targets)))
                    {Raw(other,value*e.DamageFactor*rank,spell?.Definition.School??"Physical",enemy.transform.position,talent.Id);chainCount++;}break;
                case 7:
                    if(eventId==1&&matches&&Alive(enemy)&&UnityEngine.Random.value<Mathf.Clamp01(e.Chance*rank))
                        for(var i=0;i<e.Count;i++)Raw(enemy,value*e.DamageFactor,spell?.Definition.School??"Physical",At,talent.Id);break;
                case 8:
                    if((e.TriggerEvent==4?eventId!=4:eventId!=1)||!matches||enemy==null)break;
                    if(UnityEngine.Random.value>=Mathf.Clamp01(e.Chance*(e.Status==-1?1:rank)))break;
                    var statusValue=e.StatusValue*(e.Status==-1?rank:1);
                    if(e.Status==5)Heal(value*statusValue,key);
                    else if(e.Status==6)Heal(MaxHp*statusValue,key);
                    else if(e.Status is 2 or 3 or 4){MageSpellBehaviors.ApplyRoot(enemy,statusValue,e.Status==3);controlCount++;}
                    else if(e.Status is 1 or -1){Call("ApplyTalentStatus",enemy,e.Status,statusValue,e.StatusDuration,key);controlCount++;}
                    else Fail("Unimplemented source talent hit status:"+e.Status);break;
                case 9:
                    if(!EventMatches(e.TriggerEvent,eventId)||!matches||!Alive(enemy))break;
                    var dot=Synthetic(talent.Id,spell?.Definition.School??"Fire",e.SpellId);dot.Definition.Duration=e.Duration;
                    dot.Definition.Name=talent.Name;dot.Definition.Icon=talent.Icon;dot.Rank=rank;
                    if(Call("ApplyTalentDot",enemy,dot,value*e.Value*rank,e.Duration,Math.Max(1,e.Ticks),talent.Id))
                    {MarkAffliction(dot,enemy);Count("critDot");}break;
                case 10:
                    if(eventId==6&&matches&&Ready(key,e.Interval))foreach(var s in AbilityRuntime.Spells.Where(s=>e.Matches(s)))s.NextCast=Now;break;
                case 11:
                    if(eventId!=6||enemy==null||!(e.TriggerOnAfflictedDeath?Afflicted(enemy,e):matches)||!Rate(key,e.MaxPerSecond))break;
                    Area(enemy.transform.position,e.Radius,enemy.maxHp*e.Value*rank,spell?.Definition.School??"Shadow",talent.Id,enemy);explosionCount++;break;
                case 14:
                    if(eventId!=6||enemy==null||!Afflicted(enemy,e))break;
                    foreach(var other in AbilityRuntime.Targets(enemy.transform.position,40,false).Where(t=>t.Pointer!=enemy.Pointer).Take(Math.Max(1,e.Targets)))
                    {
                        Call("CopyDots",enemy,other,e.Spells);
                        if(Afflictions.TryGetValue((enemy.Pointer,enemy.id),out var afflicted))
                        {
                            if(!Afflictions.TryGetValue((other.Pointer,other.id),out var transferred))Afflictions[(other.Pointer,other.id)]=transferred=new();
                            foreach(var a in afflicted.Where(a=>a.Expires>Now&&e.Matches(a.Spell)))transferred.Add(new Affliction{Spell=a.Spell,Expires=a.Expires});
                        }
                    }Count("spreadDotOnDeath");break;
                case 15:
                    if(e.TriggerEvent==eventId&&matches){Call("ApplyTimedStat",e.Stat,e.Value*rank,e.Duration,key);Count("triggeredTimedStatBuff");}break;
                case 17:
                    if(e.TriggerEvent==eventId&&matches&&Ready(key,e.Interval)){Heal(MaxHp*e.Value*rank,key);Count("triggeredHeal");}break;
            }
        }
        if(eventId==7&&spell!=null)
        {
            var chance=Mathf.Clamp01(SourceStat(13));
            if(UnityEngine.Random.value<chance&&Free(spell.Definition.Id))
            {
                echoCount++;var extra=Effects.Where(t=>t.Effect.Tag==24).Select(t=>t.Effect.MaxAdditional).DefaultIfEmpty(0).Max();
                for(var i=0;i<extra;i++){if(UnityEngine.Random.value>=chance)break;if(Free(spell.Definition.Id))echoCount++;}
            }
        }
    }
    static void Spawn(TalentDefinition t,TalentEffect e,float duration,int maximum)
    {
        var key=Key(t,e);if(!Pets.TryGetValue(key,out var pets))Pets[key]=pets=new();
        if(maximum>0&&pets.Count>=maximum)return;
        var id=key+":"+petSerial++;
        if(Call("SpawnPet",e.PetDisplayId,duration,1f,e.PetCooldown,0,id)
            &&Call("HasPet",new object[]{id},out var exists)&&exists is bool present&&present)
        {pets.Add(new Pet{Id=id,Expires=duration>0?Now+duration:float.PositiveInfinity});Count(e.Tag==2?"killSummon":"summon");}
    }
    public static void NotifyHeal(float requested,float actual,string origin)
    {
        if(!UsesGenericRuntime||!AbilityRuntime.Active||healDepth>0||requested<=0)return;
        healDepth++;
        try
        {
            var excess=Mathf.Max(0,requested-actual);
            foreach(var (t,e,rank) in Effects.ToArray())
            {
                if(e.Tag==20&&excess>0){Call("ApplyShieldCapped",excess,e.Cap,Key(t,e));Count("overhealShield");}
                if(e.Tag==21&&actual>0){Area(At,e.Radius,actual*e.DamageFactor,e.School,t.Id);Count("healDamageNova");}
            }
        }
        finally{healDepth--;}
    }
    public static void NotifyShieldBreak(float absorbed,float peak,string origin)
    {
        if(!UsesGenericRuntime||!AbilityRuntime.Active)return;
        foreach(var (t,e,rank) in Effects.Where(t=>t.Effect.Tag==3).ToArray())
        {Area(At,e.Radius,Mathf.Min(Mathf.Max(0,absorbed),Mathf.Max(0,peak))*e.Value*rank,e.School,t.Id);Count("shieldBreakBurst");}
        NotifySourceEvent(10,null,null,absorbed);
    }
    public static bool TryPreventDeath(DamageContainer dc)
    {
        if(!UsesGenericRuntime||!AbilityRuntime.Active||dc==null||dc.damage<Hp||Hp<=0||MaxHp<=0)return false;
        foreach(var (t,e,rank) in Effects.Where(t=>t.Effect.Tag==19))
        {
            var key=Key(t,e);if(!Ready(key,e.Interval))continue;
            var target=MaxHp*e.Value;var current=Hp;
            if(target>current)Heal(target-current,key);
            dc.damage=Mathf.Max(0,Hp-target);
            if(e.Duration>0)Call("SetInvulnerable",e.Duration,key);
            Count("cheatDeath");NotifySourceEvent(11,null,dc.enemy,dc.damage);return true;
        }
        return false;
    }
    internal static void OnDeath(Enemy enemy,DamageContainer dc)
    {
        if(!UsesGenericRuntime){LegacyTalentRuntime.OnDeath(enemy,dc);return;}
        if(enemy==null)return;HitContexts.TryGetValue((enemy.Pointer,enemy.id),out var stack);var spell=stack?.Count>0?stack.Peek():null;
        NotifySourceEvent(6,spell,enemy,dc?.damage??0);
    }
    public static void Tick()
    {
        if(!UsesGenericRuntime){LegacyTalentRuntime.Tick();return;}
        if(!AbilityRuntime.Active||C==null||MyPlayer.Instance==null||!MyPlayer.Instance.hasStarted||!CombatSimulation.CanSimulate)return;
        if(!started){started=true;AddStartingSpells();Plugin.Current?.Log.LogInfo($"Original source talent run:{C.ClassId}; nodes={C.Talents.Count}; selected={S.Ranks.Count}; spent={S.Spent}");}
        // Native stage rebinding clears ClassCombat scene pets while retaining
        // this run's talent allocation and cooldowns. Drop stale bookkeeping
        // against the authoritative runtime so permanent summons can rebind.
        foreach(var list in Pets.Values)
            for(int i=list.Count-1;i>=0;i--)
            {
                var pet=list[i];
                if(Now>=pet.Expires){Call("DespawnPet",pet.Id);list.RemoveAt(i);continue;}
                if(Call("HasPet",new object[]{pet.Id},out var exists)&&exists is bool present&&!present)
                {list.RemoveAt(i);Count("missingPetReconciled");}
            }
        foreach(var (t,e,rank) in Effects.ToArray())
        {
            var key=Key(t,e);
            if(e.Tag==25&&!InitialEffects.Contains(key)){if(Call("SetForm",e.SpellId)){InitialEffects.Add(key);Count("shapeshift");}}
            if(e.Tag==29)
            {
                bool present=Pets.TryGetValue(key,out var pets)&&pets.Count>0;
                if(!present&&InitialEffects.Remove(key))Count("permanentPetRebind");
                if(!InitialEffects.Contains(key)){Spawn(t,e,0,1);if(Pets.TryGetValue(key,out pets)&&pets.Count>0)InitialEffects.Add(key);}
            }
            if(e.Tag!=26)continue;
            if(!Next.ContainsKey(key))
            {
                var interval=PeriodicInterval(e);
                Next[key]=Now+Mathf.Max(.1f,interval);continue;
            }
            if(Now<Next[key])continue;
            var wait=PeriodicInterval(e);
            if(Free(e.Spell))Count("periodicCast");Next[key]=Now+Mathf.Max(.1f,wait);
        }
        var regen=SourceStat(24);if(regen>0)Heal(MaxHp*regen*CombatSimulation.DeltaTime,"talent-percent-regen");
        if(MaxHp>0)NotifySourceEvent(9,null,null,Hp/MaxHp);
    }
    static bool EventMatches(int sourceEvent,int adapterEvent)=>sourceEvent==0?adapterEvent==1:sourceEvent==adapterEvent;
    static float PeriodicInterval(TalentEffect e)
    {
        if(e.Interval>0)return e.Interval;
        var active=AbilityRuntime.Spells.FirstOrDefault(s=>s.Definition.Id==e.Spell);
        if(active!=null)return AbilityRuntime.Cooldown(active);
        if(e.PeriodicBaseCooldown<=0){Fail("Missing original periodic cooldown:"+e.Spell);return float.PositiveInfinity;}
        var synthetic=Synthetic(e.Spell,e.PeriodicSpellSchool);
        return Mathf.Max(.25f,e.PeriodicBaseCooldown*1.4f*Mathf.Max(0,1-SourceSpellModifier(synthetic,1)))/Mathf.Max(.1f,PlayerStats.GetStat(EStat.AttackSpeed));
    }
    internal static void EndRun(bool won)
    {
        if(!UsesGenericRuntime){LegacyTalentRuntime.EndRun(won);return;}
        if(!AbilityRuntime.Active||!started||awarded||GameManager.Instance==null)return;
        awarded=true;var seconds=GameManager.Instance.GetAliveTime();var xp=TalentCatalog.BaselineRunXp(seconds,kills,bosses,won);
        Plugin.Saved.TalentXp+=xp;Plugin.Current?.Log.LogInfo($"Original {C.ClassId} talent XP: seconds={seconds:F1}; kills={kills}; bosses={bosses}; won={won}; award={xp}; total={Plugin.Saved.TalentXp}; test={Plugin.TestSession}");Plugin.Save();
    }
}
