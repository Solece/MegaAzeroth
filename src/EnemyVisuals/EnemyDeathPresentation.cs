using System.Text.Json;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.EnemyVisuals;

// Source enemy.rs marks death at VA145939EE7..939EFC. Its update removes
// dead GUIDs only when now-deathStart >1.2f (VA14593FBC6/FC43..FC5B),
// then sends destroy-object opcode0xAA (VA145942010..20BC). Do not add a
// speculative dissolve, elemental burst, physics body or reward callback.
internal static class EnemyDeathPresentation
{
    internal const float LifetimeSeconds=1.2f;
    // Adapter resource limit; shared meshes/materials are never cloned per death.
    internal const int MaximumVisibleCorpses=512;
    static readonly List<AzerothCorpseVisual> active=new();
    static int created,completed,duplicateSuppressed,evicted,sceneRemoved,failed;
    static uint variationState=0x6d2b79f5;
    static object lastDeath;
    // Preserve original M2 relative frequencies without consuming Megabonk's
    // gameplay RNG. The cross-engine random stream itself is an adapter policy.
    internal static int ChooseVariation(uint enemyId,int total)
    {
        variationState^=enemyId+0x9e3779b9;variationState^=variationState<<13;
        variationState^=variationState>>17;variationState^=variationState<<5;
        return (int)(variationState%(uint)total);
    }
    internal static void Duplicate(){duplicateSuppressed++;}
    internal static void Add(AzerothCorpseVisual corpse)
    {
        active.RemoveAll(c=>c==null);
        while(active.Count>=MaximumVisibleCorpses)
        {
            var oldest=active[0];active.RemoveAt(0);
            if(oldest!=null){evicted++;oldest.Release("adapter-cap");}
        }
        active.Add(corpse);created++;lastDeath=corpse.Snapshot();
    }
    internal static void Removed(AzerothCorpseVisual corpse,string reason,object snapshot)
    {
        active.RemoveAll(c=>c==null || c.Pointer==corpse.Pointer);
        if(reason=="source-lifetime")completed++;
        else if(reason=="scene-destroy")sceneRemoved++;
        lastDeath=new{Reason=reason,Corpse=snapshot};
    }
    internal static void Failed(Exception error)
    {
        failed++;
        if(failed<=5)Plugin.Logger.LogWarning("Source enemy death visual unavailable: "+error);
    }
    internal static string Diagnostics()=>JsonSerializer.Serialize(new{
        SourceLifetimeSeconds=LifetimeSeconds,
        SourceLifetimeEvidence="Azeroth v0.102.3 VA14593FBC6/FC43..FC5B; destroy-object0xAA VA145942010..20BC",
        SourceDeathAnimationId=1,SchoolSpecificDeathVfxVerified=false,
        VariationSelectionPolicy="Original source M2 frequency weights; isolated cross-engine random stream",
        Policy="Original M2 Death animation and existing source material at fixed death position; removed after source1.2s combat clock; no physics/loot/combat mutation.",
        MaximumVisibleCorpses,TotalCreated=created,Completed=completed,Active=active.Count(c=>c!=null),
        DuplicateSuppressed=duplicateSuppressed,Evicted=evicted,SceneRemoved=sceneRemoved,Failed=failed,
        Corpses=active.Where(c=>c!=null).Select(c=>c.Snapshot()).ToArray(),LastDeath=lastDeath
    });
    internal static void Shutdown()
    {
        foreach(var corpse in active.ToArray())if(corpse!=null)corpse.Release("plugin-unload");
        active.Clear();
    }
}

// This entry has a unique native RVA0x4AD060. The parameterless EnemyDied
// tail-calls it; Kill and real damage reach the same authoritative death path.
[HarmonyPatch(typeof(Enemy),nameof(Enemy.EnemyDied),new[]{typeof(DamageContainer)})]
internal static class SourceEnemyDeathPatch
{
    [HarmonyPriority(Priority.Last)]
    static void Prefix(Enemy __instance,DamageContainer __0)
    {
        try{EnemyVisualOwnership.Find(__instance)?.CaptureDeath(__0);}
        catch(Exception error){EnemyDeathPresentation.Failed(error);}
    }
}

