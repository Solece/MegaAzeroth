using System.Text.Json;
using UnityEngine;

namespace AzerothMegabonk.MageVisuals;

// The body follows native movement state only. Spell casts still run through
// AzerothAbilities and use the current movement frame's baked hand positions.
internal static class MageMotionPolicy
{
    internal static string ProbeClip;
    static bool grounded;
    static int crouchState;
    static float speedSquared;
    static string nativeClip="",renderedClip="";
    static int sourceAnimationId,frame,frames;
    static bool loop;
    internal static string Select(bool onGround,int state,float horizontalSpeedSquared)
        => !onGround ? "jump" : state==2 ? "slide" : state==1 ? "crouch" : horizontalSpeedSquared>.16f ? "run" : "idle";
    internal static void Observe(bool ground,int crouch,float speed,string selected)
    {grounded=ground;crouchState=crouch;speedSquared=speed;nativeClip=selected;}
    internal static void ObserveFrame(string clip,int sourceId,int index,bool repeats,int count)
    {renderedClip=clip;sourceAnimationId=sourceId;frame=index;loop=repeats;frames=count;}
    internal static string Describe()=>JsonSerializer.Serialize(new{
        animationClock=MageAnimationClock.Snapshot(),castPoseDisabled=true,nativeGrounded=grounded,nativeCrouchState=crouchState,nativeHorizontalSpeedSquared=speedSquared,
        selectedByNativeState=nativeClip,currentClip=renderedClip,sourceAnimationId,currentFrame=frame,frameCount=frames,loop,
        probeOverride=ProbeClip,sourceHands=MageVisualBridge.HandWorldPositions.Length,
        sourceWeaponFrameSynced=SourceMageWeapon.FrameMatches(renderedClip,frame),
        clips=Plugin.Model?.Clips.Values.Select(c=>new{name=c.Name,sourceAnimationId=c.AnimationId,frames=c.Count,loop=c.Loop}).ToArray()
    });
    internal static string RunRegression()
    {
        var model=Plugin.Model;if(model==null)throw new InvalidOperationException("Mage model is not loaded");
        int checks=0;
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException("Mage movement regression: "+label);checks++;}
        Check(Select(true,0,0)=="idle","idle without native movement");
        Check(Select(true,0,4)=="run","native horizontal movement");
        Check(Select(false,0,4)=="jump","native airborne movement");
        Check(Select(true,1,0)=="crouch","real native CrouchState.Crouching");
        Check(Select(true,2,4)=="slide","real native CrouchState.Sliding");
        Check(Select(false,2,4)=="jump","airborne outranks stale slide state");
        foreach(string forbidden in new[]{"cast","cast_omni","ready_spell","ready_omni","channel"})
            Check(!model.Clips.ContainsKey(forbidden),"cast pose absent from runtime pack: "+forbidden);
        bool form=HeroAppearance.CurrentFormSpellId!=0;
        foreach(var entry in new[]{("idle",0),("run",5),("jump",38),("slide",form?5:115),("crouch",form?0:120)})
        {
            Check(model.Clips.TryGetValue(entry.Item1,out var clip) && clip.AnimationId==entry.Item2,"original clip source id "+entry.Item1);
            Check(clip.Hands!=null && clip.Hands.Length==clip.Count*2,"source VFX hand attachments "+entry.Item1);
            if(entry.Item1 is "slide" or "crouch")Check(clip.Loop,"continuous low pose "+entry.Item1);
        }
        Check(model.IsUsable(),"all body source assets retained");
        return JsonSerializer.Serialize(new{pass=true,checks,current=JsonDocument.Parse(Describe()).RootElement.Clone()});
    }
}
public static partial class MageVisualBridge
{
    public static string MotionDiagnostics()=>MageMotionPolicy.Describe();
    public static string AnimationDiagnostics()=>JsonSerializer.Serialize(new{clock=MageAnimationClock.Snapshot(),body=JsonDocument.Parse(MageMotionPolicy.Describe()).RootElement.Clone(),companions=JsonDocument.Parse(CreatureVisualBridge.AnimationDiagnostics()).RootElement.Clone(),weapon=JsonDocument.Parse(SourceMageWeapon.Describe()).RootElement.Clone()});
    public static string RunMotionRegressionForProbe()
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return "{\"allowed\":false}";
        return MageMotionPolicy.RunRegression();
    }
    public static bool SetMotionForProbe(string clip)
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")return false;
        if(!string.IsNullOrEmpty(clip) && clip is not ("idle" or "run" or "jump" or "slide" or "crouch"))return false;
        MageMotionPolicy.ProbeClip=string.IsNullOrEmpty(clip)?null:clip;return true;
    }
}
