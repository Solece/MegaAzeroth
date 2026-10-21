using System.Text.Json;
using Assets.Scripts.Actors.Enemies;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.EnemyVisuals;

// A pooled native Enemy owns exactly one replacement controller. Wrapper identity
// is insufficient across IL2CPP calls; the native actor pointer is the key.
internal static class EnemyVisualOwnership
{
    static readonly Dictionary<long,AzerothVisual> Owners=new();
    internal static AzerothVisual Find(Enemy enemy)
    {
        if(enemy==null)return null;
        long key=enemy.Pointer.ToInt64();
        if(Owners.TryGetValue(key,out var value) && value!=null)return value;
        Owners.Remove(key);return null;
    }
    internal static void Register(Enemy enemy,AzerothVisual visual)
    {
        var previous=Find(enemy);
        if(previous!=null && previous.Pointer!=visual.Pointer)previous.RetireForReplacement();
        Owners[enemy.Pointer.ToInt64()]=visual;
    }
    internal static void Remove(Enemy enemy,AzerothVisual visual)
    {
        if(enemy==null)return;
        long key=enemy.Pointer.ToInt64();
        if(Owners.TryGetValue(key,out var owner) && owner!=null && owner.Pointer==visual.Pointer)Owners.Remove(key);
    }
    internal static Enemy NearestEnemy(Transform transform)
    {
        for(var node=transform;node!=null;node=node.parent)
        {
            var actor=node.GetComponent<Enemy>();if(actor!=null)return actor;
        }
        return null;
    }
    internal static bool Owned(Enemy enemy,Transform transform)
    {
        var owner=NearestEnemy(transform);
        return owner!=null && enemy!=null && owner.Pointer==enemy.Pointer;
    }
    internal static IEnumerable<Transform> Nodes(Transform root)
    {
        var pending=new Stack<Transform>();pending.Push(root);
        while(pending.Count>0)
        {
            var node=pending.Pop();if(node==null)continue;yield return node;
            for(int i=node.childCount-1;i>=0;i--)pending.Push(node.GetChild(i));
        }
    }
    internal static bool ReplacementNode(Transform node)
    {
        for(var t=node;t!=null;t=t.parent)
            if(t.name.StartsWith("AzerothCreature_",StringComparison.Ordinal))return true;
        return false;
    }
    internal static bool BodyRenderer(Renderer renderer)
        => renderer!=null && (renderer.TryCast<MeshRenderer>()!=null || renderer.TryCast<SkinnedMeshRenderer>()!=null);
    internal static string Path(Transform node,Transform root)
    {
        var parts=new List<string>();
        for(var t=node;t!=null;t=t.parent){parts.Add(t.name);if(t==root)break;}
        parts.Reverse();return string.Join("/",parts);
    }
}