public sealed partial class AzerothVisual
{
    bool deathCaptured;
    [HideFromIl2Cpp]
    internal void CaptureDeath(DamageContainer damage)
    {
        if(deathCaptured){EnemyDeathPresentation.Duplicate();return;}
        deathCaptured=true;
        if(enemy==null || model==null || visual==null || replacement==null || !model.IsUsable(false))return;
        var death=model.SelectDeath(enemy.id);
        if(death==null || death.AnimationId!=1 || death.Count<2 || death.Duration<=0)return;
        SyncGhostPalette();
        var root=new GameObject("AzerothCorpse_"+model.Key+"_"+enemy.id);
        try
        {
            root.layer=visual.layer;
            root.transform.position=visual.transform.position;
            root.transform.rotation=visual.transform.rotation;
            root.transform.localScale=visual.transform.lossyScale;
            var corpse=root.AddComponent<AzerothCorpseVisual>();
            corpse.Bind(enemy.id,enemy.Pointer.ToInt64(),model,isBoss,death,groundFound?groundY:nativeFeetY,damage,GhostTint,Mathf.Max(0,EnemyAnimationClock.Now-sourceMaterialBoundAt));
            replacement.enabled=false;
            EnemyDeathPresentation.Add(corpse);
        }
        catch{root.SetActive(false);Object.Destroy(root);throw;}
    }
}

