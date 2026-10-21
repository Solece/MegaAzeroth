using System.Text.Json;
using Assets.Scripts.Actors.Enemies;
using UnityEngine;

namespace AzerothAbilities;

internal static class Program
{
    static int checks;
    static readonly List<string> passed=new();
    static JsonElement Json(object value)=>JsonSerializer.SerializeToElement(value);
    static JsonElement View(SpellProjectile p)=>Json(p.Snapshot());
    static Vector3 Vector(JsonElement e)=>new(e[0].GetSingle(),e[1].GetSingle(),e[2].GetSingle());
    static Vector3 Position(SpellProjectile p){var e=View(p);return new(e.GetProperty("X").GetSingle(),e.GetProperty("Y").GetSingle(),e.GetProperty("Z").GetSingle());}
    static JsonElement[] Events()=>JsonDocument.Parse(TargetAcquisition.EventsJson).RootElement.EnumerateArray().Select(e=>e.Clone()).ToArray();
    static string Kind(JsonElement e)=>e.GetProperty("Kind").GetString();
    static void Require(bool ok,string message){checks++;if(!ok)throw new InvalidOperationException(message);}
    static bool Near(Vector3 a,Vector3 b)=>Vector3.Distance(a,b)<.00002f;
    static Enemy Enemy(uint id,float x)
    {
        var enemy=new Enemy{id=id};enemy.transform.position=new(x,0,0);enemy.collider=new Collider{bounds=new Bounds{center=enemy.transform.position}};
        AbilityRuntime.World.Add(enemy);return enemy;
    }
    static LearnedSpell Spell(bool dot=false)
    {var s=new LearnedSpell(new SpellDefinition{Kind=dot?"damageOverTime":"projectile"});AbilityRuntime.Spells.Add(s);return s;}
    static void Reset()
    {
        CombatSimulation.Time=0;CombatSimulation.CanSimulate=true;ClassCombat.GenericActive=false;
        TargetAcquisition.Reset();AbilityRuntime.World.Clear();AbilityRuntime.Spells.Clear();AbilityRuntime.Projectiles.Clear();AbilityRuntime.HitTargets.Clear();
        AbilityRuntime.OverrideCastRange=0;AbilityRuntime.CurrentCastSerial=1;
        SpellVfx.Live.Clear();SpellVfx.Impacts.Clear();SpellVfx.Released=0;ClassSpellBehaviors.Dots.Clear();
        TomeCompatibility.ProjectileSpeed=TomeCompatibility.Duration=TomeCompatibility.Size=1;TomeCompatibility.ExtraBounces=0;
        TomeCompatibilityDiagnostics.Created=TomeCompatibilityDiagnostics.Hits=TomeCompatibilityDiagnostics.Bounces=TomeCompatibilityDiagnostics.Visuals=0;
        TomeCompatibilityDiagnostics.CastProjectileSpeedOverride=0;
    }
    static bool Step(SpellProjectile projectile,float dt)
    {if(CombatSimulation.CanSimulate)CombatSimulation.Time+=dt;return projectile.Tick(dt);}
    static void Complete(SpellProjectile projectile,float dt=.125f)
    {
        int frames=0;while(Step(projectile,dt)){if(++frames>1000)throw new InvalidOperationException("Projectile did not finish within bounded fixture");}
    }
    public static void CheckArrivalBeforeDamage(Enemy target,string damageKind)
    {
        var e=Events().Last();Require(Kind(e)=="arrival-impact",damageKind+" damage must follow an actual arrival event");
        Require(e.GetProperty("AppliesDamage").GetBoolean(),"arrival must explicitly admit live-target damage");
        Require(e.GetProperty("TargetAlive").GetBoolean(),"dead targets cannot receive damage");
        Require(e.GetProperty("NewEnemy").GetUInt32()==target.id,"damage target must match arrived target ID");
        var destination=Vector(e.GetProperty("Destination"));
        Require(Near(Vector(e.GetProperty("Position")),destination),"projectile position reaches destination before damage");
        Require(e.GetProperty("VisualActive").GetBoolean(),"visual still exists at damage admission");
        Require(Near(Vector(e.GetProperty("VisualPosition")),destination),"visual is synchronized to destination before damage");
        Require(Near(Vector(e.GetProperty("ArrivalProjectilePosition")),destination),"arrival position is the destination");
    }
    static void Case(string name,Action run){Reset();run();passed.Add(name);}
    public static int Main(string[] args)
    {
        string error=null;
        try
        {
            Case("large Size cannot cause early direct damage",()=>
            {
                TomeCompatibility.Size=100;var target=Enemy(1,10);var projectile=new SpellProjectile(Spell(),target,new(0,0,0),7);
                Require(Step(projectile,.25f),"large Size must not resolve a ten-meter flight after half a meter");
                Require(Near(Position(projectile),new(.5f,0,0)),"real projectile moves by speed times delta");
                Require(AbilityRuntime.HitTargets.Count==0&&target.hp==100,"no damage before actual arrival");
                Complete(projectile);Require(target.hp==93&&TomeCompatibilityDiagnostics.Hits==1,"one hit at arrival");
                Require(Near(Position(projectile),new(10,0,0)),"direct-hit final position is target endpoint");
            });
            Case("dead target without replacement flies to last collider endpoint",()=>
            {
                var target=Enemy(1,10);target.collider.bounds=new Bounds{center=new(10,2,0)};
                var projectile=new SpellProjectile(Spell(),target,new(0,0,0),7);Require(Step(projectile,.25f),"initial live flight");
                var cached=Vector(View(projectile).GetProperty("LastValidDestination"));target.Dead=true;target.transform.position=new(90,0,0);target.collider.bounds=new Bounds{center=new(90,40,0)};
                Require(Step(projectile,.25f),"death without replacement must not despawn midflight");
                var view=View(projectile);Require(view.GetProperty("DestinationOnly").GetBoolean(),"fallback is a fixed world point");
                Require(Near(Vector(view.GetProperty("Destination")),cached),"pooled transform cannot rewrite the cached endpoint");
                Require(view.GetProperty("VisualActive").GetBoolean()&&SpellVfx.Released==0,"live visual survives target death");
                Require(SpellVfx.Impacts.Count==0&&AbilityRuntime.HitTargets.Count==0,"no premature impact or damage");
                Complete(projectile);Require(Near(Position(projectile),cached),"fallback reaches last known collider endpoint");
                Require(AbilityRuntime.HitTargets.Count==0&&TomeCompatibilityDiagnostics.Hits==0&&target.hp==100,"dead endpoint is cosmetic only");
                Require(SpellVfx.Impacts.Count==1&&Near(SpellVfx.Impacts[0],cached),"one impact at the cached endpoint");
                var arrival=Events().Single(e=>Kind(e)=="arrival-impact");Require(!arrival.GetProperty("AppliesDamage").GetBoolean(),"dead arrival explicitly denies damage");
                Require(arrival.GetProperty("Outcome").GetString()=="last-destination-no-damage","fallback outcome is explicit");
                projectile.Dispose();projectile.Dispose();Require(SpellVfx.Released==1,"visual release is once and only after finish");
            });
            Case("pooled native identity cannot receive old projectile damage",()=>
            {
                var target=Enemy(1,10);var projectile=new SpellProjectile(Spell(),target,new(0,0,0),7);Step(projectile,.25f);
                target.id=99;target.transform.position=new(100,0,0);target.collider.bounds=new Bounds{center=new(100,0,0)};
                Require(Step(projectile,.25f),"pooled identity mismatch keeps flight alive");Complete(projectile);
                Require(target.hp==100&&AbilityRuntime.HitTargets.Count==0,"old projectile cannot damage a rebound identity");
                Require(Near(SpellVfx.Impacts.Single(),new(10,0,0)),"pool reuse preserves the old endpoint");
            });
            Case("distinct volley and Quantity survive unclaimed retarget",()=>
            {
                var a=Enemy(1,10);var b=Enemy(2,12);var c=Enemy(3,14);var spell=Spell();
                var plan=TargetAcquisition.Prepare(spell,new(0,0,0),30,new(){a,b,c},4);
                var projectiles=plan.Assigned.Select((e,i)=>new SpellProjectile(spell,e,new(0,0,0),7,volley:plan,projectileIndex:i)).ToArray();
                Require(plan.Assigned.Select(e=>e.id).SequenceEqual(new uint[]{1,2,3,1}),"actual production assignment remains distinct then round robin");
                Require(TomeCompatibilityDiagnostics.Created==4,"retarget must not alter Quantity or create another projectile");
                var replacement=Enemy(4,16);Step(projectiles[0],.25f);a.Dead=true;
                Require(Step(projectiles[0],.25f),"retarget continues the existing projectile");
                var retarget=Events().Single(e=>Kind(e)=="retarget-dead");Require(retarget.GetProperty("NewEnemy").GetUInt32()==4,"unclaimed target wins over closer reserved targets");
                Require(AbilityRuntime.HitTargets.Count==0&&replacement.hp==100,"retarget does not deal early damage");
                Complete(projectiles[0]);Require(AbilityRuntime.HitTargets.SequenceEqual(new uint[]{4}),"same projectile damages the live replacement only on arrival");
                Require(TomeCompatibilityDiagnostics.Created==4,"projectile count remains fixed after retarget");
                foreach(var p in projectiles)p.Dispose();
            });
            Case("fallback flight and ages freeze during pause",()=>
            {
                var target=Enemy(1,10);var projectile=new SpellProjectile(Spell(),target,new(0,0,0),7);Step(projectile,.25f);target.Dead=true;Step(projectile,.25f);
                var before=View(projectile);var position=Position(projectile);CombatSimulation.CanSimulate=false;
                Require(Step(projectile,30),"paused live projectile is retained");var after=View(projectile);
                Require(Near(position,Position(projectile)),"paused projectile has no movement");
                Require(before.GetProperty("Age").GetSingle()==after.GetProperty("Age").GetSingle(),"combat-clock age remains frozen");
                Require(SpellVfx.Impacts.Count==0&&AbilityRuntime.HitTargets.Count==0,"pause cannot cause impact or damage");
                CombatSimulation.CanSimulate=true;Require(Step(projectile,.25f),"resume continues fallback flight");
                Require(!Near(position,Position(projectile)),"position advances on resume");Complete(projectile);
            });
            Case("death during delayed launch preserves the constructor endpoint",()=>
            {
                var target=Enemy(1,10);var projectile=new SpellProjectile(Spell(),target,new(0,0,0),7,launchDelay:2);
                target.Dead=true;target.collider.bounds=new Bounds{center=new(50,0,0)};
                Require(Step(projectile,1),"delayed projectile stays queued");Require(SpellVfx.Live.Count==0&&Near(Position(projectile),new(0,0,0)),"delay cannot launch visual or move");
                Require(Step(projectile,1),"delayed orphan starts a real flight");Require(View(projectile).GetProperty("DestinationOnly").GetBoolean(),"delayed orphan uses cached endpoint");
                Complete(projectile);Require(Near(SpellVfx.Impacts.Single(),new(10,0,0)),"delayed endpoint is not the dead transform");
            });
            Case("finite range expiry remains explicit without damage",()=>
            {
                var target=Enemy(1,10);var projectile=new SpellProjectile(Spell(),target,new(0,0,0),7);Step(projectile,.25f);
                target.transform.position=new(100,0,0);target.collider.bounds=new Bounds{center=target.transform.position};Complete(projectile);
                var view=View(projectile);Require(view.GetProperty("FinishReason").GetString()=="range-expired","moving target cannot create an infinite travel budget");
                Require(MathF.Abs(view.GetProperty("Distance").GetSingle()-view.GetProperty("Range").GetSingle())<.00001f,"travel is clamped to finite budget");
                Require(AbilityRuntime.HitTargets.Count==0&&SpellVfx.Impacts.Count==0,"range expiry cannot fake a destination impact");
                Require(Events().Any(e=>Kind(e)=="expired-range"),"range expiration is diagnosed");
            });
            Case("bounce keeps one projectile and accounts for actual final segment",()=>
            {
                TomeCompatibility.ExtraBounces=1;var target=Enemy(1,10);Enemy(2,14);
                var projectile=new SpellProjectile(Spell(),target,new(0,0,0),7);Complete(projectile);
                Require(AbilityRuntime.HitTargets.SequenceEqual(new uint[]{1,2}),"native tome bounce still hits two different targets");
                Require(TomeCompatibilityDiagnostics.Created==1&&TomeCompatibilityDiagnostics.Bounces==1,"bounce does not create another projectile");
                Require(MathF.Abs(View(projectile).GetProperty("Distance").GetSingle()-14)<.00002f,"arrival segments consume real travel range");
            });
            Case("large timestep preserves real distance and bounce budget",()=>
            {
                TomeCompatibility.ExtraBounces=1;var target=Enemy(1,3);Enemy(2,7);
                var projectile=new SpellProjectile(Spell(),target,new(0,0,0),7,speedOverride:1000);
                Require(Step(projectile,1),"nearby first hit retains second-target bounce");
                Require(AbilityRuntime.HitTargets.SequenceEqual(new uint[]{1}),"first endpoint resolves once");
                Require(MathF.Abs(View(projectile).GetProperty("Distance").GetSingle()-3)<.00002f,"large attempted step consumes only actual three meters");
                Require(View(projectile).GetProperty("RemainingRange").GetSingle()>3,"unused range survives a nearby arrival");
                Require(!Step(projectile,1),"second endpoint resolves on next tick");
                Require(AbilityRuntime.HitTargets.SequenceEqual(new uint[]{1,2}),"second actual target receives one hit");
                Require(MathF.Abs(View(projectile).GetProperty("Distance").GetSingle()-7)<.00002f,"total path remains seven meters");
            });
            Case("moving DoT applies only at scheduled actual visual arrival",()=>
            {
                ClassCombat.GenericActive=true;var target=Enemy(1,10);var projectile=new SpellProjectile(Spell(true),target,new(0,0,0),7);
                Require(Step(projectile,4.75f),"DoT waits for source distance/speed delivery time");Require(ClassSpellBehaviors.Dots.Count==0,"DoT cannot start before arrival");
                Require(!Step(projectile,.25f),"DoT completes its delivery on arrival");Require(ClassSpellBehaviors.Dots.Contains(1),"actual arrival applies one DoT");
                Require(Near(Position(projectile),new(10,0,0)),"DoT visual reaches actual endpoint before application");
            });
            Case("dead moving DoT produces a last-point impact without a status",()=>
            {
                ClassCombat.GenericActive=true;var target=Enemy(1,10);var projectile=new SpellProjectile(Spell(true),target,new(0,0,0),7);Step(projectile,.25f);
                target.Dead=true;Require(Step(projectile,.25f),"dead DoT delivery remains in flight");Complete(projectile);
                Require(ClassSpellBehaviors.Dots.Count==0&&TomeCompatibilityDiagnostics.Hits==0,"dead target never receives a status or hit");
                Require(Near(SpellVfx.Impacts.Single(),new(10,0,0)),"dead DoT has one last-point impact");
            });
        }
        catch(Exception ex){error=ex.ToString();}
        var report=new{Passed=error==null,Checks=checks,Cases=passed,Error=error,
            Subject="Exact extracted candidate SpellProjectile plus linked production TargetAcquisition/VolleyPlan",
            Fixture="Managed Unity/native-world stand-ins; not native rendering or actual game verification",UseAppHost=false,NativeGameplayVerified=false};
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
        if(args.Length>0)File.WriteAllText(args[0],json);Console.WriteLine(json);return error==null?0:1;
    }
}
