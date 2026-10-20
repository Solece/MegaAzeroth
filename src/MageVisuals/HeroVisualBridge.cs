using System.Text.Json;
using AzerothMegabonk.MageVisuals;
using UnityEngine;

namespace AzerothHeroes;

// Visual-only adapter API. HeroBridge remains owned by the source class/ability
// module; this assembly discovers its selected/active class without a dependency.
public static class HeroVisualBridge
{
    public static string ActiveClassId=>HeroAppearance.CurrentClassId;
    public static int ActiveFormSpellId=>HeroAppearance.CurrentFormSpellId;
    public static bool IsAttached=>MageVisualBridge.IsAttached;
    public static Texture PortraitTexture=>MageVisualBridge.PortraitTexture;
    public static string AppearanceDiagnostics()=>JsonSerializer.Serialize(new{
        activeClassId=ActiveClassId,activeFormSpellId=ActiveFormSpellId,attached=IsAttached,sourceHeadClosed=MageVisualBridge.SourceHeadClosed,
        sourceVertexCount=MageVisualBridge.SourceVertexCount,bindCount=MageVisualBridge.BindCount,
        nativeMovementRetained=true,castPosesDisabled=true,appearance=HeroAppearance.Diagnostics(),
        equipment=JsonDocument.Parse(MageVisualBridge.WeaponDiagnostics()).RootElement.Clone(),
        motion=JsonDocument.Parse(MageVisualBridge.MotionDiagnostics()).RootElement.Clone(),
        portraitSourceCenter=new[]{MageVisualBridge.PortraitWorldCenter.x,MageVisualBridge.PortraitWorldCenter.y,MageVisualBridge.PortraitWorldCenter.z},
        portraitSourceRadius=MageVisualBridge.PortraitWorldRadius
    });
    public static string AnimationDiagnostics()=>MageVisualBridge.AnimationDiagnostics();
    public static bool SetClassForProbe(string classId)
    {
        if(!Allowed())return false;
        if(!string.IsNullOrEmpty(classId) && !HeroAppearance.Classes.Contains(classId))return false;
        HeroAppearance.ProbeClassOverride=string.IsNullOrEmpty(classId)?null:classId;return true;
    }
    public static string PrimeAllForProbe()
    {
        if(!Allowed())return "{\"allowed\":false}";
        bool bodies=HeroAppearance.PrimeAll(),equipment=SourceMageWeapon.PrimeAllForProbe();
        return JsonSerializer.Serialize(new{allowed=true,pass=bodies && equipment,classIds=HeroAppearance.Classes,appearance=HeroAppearance.Diagnostics(),equipmentValid=equipment});
    }
    public static bool SetFormForProbe(int spellId)
    {
        if(!Allowed() || spellId is not (-1 or 0 or 9634 or 768))return false;
        HeroAppearance.ProbeFormOverride=spellId<0?null:spellId;return true;
    }
    public static string RunMotionRegressionForProbe()=>MageVisualBridge.RunMotionRegressionForProbe();
    public static string RunRosterRegressionForProbe()
    {
        if(!Allowed())return "{\"allowed\":false}";
        HeroAppearance.PrimeAll();SourceMageWeapon.PrimeAllForProbe();int checks=0;
        void Check(bool value,string label){if(!value)throw new InvalidOperationException("Source hero roster regression: "+label);checks++;}
        var rows=new List<object>();
        foreach(string id in HeroAppearance.Classes)
        {
            var model=HeroAppearance.GetModel(id);using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(model.SourceFolder,"model.json")));var source=doc.RootElement.GetProperty("source");
            Check(source.GetProperty("classId").GetString()==id,id+" source class identity");
            int race=source.GetProperty("raceId").GetInt32(),expectedRace=id=="hunter"?3:id=="shaman"?2:id=="druid"?4:1;
            Check(race==expectedRace,id+" source canonical race identity");Check(model.HeadClosed,id+" validated rear head coverage");Check(model.IsUsable(),id+" all meshes retained");
            foreach(var movement in new[]{("idle",0),("walk",4),("run",5),("jump_start",37),("jump",38),("jump_end",39),("slide",115),("crouch",120),("death",1)})
            {
                Check(model.Clips.TryGetValue(movement.Item1,out var clip) && clip.AnimationId==movement.Item2,id+" original movement "+movement.Item1);
                Check(clip.Hands!=null && clip.Hands.Length==clip.Count*2,id+" animated hands "+movement.Item1);
                Check(clip.Portraits!=null && clip.Portraits.Length==clip.Count,id+" original head portrait "+movement.Item1);
            }
            foreach(string forbidden in new[]{"cast","cast_omni","ready_spell","ready_omni","channel"})Check(!model.Clips.ContainsKey(forbidden),id+" cast pose disabled "+forbidden);
            rows.Add(new{classId=id,raceId=race,vertices=model.VertexCount,clips=model.Clips.Count,sourceFolder=model.SourceFolder,headClosed=model.HeadClosed});
        }
        Check(SourceMageWeapon.AssetsValid,"all nine original equipment caches retained");
        return JsonSerializer.Serialize(new{allowed=true,pass=true,checks,classes=rows.ToArray(),appearance=HeroAppearance.Diagnostics()});
    }
    static bool Allowed()=>Application.isBatchMode && Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1";
}
