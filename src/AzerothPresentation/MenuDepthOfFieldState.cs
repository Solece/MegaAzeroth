using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using Object=UnityEngine.Object;
namespace AzerothPresentation;

// Own only PostProcessEffectSettings.active on DepthOfField. The existing hero
// focus adapter owns distance/aperture; these settings are intentionally untouched.
sealed class MenuDepthOfFieldState
{
    readonly Dictionary<int,(DepthOfField Effect,bool Active)> saved=new();
    int staleEffectsPruned;
    static bool TryRead(DepthOfField effect,out bool active)
    {active=false;try{if(effect==null||effect.name==null)return false;active=effect.active;return true;}catch{return false;}}
    void PruneDestroyed(){foreach(var id in saved.Where(x=>!TryRead(x.Value.Effect,out _)).Select(x=>x.Key).ToArray()){saved.Remove(id);staleEffectsPruned++;}}
    internal void Apply(Camera camera)
    {
        PruneDestroyed();
        var layer=camera.GetComponent<PostProcessLayer>();if(layer==null||!layer.enabled)return;
        foreach(var volume in Object.FindObjectsOfType<PostProcessVolume>())
        {
            if(!volume.isActiveAndEnabled||(layer.volumeLayer.value&(1<<volume.gameObject.layer))==0)continue;
            var profile=volume.profile;if(profile?.settings==null)continue;
            foreach(var setting in profile.settings)
            {
                var effect=setting?.TryCast<DepthOfField>();if(effect==null)continue;int id=effect.GetInstanceID();
                if(!saved.ContainsKey(id))saved[id]=(effect,effect.active);effect.active=false;
            }
        }
    }
    internal object Snapshot()
    {
        PruneDestroyed();
        return new{ChangedField="DepthOfField.active only",StaleEffectsPruned=staleEffectsPruned,Effects=saved.Select(s=>new{Id=s.Key,OriginalActive=s.Value.Active,CurrentActive=TryRead(s.Value.Effect,out var active)?(bool?)active:null}).ToArray()};
    }
    internal object Restore()
    {
        int restored=0,missing=0,failed=0;foreach(var row in saved.Values)try{if(row.Effect==null){missing++;continue;}row.Effect.active=row.Active;if(row.Effect.active==row.Active)restored++;else failed++;}catch{missing++;}
        saved.Clear();return new{Restored=restored,SceneObjectsGone=missing,Failed=failed};
    }
}
