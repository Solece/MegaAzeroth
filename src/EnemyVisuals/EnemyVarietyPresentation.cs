using System.Text.Json;
using Assets.Scripts.Actors.Enemies;
using BepInEx;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.EnemyVisuals;

internal static class EnemyVarietySelection
{
    internal static readonly Dictionary<string,int> DisplayIds=new(){["ghoul"]=137,["zombie"]=1200,["scorpion"]=2485,["silithidwasp"]=92,["ent"]=864,["fleshgolem"]=1061,["doomguard"]=433,["dragonspawncaster"]=6761};
    // Exact native enum IDs from the installed dump. No random call, stat,
    // collider, transform or spawner mutation participates in appearance choice.
    internal static string Resolve(Enemy enemy,bool boss)
    {
        if(enemy?.enemyData==null||EndlessGhostPresentation.Matches(enemy))return null;
        int id=(int)enemy.enemyData.enemyName;
        if(boss)return id switch{7=>"doomguard",35 or 36=>"dragonspawncaster",_=>null};
        return id switch{6=>"ghoul",41=>"zombie",26 or 30=>"scorpion",24=>"silithidwasp",19 or 20 or 21=>"ent",31=>"fleshgolem",_=>null};
    }
    internal static string Catalog()
    {
        string file=Path.Combine(Paths.GameRootPath,"AzerothAssets","enemy-variety-assets.json");
        return File.Exists(file)?File.ReadAllText(file):"{\"format\":\"azeroth-enemy-variety-assets-v1\",\"error\":\"Source variety manifest missing\",\"models\":[]}";
    }
}

public sealed partial class AzerothVisual
{
    MaterialPropertyBlock[] sourceMaterialBlocks;Material[] sourceChosenMaterials;
    SourceEnvironmentInstance sourceEnvironment;
    float sourceMaterialBoundAt;
    [HideFromIl2Cpp]
    void ResetSourcePresentation()
    {
        sourceEnvironment?.Release();sourceEnvironment=null;sourceMaterialBlocks=null;sourceChosenMaterials=null;sourceMaterialBoundAt=0;
    }
    [HideFromIl2Cpp]
    void SyncSourceMaterials()
    {
        if(model?.SourceProfile==null||replacement==null)return;
        model.ApplySourceColors(replacement,clipName,lastFrame,Mathf.Max(0,EnemyAnimationClock.Now-sourceMaterialBoundAt),ref sourceMaterialBlocks,ref sourceChosenMaterials);
    }
    [HideFromIl2Cpp]
    void SetSourceRenderFrame(Mesh mesh)
    {
        if(model?.SourceProfile==null||mesh==null||filter==null||visual==null)return;
        sourceEnvironment??=new SourceEnvironmentInstance();filter.sharedMesh=sourceEnvironment.Apply(model,mesh,visual.transform);SyncSourceMaterials();
    }
    [HideFromIl2Cpp]
    internal object VarietySnapshot()
    {
        bool boss=enemy!=null&&(enemy.IsBoss()||enemy.IsStageBoss()||enemy.IsFinalBoss());
        var mesh=filter?.sharedMesh;
        int frames=model!=null&&model.Clips.TryGetValue(clipName,out var c)?c.Count:0;
        return new{NativeId=enemy?.enemyData!=null?(int)enemy.enemyData.enemyName:-1,NativeEnemyName=enemy?.enemyData?.enemyName.ToString(),EnemyId=enemy!=null?enemy.id:0,
            ExpectedKey=EnemyVarietySelection.Resolve(enemy,boss),ActualKey=model?.Key,IsBoss=boss,SourceDisplayId=model?.SourceProfile?.SourceDisplayId??0,SourceSha256=model?.SourceProfile?.SourceSha256,
            SourceMaterialProfileLoaded=model?.SourceProfile!=null,SourceVertexMap=model?.SourceProfile?.SourceVertexMap??Array.Empty<int>(),
            CurrentClip=clipName,CurrentFrame=lastFrame,FrameCount=frames,DeathProfileLoaded=model?.DeathProfileLoaded??false,DeathVariantCount=model?.DeathVariantCount??0,
            MeshRenderers=visual!=null?visual.GetComponentsInChildren<MeshRenderer>(true).Length:0,SubMeshCount=mesh!=null?mesh.subMeshCount:0,
            AssetsValid=model!=null&&model.IsUsable(false),SourceMaterials=model?.SourceMaterialSnapshot(replacement,mesh,clipName,lastFrame,Mathf.Max(0,EnemyAnimationClock.Now-sourceMaterialBoundAt)),
            NativeScaleEvidence=JsonSerializer.Deserialize<JsonElement>(Describe())};
    }
}

public static partial class EnemyVisualBridge
{
    public static string VarietyCatalogJson=>EnemyVarietySelection.Catalog();
    public static string VarietyDiagnostics(Enemy enemy)
    {
        var visual=enemy!=null?EnemyVisualOwnership.Find(enemy):null;
        return visual!=null?JsonSerializer.Serialize(visual.VarietySnapshot()):"{\"SourceMaterialProfileLoaded\":false,\"attached\":false}";
    }
    public static string[] AllVarietyDiagnostics()=>Object.FindObjectsOfType<AzerothVisual>(true).Select(v=>JsonSerializer.Serialize(v.VarietySnapshot())).ToArray();
}
