using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using Il2CppInterop.Runtime;
using System.Runtime.InteropServices;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

// Character models have different depth from the native placeholder. Focus the
// menu camera on the actual preview and restore native settings when it leaves.
internal sealed class MenuPreviewFocus
{
    sealed class Saved
    {
        internal DepthOfField Effect;internal float Distance,Aperture;
        internal bool DistanceOverride,ApertureOverride;
    }
    readonly Dictionary<int,Saved> saved=new();
    float desiredDistance;int applied;string cameraName="";
    internal void Update(Renderer subject)
    {
        if(subject==null)return;
        var menu=Object.FindObjectsOfType<MenuCamera>().FirstOrDefault(m=>m!=null && m.camera!=null && m.camera.enabled);
        var camera=menu?.camera??Camera.main;if(camera==null)return;
        cameraName=camera.name;desiredDistance=Mathf.Max(.1f,Vector3.Dot(subject.bounds.center-camera.transform.position,camera.transform.forward));applied=0;
        var layer=camera.GetComponent<PostProcessLayer>();if(layer==null || !layer.enabled)return;
        foreach(var volume in Object.FindObjectsOfType<PostProcessVolume>())
        {
            if(volume==null||!volume.enabled||!volume.gameObject.activeInHierarchy || (layer.volumeLayer.value&(1<<volume.gameObject.layer))==0)continue;
            var profile=volume.profile;if(profile?.settings==null)continue;
            foreach(var setting in profile.settings)
            {
                var effect=setting?.TryCast<DepthOfField>();if(effect==null)continue;
                int id=effect.GetInstanceID();
                if(!saved.TryGetValue(id,out var original))saved[id]=original=new Saved{Effect=effect,Distance=ReadValue(effect.focusDistance),Aperture=ReadValue(effect.aperture),DistanceOverride=effect.focusDistance.overrideState,ApertureOverride=effect.aperture.overrideState};
                effect.focusDistance.overrideState=true;SetValue(effect.focusDistance,desiredDistance);
                effect.aperture.overrideState=true;SetValue(effect.aperture,Mathf.Max(original.Aperture,16f));applied++;
            }
        }
    }
    internal object Snapshot()=>new{camera=cameraName,focusDistance=desiredDistance,appliedProfiles=applied,
        profiles=saved.Values.Select(Inspect).ToArray()};
    static object Inspect(Saved s)
    {
        try{return new{nativeAlive=s.Effect!=null,originalFocus=s.Distance,originalAperture=s.Aperture,currentFocus=ReadValue(s.Effect.focusDistance),currentAperture=ReadValue(s.Effect.aperture)};}
        catch{return new{nativeAlive=false,originalFocus=s.Distance,originalAperture=s.Aperture};}
    }
    // The generated ParameterOverride<float>.value marshaller returns boxed
    // pointer bits on this IL2CPP build. Resolve the actual runtime field for
    // read-only evidence and use the concrete native FloatParameter.Interp API.
    static float ReadValue(FloatParameter parameter)
    {
        var klass=IL2CPP.il2cpp_object_get_class(parameter.Pointer);
        var field=IL2CPP.il2cpp_class_get_field_from_name(klass,"value");
        if(field==IntPtr.Zero)throw new MissingFieldException("Native FloatParameter.value");
        int offset=(int)IL2CPP.il2cpp_field_get_offset(field);
        if(offset<16||offset>256)throw new InvalidDataException("Unexpected native FloatParameter.value offset "+offset);
        float value=BitConverter.Int32BitsToSingle(Marshal.ReadInt32(parameter.Pointer,offset));
        if(!float.IsFinite(value)||value<=0||value>100000)throw new InvalidDataException("Invalid native focus parameter "+value);
        return value;
    }
    static void SetValue(FloatParameter parameter,float value)
    {
        parameter.Interp(value,value,1f);
        if(Mathf.Abs(ReadValue(parameter)-value)>.001f)throw new InvalidOperationException("Native focus parameter did not update");
    }
    internal void Restore()
    {
        foreach(var s in saved.Values)
        {
            try{if(s.Effect!=null)RestoreParameter(s.Effect.focusDistance,s.Distance,s.DistanceOverride);}catch{}
            try{if(s.Effect!=null)RestoreParameter(s.Effect.aperture,s.Aperture,s.ApertureOverride);}catch{}
        }
        saved.Clear();applied=0;
    }
    static void RestoreParameter(FloatParameter parameter,float value,bool overridden)
    {
        try{SetValue(parameter,value);}
        finally{parameter.overrideState=overridden;}
    }
}
