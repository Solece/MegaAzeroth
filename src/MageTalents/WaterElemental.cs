using System.Reflection;
using System.Text.Json;
using AzerothAbilities;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Menu.Shop;
using BepInEx;
using UnityEngine;

namespace MageTalents;

public static class WaterElemental
{
    static Type bridge;
    static MethodInfo create,setPosition,setFacing,setClip,destroy;
    static GameObject visual;
    static Vector3 position,followTarget;
    static float nextAttack,castingUntil,visualHeight,nextFollow;
    static bool travelling;
    public static int ShotCount {get;private set;}
    public static float LastDamage {get;private set;}
    public static bool Active=>visual!=null;
    static void Bridge()
    {
        if(bridge!=null)return;
        foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {bridge=assembly.GetType("AzerothMegabonk.MageVisuals.CreatureVisualBridge");if(bridge!=null)break;}
        if(bridge==null)return;create=bridge.GetMethod("Create");setPosition=bridge.GetMethod("SetPosition");setFacing=bridge.GetMethod("SetFacing");setClip=bridge.GetMethod("SetClip");destroy=bridge.GetMethod("Destroy");
    }
    static float Height(string key)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(Paths.GameRootPath,"AzerothMageAssets",key,"model.json")));
        return doc.RootElement.GetProperty("boundsMax")[1].GetSingle()-doc.RootElement.GetProperty("boundsMin")[1].GetSingle();
    }
    static float TargetHeight(MyPlayer p)
    {
        var capsule=p.playerMovement?.GetComponent<CapsuleCollider>();
        var worldHeight=capsule==null?Mathf.Max(.1f,p.height):(capsule.direction==1?capsule.height:capsule.radius*2)*Mathf.Abs(capsule.transform.lossyScale.y);
        return Height("waterelemental")*.55f*worldHeight/Mathf.Max(.01f,Height("human-male"));
    }
    public static void Clear()
    {
        if(visual!=null){if(destroy!=null)destroy.Invoke(null,new object[]{visual});else UnityEngine.Object.Destroy(visual);}
        visual=null;nextAttack=castingUntil=nextFollow=0;travelling=false;ShotCount=0;LastDamage=0;
    }
    public static void Tick(MyPlayer p)
    {
        Bridge();if(create==null)return;
        var orientation=p.playerMovement?.orientation;
        var forward=orientation!=null?orientation.forward:p.transform.forward;forward.y=0;if(forward.sqrMagnitude<.001f)forward=Vector3.forward;forward.Normalize();
        var feet=p.feet!=null?p.feet.position:p.transform.position;
        var desired=feet-forward*2.8f;
        if(visual==null)
        {
            position=desired;visualHeight=TargetHeight(p);visual=(GameObject)create.Invoke(null,new object[]{"waterelemental",position,visualHeight});nextAttack=Time.time;
            Plugin.Current.Log.LogInfo($"Source Water Elemental summoned:model110; sourceScale=.55; hostHeight={visualHeight:F3}; attack50*(1+.12*levelIndex)*globalDamage; interval2.2; range28; projectile116/speed28/slow.3for2sec.");
        }
        if((position-feet).sqrMagnitude>40*40){position=desired;travelling=false;}
        if(Time.time>=nextFollow)
        {
            nextFollow=Time.time+.25f;
            if((position-desired).sqrMagnitude>3.5f*3.5f){followTarget=desired;travelling=true;}
        }
        var previous=position;
        if(travelling)
        {
            var speed=1.15f*Mathf.Max(7,7*Mathf.Max(0,PlayerStats.GetStat(EStat.MoveSpeedMultiplier)));
            position=Vector3.MoveTowards(position,followTarget,speed*Time.deltaTime);if((position-followTarget).sqrMagnitude<.001f)travelling=false;
        }
        setPosition?.Invoke(null,new object[]{visual,position});
        var moving=(position-previous).sqrMagnitude>.0001f;
        if(moving)setFacing?.Invoke(null,new object[]{visual,position-previous});
        if(Time.time>=castingUntil)setClip?.Invoke(null,new object[]{visual,moving?"run":"idle"});
        if(Time.time<nextAttack)return;
        var enemies=AbilityRuntime.Targets(position,28,false);if(enemies.Count==0)return;
        var target=enemies[0];var levelIndex=Math.Max(0,p.inventory.playerXp.level);
        LastDamage=50*(1+.12f*levelIndex)*Mathf.Max(0,PlayerStats.GetStat(EStat.DamageMultiplier))*(1+Plugin.Run.Stat(Plugin.Catalog,17));
        if(!AbilityRuntime.LaunchPetProjectile(target,position+Vector3.up*(visualHeight*.55f),LastDamage))return;
        nextAttack=Time.time+2.2f;ShotCount++;castingUntil=Time.time+1;
        setFacing?.Invoke(null,new object[]{visual,target.transform.position-position});setClip?.Invoke(null,new object[]{visual,"cast"});
    }
}
