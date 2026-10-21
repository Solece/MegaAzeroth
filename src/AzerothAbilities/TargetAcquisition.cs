using System.Text.Json;
using Assets.Scripts.Actors.Enemies;
using UnityEngine;

namespace AzerothAbilities;

// User-requested arena policy; these changes are not original Azeroth rules.
internal static class TargetAcquisition
{
    internal const float DetectionMultiplier=1.2f;
    static int nextVolley,nextProjectile;
    static object lastVolley;
    static readonly Queue<object> events=new();
    internal static float DetectionRange(float sourceRange)=>sourceRange>0&&float.IsFinite(sourceRange)?sourceRange*DetectionMultiplier:sourceRange;
    internal static List<Enemy> SkillTargets(Vector3 origin,float sourceRange,bool priority)
        =>AbilityRuntime.Targets(origin,DetectionRange(sourceRange),priority).GroupBy(e=>e.id).Select(g=>g.First()).ToList();
    internal static List<Enemy> DistinctNearest(IEnumerable<Enemy> candidates,Vector3 origin)
        =>candidates.GroupBy(e=>e.id).Select(g=>g.First()).OrderBy(e=>(e.transform.position-origin).sqrMagnitude).ThenBy(e=>e.id).ToList();
    internal static VolleyPlan Prepare(LearnedSpell spell,Vector3 origin,float sourceRange,List<Enemy> eligible,int quantity)
    {
        var targets=DistinctNearest(eligible,origin);
        var plan=new VolleyPlan(++nextVolley,AbilityRuntime.CurrentCastSerial,spell.Definition.Id,sourceRange,origin,targets,quantity);
        lastVolley=new{plan.VolleyId,plan.CastToken,Spell=plan.Spell,SourceRange=sourceRange,DetectionRange=DetectionRange(sourceRange),Quantity=quantity,
            Origin=new[]{origin.x,origin.y,origin.z},EligibleIds=targets.Select(e=>e.id).ToArray(),
            EligibleDistances=targets.Select(e=>Vector3.Distance(origin,e.transform.position)).ToArray(),AssignedIds=plan.Assigned.Select(e=>e.id).ToArray(),
            RoundRobinUsed=quantity>targets.Count&&targets.Count>0};
        return plan;
    }
    internal static int NewProjectile()=>++nextProjectile;
    internal static void Record(object value)
    {events.Enqueue(value);while(events.Count>512)events.Dequeue();}
    internal static void ProjectileEvent(string kind,int projectile,int index,VolleyPlan plan,string spell,int castToken,uint oldEnemy,uint newEnemy,Vector3 position,float range,float remaining,IEnumerable<Enemy> eligible=null,float targetDistance=-1,Vector3? destination=null,float age=0,bool targetAlive=false,bool appliesDamage=false,string outcome=null,bool visualActive=false,float[] visualPosition=null)
        =>Record(new{Kind=kind,Time=CombatSimulation.Time,ProjectileId=projectile,ProjectileIndex=index,VolleyId=plan?.VolleyId??0,CastToken=castToken,Spell=spell,
            OldEnemy=oldEnemy,NewEnemy=newEnemy,Position=new[]{position.x,position.y,position.z},SourceRange=plan?.SourceRange,DetectionRange=plan!=null?DetectionRange(plan.SourceRange):(float?)null,
            TravelRange=range,RemainingRange=remaining,Distance=range-remaining,TargetDistance=targetDistance,EligibleIds=eligible?.Select(e=>e.id).ToArray()??Array.Empty<uint>(),
            Destination=destination.HasValue?new[]{destination.Value.x,destination.Value.y,destination.Value.z}:null,Age=age,TargetAlive=targetAlive,AppliesDamage=appliesDamage,Outcome=outcome,VisualActive=visualActive,VisualPosition=visualPosition,
            ArrivalProjectilePosition=kind=="arrival-impact"?new[]{position.x,position.y,position.z}:null});
    internal static string LastVolleyJson=>JsonSerializer.Serialize(lastVolley);
    internal static string EventsJson=>JsonSerializer.Serialize(events.ToArray());
    internal static string SnapshotJson=>JsonSerializer.Serialize(new{DetectionMultiplier,LastVolley=lastVolley,RecentEvents=events.ToArray(),LiveProjectiles=AbilityRuntime.Projectiles.Select(p=>p.Snapshot()).ToArray()});
    internal static void Reset(){lastVolley=null;events.Clear();nextVolley=nextProjectile=0;}
}

