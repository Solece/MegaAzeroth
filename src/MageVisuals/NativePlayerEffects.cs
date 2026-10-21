using System.Text.Json;
using UnityEngine;

namespace AzerothMegabonk.MageVisuals;

// Powerup visuals are owned by native status/pickup components, not the replaced
// character skin. Preserve their native activation, duration and renderer state.
internal static class NativePlayerEffects
{
    static readonly List<(Transform Root,string Source)> roots=new();
    internal static void Refresh(Transform body)
    {
        roots.Clear();Collect(body);
    }
    static void Add(Transform root,string source)
    {
        if(root!=null&&!roots.Any(r=>r.Root==root))roots.Add((root,source));
    }
    static void Collect(Transform node)
    {
        if(node==null||node.name.StartsWith("Azeroth",StringComparison.Ordinal))return;
        var shield=node.GetComponent<Shield>();
        if(shield!=null){Add(node,"Shield");Add(shield.renderer?.transform,"Shield.renderer");}
        var effects=node.GetComponent<PlayerShieldEffects>();
        if(effects!=null){Add(effects.shieldChargeFx?.transform,"PlayerShieldEffects.shieldChargeFx");Add(effects.shieldBreakFx?.transform,"PlayerShieldEffects.shieldBreakFx");}
        if(node.GetComponent<ParticleSystem>()!=null)Add(node,"ParticleSystem");
        for(int i=0;i<node.childCount;i++)Collect(node.GetChild(i));
    }
    internal static bool IsProtected(Transform node)
    {
        for(var ancestor=node;ancestor!=null;ancestor=ancestor.parent)
            if(roots.Any(r=>r.Root!=null&&r.Root==ancestor))return true;
        return false;
    }
    internal static string SnapshotJson=>JsonSerializer.Serialize(new{
        roots=roots.Where(r=>r.Root!=null).Select(r=>new{source=r.Source,path=Path(r.Root),active=r.Root.gameObject.activeInHierarchy,
            renderers=r.Root.GetComponentsInChildren<Renderer>(true).Select(v=>new{path=Path(v.transform),v.enabled,active=v.gameObject.activeInHierarchy,v.forceRenderingOff,material=v.sharedMaterial?.name}).ToArray()}).ToArray()
    });
    static string Path(Transform node){var parts=new List<string>();for(var p=node;p!=null;p=p.parent)parts.Add(p.name);parts.Reverse();return string.Join("/",parts);}
    internal static void Clear()=>roots.Clear();
}

public static partial class MageVisualBridge
{
    public static string NativeEffectDiagnosticsJson=>NativePlayerEffects.SnapshotJson;
}