public sealed partial class AzerothVisual
{
    sealed class NativeBody
    {
        internal Renderer Renderer;
        internal bool WasEnabled;
        internal string Path;
    }
    readonly Dictionary<int,NativeBody> nativeBodies=new();
    readonly HashSet<int> nativeBodyMeshIds=new();
    int duplicateRootsRemoved;
    float nextOwnershipScan;
    bool ownershipRegistered;

    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    void RegisterOwnership(Enemy target)
    {
        RestoreNativeBodies();nativeBodies.Clear();nativeBodyMeshIds.Clear();
        if(enemy!=null && target.Pointer!=enemy.Pointer)EnemyVisualOwnership.Remove(enemy,this);
        EnemyVisualOwnership.Register(target,this);ownershipRegistered=true;nextOwnershipScan=0;
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    internal void RetireForReplacement()
    {
        enabled=false;ReleaseOwnership();
        if(visual!=null){visual.SetActive(false);Object.Destroy(visual);}
        visual=null;replacement=null;filter=null;enemy=null;original=null;model=null;
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    void CaptureNativeBodies()
    {
        if(enemy==null)return;
        if(nativeBodyMeshIds.Count==0 && enemy.enemyData?.animation!=null)
            foreach(var sequence in enemy.enemyData.animation.Animations)
                foreach(var mesh in sequence.Meshes)if(mesh!=null)nativeBodyMeshIds.Add(mesh.GetInstanceID());
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] void Add(Renderer renderer)
        {
            if(renderer==null || EnemyVisualOwnership.ReplacementNode(renderer.transform))return;
            if(renderer!=original && !EnemyVisualOwnership.Owned(enemy,renderer.transform))return;
            if(!IsNativeBody(renderer))return;
            int id=renderer.GetInstanceID();
            if(!nativeBodies.ContainsKey(id))nativeBodies[id]=new NativeBody{Renderer=renderer,WasEnabled=renderer.enabled,Path=EnemyVisualOwnership.Path(renderer.transform,enemy.transform)};
        }
        Add(original);
        if(enemy.enemyRenderer!=null)Add(enemy.enemyRenderer.enemyRenderer);
        if(enemy.animatedMesh!=null)Add(enemy.animatedMesh.GetComponent<Renderer>());
        foreach(var node in EnemyVisualOwnership.Nodes(enemy.transform))
        {
            if(!EnemyVisualOwnership.Owned(enemy,node) || EnemyVisualOwnership.ReplacementNode(node))continue;
            Add(node.GetComponent<MeshRenderer>());Add(node.GetComponent<SkinnedMeshRenderer>());
        }
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    bool IsNativeBody(Renderer renderer)
    {
        if(renderer==null || !EnemyVisualOwnership.BodyRenderer(renderer))return false;
        if(renderer==original || renderer==enemy?.enemyRenderer?.enemyRenderer || renderer==enemy?.dissolve?.enemyRenderer)return true;
        // Mesh effects/rings must survive. A second native body is identified by
        // the enemy's actual animation meshes, not merely by renderer type/name.
        var filter=renderer.GetComponent<MeshFilter>();
        var skin=renderer.TryCast<SkinnedMeshRenderer>();
        var mesh=filter!=null?filter.sharedMesh:skin!=null?skin.sharedMesh:null;
        return mesh!=null && nativeBodyMeshIds.Contains(mesh.GetInstanceID());
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    void RemoveDuplicateRoots()
    {
        if(enemy==null)return;
        var duplicates=EnemyVisualOwnership.Nodes(enemy.transform)
            .Where([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (t)=>t!=null && t.gameObject!=visual && t.name.StartsWith("AzerothCreature_",StringComparison.Ordinal) && EnemyVisualOwnership.Owned(enemy,t))
            .Select([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (t)=>t.gameObject).ToArray();
        foreach(var extra in duplicates)
        {
            // Destroy is deferred until the end of the frame; hide immediately.
            if(extra.activeSelf){extra.SetActive(false);duplicateRootsRemoved++;}
            Object.Destroy(extra);
        }
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    void SuppressNativeBodies(bool forceScan=false)
    {
        if(enemy==null || !ownershipRegistered)return;
        if(EnemyVisualOwnership.Find(enemy)?.Pointer!=Pointer){RetireForReplacement();return;}
        if(forceScan || Time.unscaledTime>=nextOwnershipScan)
        {
            nextOwnershipScan=Time.unscaledTime+.25f;RemoveDuplicateRoots();CaptureNativeBodies();
        }
        foreach(var body in nativeBodies.Values)if(body.Renderer!=null)body.Renderer.enabled=false;
        if(original!=null)original.enabled=false;
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    void RestoreNativeBodies()
    {
        foreach(var body in nativeBodies.Values)if(body.Renderer!=null)body.Renderer.enabled=body.WasEnabled;
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    void ReleaseOwnership()
    {
        if(!ownershipRegistered)return;
        if(enemy!=null && EnemyVisualOwnership.Find(enemy)?.Pointer==Pointer)
        {
            RestoreNativeBodies();if(original!=null)original.enabled=true;
            EnemyVisualOwnership.Remove(enemy,this);
        }
        nativeBodies.Clear();ownershipRegistered=false;
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    object OwnershipSnapshot()
    {
        var roots=enemy==null?Array.Empty<Transform>():EnemyVisualOwnership.Nodes(enemy.transform)
            .Where([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (t)=>t.name.StartsWith("AzerothCreature_",StringComparison.Ordinal) && EnemyVisualOwnership.Owned(enemy,t)).ToArray();
        var bodyStates=nativeBodies.Values.Where([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (b)=>b.Renderer!=null).Select([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (b)=>new{
            path=b.Path,instanceId=b.Renderer.GetInstanceID(),type=b.Renderer.GetIl2CppType().Name,
            initiallyEnabled=b.WasEnabled,enabled=b.Renderer.enabled,active=b.Renderer.gameObject.activeInHierarchy,
            visible=b.Renderer.enabled && b.Renderer.gameObject.activeInHierarchy && !b.Renderer.forceRenderingOff
        }).ToArray();
        int activeRoots=roots.Count([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (t)=>t.gameObject.activeInHierarchy);
        int sourceRenderers=roots.Where([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (t)=>t.gameObject.activeInHierarchy).Sum([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (t)=>EnemyVisualOwnership.Nodes(t).Count([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (n)=>{
            var r=n.GetComponent<MeshRenderer>();return r!=null && r.enabled && r.gameObject.activeInHierarchy && EnemyVisualOwnership.Owned(enemy,n);
        }));
        return new{
            enemyName=enemy?.enemyData?.enemyName.ToString(),enemyId=enemy!=null?enemy.id:0,
            ownerPointer=enemy!=null?enemy.Pointer.ToInt64():0,controllerPointer=Pointer.ToInt64(),
            controllerOwnsActor=enemy!=null && EnemyVisualOwnership.Find(enemy)?.Pointer==Pointer,
            activeReplacementRoots=activeRoots,visibleSourceBodyRenderers=sourceRenderers,
            visibleNativeBodyRenderers=bodyStates.Count([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (b)=>b.visible),trackedNativeBodyRenderers=bodyStates.Length,
            oneSourceAndNoNative=activeRoots==1 && sourceRenderers==1 && bodyStates.All([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (b)=>!b.visible),
            duplicateRootsRemoved,nativeBodies=bodyStates,
            preservedMeshEffects=enemy==null?Array.Empty<string>():EnemyVisualOwnership.Nodes(enemy.transform).Where([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (t)=>EnemyVisualOwnership.Owned(enemy,t) && !EnemyVisualOwnership.ReplacementNode(t)).Select([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (t)=>t.GetComponent<MeshRenderer>()).Where([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (r)=>r!=null && !IsNativeBody(r)).Select([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (r)=>EnemyVisualOwnership.Path(r.transform,enemy.transform)).ToArray(),
            sourceRoots=roots.Select([Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] (t)=>new{path=EnemyVisualOwnership.Path(t,enemy.transform),active=t.gameObject.activeInHierarchy,instanceId=t.gameObject.GetInstanceID()}).ToArray()
        };
    }
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    public string DescribeOwnership()=>JsonSerializer.Serialize(OwnershipSnapshot());

    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    internal string RunOwnershipRegression(Enemy foreign)
    {
        var target=enemy;var data=model;bool boss=isBoss;
        var foreignVisual=EnemyVisualOwnership.Find(foreign);
        var foreignRenderer=foreignVisual?.replacement;
        bool foreignWasVisible=foreignRenderer!=null && foreignRenderer.enabled;
        float hp=target.hp;var position=target.transform.position;
        var collider=target.collider;var colliderPointer=collider!=null?collider.Pointer:IntPtr.Zero;
        var colliderHeight=collider!=null?collider.height:0;var radius=collider!=null?collider.radius:0;
        GameObject extraNative=null,duplicate=null,meshEffect=null;
        int checks=0;
        [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp] void Check(bool value,string label){if(!value)throw new InvalidOperationException("Enemy ownership regression: "+label);checks++;}
        try
        {
            CaptureNativeBodies();int before=nativeBodies.Count;
            extraNative=new GameObject("AzerothProbe_NativeGolemBody");extraNative.transform.SetParent(target.transform,false);
            extraNative.AddComponent<MeshFilter>().sharedMesh=original.GetComponent<MeshFilter>()?.sharedMesh ?? data.Template;
            extraNative.AddComponent<MeshRenderer>().sharedMaterial=data.Material;
            duplicate=new GameObject("AzerothCreature_DuplicateProbe");duplicate.transform.SetParent(target.transform,false);
            duplicate.AddComponent<MeshFilter>().sharedMesh=data.Template;
            duplicate.AddComponent<MeshRenderer>().sharedMaterial=data.Material;
            meshEffect=new GameObject("AzerothProbe_SpawnRingMesh");meshEffect.transform.SetParent(target.transform,false);
            meshEffect.AddComponent<MeshFilter>().sharedMesh=data.Template;
            meshEffect.AddComponent<MeshRenderer>().sharedMaterial=data.Material;
            SuppressNativeBodies(true);
            Check(meshEffect.GetComponent<MeshRenderer>().enabled,"native mesh effect preserved");
            Check(!extraNative.GetComponent<MeshRenderer>().enabled,"native extra body stays hidden");
            Check(!duplicate.activeSelf,"stale replacement hidden before deferred destruction");
            Check(nativeBodies.Count>=before+1,"extra native body has an ownership entry");
            for(int i=0;i<4;i++)
            {
                Bind(target,data,boss);SuppressNativeBodies(true);
                using var state=JsonDocument.Parse(DescribeOwnership());
                Check(state.RootElement.GetProperty("oneSourceAndNoNative").GetBoolean(),"repeat bind "+i);
            }
            Check(meshEffect.GetComponent<MeshRenderer>().enabled,"native mesh effect preserved after rebind");
            Check(target.hp==hp,"native hp unchanged");Check(target.transform.position==position,"native position unchanged");
            Check(target.collider!=null && target.collider.Pointer==colliderPointer && target.collider.height==colliderHeight && target.collider.radius==radius,"native collider unchanged");
            Check(foreignRenderer==null || foreignRenderer.enabled==foreignWasVisible,"other enemy renderer unchanged");
            return JsonSerializer.Serialize(new{pass=true,checks,ownership=OwnershipSnapshot(),foreignEnemyId=foreign!=null?foreign.id:0});
        }
        finally
        {
            if(meshEffect!=null){meshEffect.SetActive(false);Object.Destroy(meshEffect);}
            if(extraNative!=null){extraNative.SetActive(false);Object.Destroy(extraNative);}
            if(duplicate!=null){duplicate.SetActive(false);Object.Destroy(duplicate);}
        }
    }
}

public static partial class EnemyVisualBridge
{
    public static string DescribeOwnership(Enemy enemy)
        => EnemyVisualOwnership.Find(enemy)?.DescribeOwnership() ?? "{\"attached\":false}";
    public static string RunOwnershipRegressionForProbe(Enemy enemy,Enemy otherEnemy)
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return "{\"allowed\":false}";
        var visual=EnemyVisualOwnership.Find(enemy);
        if(visual==null)throw new InvalidOperationException("No source enemy visual for ownership regression");
        return visual.RunOwnershipRegression(otherEnemy);
    }
}
