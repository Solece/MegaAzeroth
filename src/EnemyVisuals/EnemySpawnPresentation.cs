using System.Text.Json;
using HarmonyLib;
using Actors.Enemies;
using Assets.Scripts.Actors.Enemies;
using UnityEngine;

namespace AzerothMegabonk.EnemyVisuals;

// Megabonk already owns spawn timing, pooled particles and spawn audio. Its
// teleport coroutine animates only the hidden native renderer, so mirror that
// renderer's below-ground displacement onto the replacement body.
public sealed partial class AzerothVisual
{
    private float nativeSpawnOffsetY;
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    void ApplyNativeSpawnOffset(float feetY)
    {
        nativeSpawnOffsetY=0;
        if(enemy==null || original==null || visual==null || enemy.state!=EEnemyState.Teleporting)return;
        float nativeMin=original.bounds.min.y;
        if(!float.IsFinite(nativeMin))return;
        nativeSpawnOffsetY=Math.Min(0,nativeMin-feetY)*sizeMultiplier;
        visual.transform.position+=Vector3.up*nativeSpawnOffsetY;
    }
}

internal static class NativeSpawnEvidence
{
    static readonly Queue<object> Recent=new();
    internal static int Count;
    internal static void Observe(EnemySpawnParticles fx,Enemy enemy)
    {
        Count++;
        var ps=fx!=null?fx.ps:null;
        Recent.Enqueue(new{enemyId=enemy!=null?enemy.id:0,enemyName=enemy?.enemyData?.enemyName.ToString(),time=Time.unscaledTime,
            state=enemy!=null?enemy.state.ToString():"",effectName=fx!=null?fx.gameObject.name:"",effectInstance=fx!=null?fx.gameObject.GetInstanceID():0,
            active=fx!=null && fx.gameObject.activeInHierarchy,playing=ps!=null && ps.isPlaying,
            particleCount=ps!=null?ps.particleCount:0,nativeAudioPresent=fx!=null && fx.audio!=null,
            feet=enemy!=null?new[]{enemy.GetFeetPosition().x,enemy.GetFeetPosition().y,enemy.GetFeetPosition().z}:Array.Empty<float>()});
        while(Recent.Count>32)Recent.Dequeue();
    }
    internal static string Describe()=>JsonSerializer.Serialize(new{nativeSpawnParticleCalls=Count,recent=Recent.ToArray()});
}
[HarmonyPatch(typeof(EnemySpawnParticles),nameof(EnemySpawnParticles.Set))]
internal static class NativeSpawnParticleEvidencePatch
{
    static void Postfix(EnemySpawnParticles __instance,Enemy enemy)
    {
        try{NativeSpawnEvidence.Observe(__instance,enemy);}catch(Exception ex){Plugin.Logger.LogWarning("Native spawn evidence unavailable: "+ex.Message);}
    }
}
public static partial class EnemyVisualBridge
{
    public static string SpawnEffectsDiagnostics()=>NativeSpawnEvidence.Describe();
}
