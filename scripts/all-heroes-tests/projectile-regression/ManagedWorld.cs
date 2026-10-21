using Assets.Scripts.Actors.Enemies;
using UnityEngine;

// Managed stand-ins isolate actual SpellProjectile/VolleyPlan code from Unity
// and IL2CPP. These fixtures are not evidence of native rendering or gameplay.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up=>new(0,1,0);
        public float sqrMagnitude=>x*x+y*y+z*z;
        public float magnitude=>MathF.Sqrt(sqrMagnitude);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
        public static Vector3 MoveTowards(Vector3 from,Vector3 to,float max)
        {var d=to-from;float length=d.magnitude;return length<=max||length==0?to:from+d*(max/length);}
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Mathf.Clamp01(t);
    }
    public struct Quaternion {public static Quaternion LookRotation(Vector3 at)=>default;}
    public static class Mathf
    {
        public static float Max(float a,float b)=>MathF.Max(a,b);
        public static float Min(float a,float b)=>MathF.Min(a,b);
        public static float Clamp01(float a)=>Math.Clamp(a,0,1);
    }
    public sealed class Transform {public Vector3 position;public Quaternion rotation;}
    public sealed class GameObject {public readonly Transform transform=new();public bool activeInHierarchy=true;}
    public struct Bounds {public Vector3 center;}
    public sealed class Collider {public Bounds bounds;}
    public static class Application {public static bool isBatchMode=true;}
}
namespace Assets.Scripts.Actors.Enemies
{
    public sealed class Enemy
    {
        public uint id;
        public bool Dead;
        public float hp=100;
        public readonly Transform transform=new();
        public Collider collider;
        public bool IsDeadOrDyingNextFrame()=>Dead||hp<=0;
    }
}
namespace AzerothAbilities
{
    internal sealed class SpellDefinition
    {public string Id="fireball",Kind="projectile";public float ProjectileSpeed=2,Range=30,Radius=3;}
    internal sealed class LearnedSpell
    {public readonly SpellDefinition Definition;public float RadiusFactor=1;public LearnedSpell(SpellDefinition d){Definition=d;}}
    internal static class CombatSimulation {public static float Time;public static bool CanSimulate=true;}
    internal sealed class BoolSetting {public bool Value=true;}
    internal sealed class Plugin {public static Plugin Current=new();public readonly BoolSetting SpellVisuals=new();}
    internal static class AbilityBehaviour {public static bool RenderVisuals=true;}
    internal static class TomeCompatibility
    {public static float ProjectileSpeed=1,Duration=1,Size=1;public static int ExtraBounces;}
    internal static class TomeCompatibilityDiagnostics
    {
        public static float CastProjectileSpeedOverride;
        public static int Created,Hits,Bounces,Visuals;
        public static void ProjectileCreated()=>Created++;
        public static void ProjectileHit()=>Hits++;
        public static void ProjectileBounced()=>Bounces++;
        public static void ProjectileVisualCreated()=>Visuals++;
    }
    internal static class SpellVfx
    {
        public static readonly List<GameObject> Live=new();
        public static readonly List<Vector3> Impacts=new();
        public static int Released;
        public static GameObject Projectile(string id,Vector3 at,float size)
        {var go=new GameObject();go.transform.position=at;Live.Add(go);return go;}
        public static void Play(string id,string stage,Vector3 at,float scale,float duration)
        {if(stage=="impact")Impacts.Add(at);}
        public static void Release(GameObject go){Released++;if(go!=null)go.activeInHierarchy=false;}
    }
    internal static class ClassCombat {public static bool GenericActive;}
    internal static class ClassSpellBehaviors
    {
        public static readonly HashSet<uint> Dots=new();
        public static bool HasDot(Enemy enemy,string id)=>Dots.Contains(enemy.id);
        public static void ApplySpellDot(LearnedSpell spell,Enemy target,float damage,int token)
        {Program.CheckArrivalBeforeDamage(target,"dot");Dots.Add(target.id);}
        public static float Radius(LearnedSpell spell)=>spell.Definition.Radius;
    }
    internal static class MageSpellBehaviors {public static float RadiusScale(LearnedSpell spell)=>1;}
    internal static class AbilityRuntime
    {
        public static bool Active=true;
        public static int CurrentCastSerial;
        public static float OverrideCastRange;
        public static readonly List<Enemy> World=new();
        public static readonly List<LearnedSpell> Spells=new();
        public static readonly List<SpellProjectile> Projectiles=new();
        public static readonly List<uint> HitTargets=new();
        public static List<Enemy> Targets(Vector3 origin,float range,bool priority)
            =>World.Where(e=>!e.IsDeadOrDyingNextFrame()&&Vector3.Distance(origin,e.transform.position)<=range)
                .OrderBy(e=>Vector3.Distance(origin,e.transform.position)).ThenBy(e=>e.id).ToList();
        public static void Hit(LearnedSpell spell,Enemy target,float damage,Vector3 from,int castToken)
        {
            Program.CheckArrivalBeforeDamage(target,"direct");
            HitTargets.Add(target.id);target.hp-=damage;
        }
    }
}