internal sealed class VolleyPlan
{
    internal readonly int VolleyId,CastToken;
    internal readonly string Spell;
    internal readonly float SourceRange;
    internal readonly Vector3 Origin;
    internal readonly List<Enemy> Assigned=new();
    readonly Dictionary<uint,int> claims=new();
    internal VolleyPlan(int volley,int castToken,string spell,float range,Vector3 origin,List<Enemy> eligible,int quantity)
    {
        VolleyId=volley;CastToken=castToken;Spell=spell;SourceRange=range;Origin=origin;
        if(eligible.Count>0)for(int i=0;i<quantity;i++)Assigned.Add(eligible[i%eligible.Count]);
    }
    internal void Claim(uint enemy)=>claims[enemy]=claims.TryGetValue(enemy,out int count)?count+1:1;
    internal void Release(uint enemy){if(!claims.TryGetValue(enemy,out int count))return;if(count<=1)claims.Remove(enemy);else claims[enemy]=count-1;}
    internal Enemy Replacement(List<Enemy> candidates)=>candidates.FirstOrDefault(e=>!claims.ContainsKey(e.id))??candidates.FirstOrDefault();
}

public static class TargetAcquisitionDiagnostics
{
    public static string PolicyJson=>JsonSerializer.Serialize(new{Origin="user-requested arena adaptation",DetectionMultiplier=TargetAcquisition.DetectionMultiplier,
        VolleyOrder="nearest distance, then stable native enemy ID; distinct before round-robin",DeadTarget="nearest unclaimed per volley, then nearest available; if none, last valid world destination without dead-target damage; remaining travel range only",Arrival="move visual to destination before damage and impact; Size does not enlarge arrival distance",
        GroundArea="initial target acquisition +20%; damage radius unchanged",Totems="activation scan +20%; damage/attack radius unchanged",
        Chain="initial acquisition +20%; hop radius unchanged",SelfArea="activation scan +20%; hit radius unchanged",Pets="pet AI acquisition unchanged",SourceAutoWeapons="unchanged"});
    public static string LastVolleyJson=>TargetAcquisition.LastVolleyJson;
    public static string RecentEventsJson=>TargetAcquisition.EventsJson;
    public static string SnapshotJson=>TargetAcquisition.SnapshotJson;
    public static string QueryTargetsForProbe(string spellId,Vector3 origin)
    {
        if(!Application.isBatchMode||Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1"||!AbilityRuntime.Active)return "{}";
        var spell=AbilityRuntime.Spells.FirstOrDefault(s=>s.Definition.Id==spellId);if(spell==null)return "{}";
        var targets=TargetAcquisition.SkillTargets(origin,spell.Definition.Range,false);
        return JsonSerializer.Serialize(new{Spell=spellId,SourceRange=spell.Definition.Range,DetectionRange=TargetAcquisition.DetectionRange(spell.Definition.Range),
            DamageRadius=ClassCombat.GenericActive?ClassSpellBehaviors.Radius(spell):spell.Definition.Radius*spell.RadiusFactor*MageSpellBehaviors.RadiusScale(spell)*TomeCompatibility.Size,
            EligibleIds=targets.Select(e=>e.id).ToArray(),Distances=targets.Select(e=>Vector3.Distance(origin,e.transform.position)).ToArray()});
    }
}