// A detached render-only subject survives immediate native pool deactivation.
// It shares pinned source meshes/material, owns no collider, AI, Rigidbody or HP.
public sealed class AzerothCorpseVisual:MonoBehaviour
{
    public AzerothCorpseVisual(IntPtr pointer):base(pointer){}
    Model model;Clip clip;MeshFilter filter;MeshRenderer renderer;
    uint enemyId;long enemyPointer;bool boss,released;
    float start,age,nextFrame,floorY;int frame=-1;
    Vector3 deathPosition;string damageSource="",element="",releaseReason="";
    float killingDamage;bool killingCrit;
    Color nativeTint;MaterialPropertyBlock[] sourceBlocks;Material[] chosenMaterials;
    MaterialPropertyBlock[] genericSourceBlocks;Material[] genericChosenMaterials;SourceEnvironmentInstance sourceEnvironment;float sourceAgeAtDeath;
    [HideFromIl2Cpp]
    internal void Bind(uint id,long pointer,Model source,bool isBoss,Clip death,float floor,DamageContainer damage,Color ghostTint,float sourceGlobalAge=0)
    {
        enemyId=id;enemyPointer=pointer;model=source;boss=isBoss;clip=death;floorY=floor;
        deathPosition=transform.position;start=EnemyAnimationClock.Now;
        damageSource=damage?.damageSource??"";element=damage!=null?damage.element.ToString():"";
        killingDamage=damage?.damage??0;killingCrit=damage?.crit??false;
        filter=gameObject.AddComponent<MeshFilter>();renderer=gameObject.AddComponent<MeshRenderer>();
        nativeTint=ghostTint;sourceAgeAtDeath=sourceGlobalAge;renderer.sharedMaterials=model.RenderMaterials;ApplyFrame(0);
    }
    [HideFromIl2Cpp]
    void ApplyFrame(int value)
    {
        if(frame==value && filter.sharedMesh!=null)return;
        var mesh=clip.Meshes[value];if(mesh==null)throw new InvalidOperationException("Missing original death frame "+model.Key+"/"+value);
        frame=value;filter.sharedMesh=mesh;transform.position=deathPosition;
        model.ApplyGhostColors(renderer,clip.Name,frame,nativeTint,ref sourceBlocks,ref chosenMaterials);
        model.ApplySourceColors(renderer,clip.Name,frame,sourceAgeAtDeath+age,ref genericSourceBlocks,ref genericChosenMaterials);
        // Same native-floor presentation guard as the living source body.
        // Source pose/rotation/scale remain unchanged; only penetration is raised.
        float lift=Mathf.Max(0,floorY-renderer.bounds.min.y);
        if(lift>0)transform.position+=Vector3.up*lift;
        if(model.SourceProfile!=null){sourceEnvironment??=new SourceEnvironmentInstance();filter.sharedMesh=sourceEnvironment.Apply(model,mesh,transform);}
    }
    void LateUpdate()
    {
        if(released || model==null || clip==null)return;
        try
        {
            float now=EnemyAnimationClock.Now;age=Mathf.Max(0,now-start);
            if(age>EnemyDeathPresentation.LifetimeSeconds){Release("source-lifetime");return;}
            if(!EnemyAnimationClock.CanAnimate || now<nextFrame)return;
            nextFrame=now+1f/24;
            int value=Math.Clamp((int)(Mathf.Clamp01(age/clip.Duration)*(clip.Count-1)),0,clip.Count-1);
            ApplyFrame(value);
        }
        catch(Exception error){EnemyDeathPresentation.Failed(error);Release("asset-failure");}
    }
    [HideFromIl2Cpp]
    internal object Snapshot()=>new{
        EnemyId=enemyId,EnemyPointer=enemyPointer,ModelKey=model?.Key,IsBoss=boss,
        Age=age,ClipDuration=clip?.Duration??0,Frame=frame,FrameCount=clip?.Count??0,
        NormalizedTime=clip==null?0:Mathf.Clamp01(age/clip.Duration),
        LifetimeSeconds=EnemyDeathPresentation.LifetimeSeconds,
        SourceDeathAnimationId=clip?.AnimationId??-1,SourceSequenceIndex=clip?.SequenceIndex??-1,
        SourceFrequency=clip?.SourceFrequency??0,SourceVariantCount=model?.DeathVariantCount??0,DeathProfileLoaded=model?.DeathProfileLoaded??false,
        Visible=!released && renderer!=null && renderer.enabled && gameObject.activeInHierarchy,
        AssetsValid=model!=null && model.IsUsable(false) && filter!=null && filter.sharedMesh!=null,
        Position=new[]{transform.position.x,transform.position.y,transform.position.z},
        Scale=new[]{transform.lossyScale.x,transform.lossyScale.y,transform.lossyScale.z},
        HasCollider=GetComponent<Collider>()!=null,HasRigidbody=GetComponent<Rigidbody>()!=null,
        NativeActorChild=transform.parent!=null,DamageSource=damageSource,Element=element,
        KillingDamage=killingDamage,KillingCrit=killingCrit,ReleaseReason=releaseReason
        ,SourceDisplayId=model?.SourceProfile?.SourceDisplayId??0,SourceMaterials=model?.SourceMaterialSnapshot(renderer,filter?.sharedMesh,clip?.Name,frame,sourceAgeAtDeath+age)
    };
    [HideFromIl2Cpp]
    internal void Release(string reason)
    {
        if(released)return;releaseReason=reason;
        var snapshot=Snapshot();released=true;gameObject.SetActive(false);
        sourceEnvironment?.Release();
        EnemyDeathPresentation.Removed(this,reason,snapshot);Object.Destroy(gameObject);
    }
    void OnDestroy()
    {
        sourceEnvironment?.Release();
        if(released)return;released=true;
        EnemyDeathPresentation.Removed(this,"scene-destroy",new{EnemyId=enemyId,ModelKey=model?.Key,Age=age});
    }
}

public static partial class EnemyVisualBridge
{
    public static string DeathDiagnostics()=>EnemyDeathPresentation.Diagnostics();
}
