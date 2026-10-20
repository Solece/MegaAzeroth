using UnityEngine;
using Assets.Scripts.Actors.Player;
using HarmonyLib;
using System.Reflection;

namespace AzerothHud;

// Called on Unity's main thread by the mod test orchestrator. No mouse/keyboard simulation.
public static class HudProbeApi
{
    public static string[] BatchProbe(string action)
    {
        action=(action??"").Trim().ToLowerInvariant();
        if(action!="refresh" && action!="describe" && action!="" && !PostCombatFixture.Authorized)
            throw new InvalidOperationException("HUD mutation probes require Application.isBatchMode and AZEROTH_LAB_HEADLESS_TEST=1");
        var pause=UiManager.Instance?.pause;
        if(action=="pause")pause?.Pause();
        else if(action=="resume")pause?.Resume();
        else if(action=="scroll-bottom")HudBehaviour.Current?.ProbeScroll(0);
        else if(action=="scroll-top")HudBehaviour.Current?.ProbeScroll(1);
        else if(action.StartsWith("tooltip:")&&int.TryParse(action.Substring(8),out int index))HudBehaviour.Current?.ProbeTooltip(index);
        else if(action.StartsWith("xp:")&&int.TryParse(action.Substring(3),out int amount)&&amount>0)MyPlayer.Instance?.inventory?.AddXp(amount);
        else if(action=="postcombat")
        {
            PostCombatFixture.Arm();
            MyPlayer.Instance?.inventory?.playerHealth?.KillPlayer();
        }
        else if(action=="postcombat-open")
        {
            PostCombatFixture.Arm();
            if(GameManager.Instance==null || !GameManager.Instance.isGameOver)throw new InvalidOperationException("Native end-run is not complete; wait after postcombat before opening its stats.");
            UiManager.Instance?.deathScreen?.ShowStats();
        }
        HudBehaviour.Current?.ForceRefresh();Canvas.ForceUpdateCanvases();
        return Describe();
    }
    public static string[] Describe()
    {
        var rows=new List<string>
        {
            $"XP native={HudDiagnostics.NativeXp},level={HudDiagnostics.Level},current={HudDiagnostics.XpInLevel},maximum={HudDiagnostics.XpLevelMaximum},ratio={HudDiagnostics.XpProgress:F5},source={HudDiagnostics.ExperienceSource}",
            $"Pause={HudDiagnostics.Paused},spellPanel={PauseInventoryDiagnostics.Visible},learned={PauseInventoryDiagnostics.LearnedSpellCount},columns={PauseInventoryDiagnostics.Columns},rows={PauseInventoryDiagnostics.Rows},scroll={PauseInventoryDiagnostics.ScrollPosition:F3},viewport={PauseInventoryDiagnostics.ViewportBounds},sourceWeaponRow={PauseInventoryDiagnostics.SourceRowBounds},weaponGraphicsHidden={PauseInventoryDiagnostics.NativeWeaponGraphicsHidden},nativeTomesRetained={PauseInventoryDiagnostics.NativeTomeParentUnchanged},nativeItemsRetained={PauseInventoryDiagnostics.NativeItemParentUnchanged}",
            "CustomRunTime=absent; native GameTimer transforms/font sizes untouched"
        };
        rows.Add($"PostCombat={HudDiagnostics.PostCombat};combatHudHidden={PostCombatDiagnostics.CombatHudHidden};magePortrait={PostCombatDiagnostics.SourcePortraitShown};resultSpells={PostCombatDiagnostics.ResultSpellCount};resultViewport={PostCombatDiagnostics.ResultViewport};mappedRows={PostCombatDiagnostics.DamageRowsMapped};nativeXpFillsHidden={HudDiagnostics.NativeXpFillGraphicsHidden};testSaveGuard={PostCombatFixture.Active}");
        rows.Add($"SourcePlayerCluster topPixels={HudDiagnostics.PlayerClusterTopScreenPixels:F1},nativeTopRowBottomPixels={HudDiagnostics.NativeTopRowBottomScreenPixels:F1},bounds={HudDiagnostics.PlayerClusterScreenBounds},safeArea={HudDiagnostics.ScreenSafeArea}");
        rows.Add("NativeXpGraphics "+HudDiagnostics.NativeXpGraphics);
        foreach(var counter in Resources.FindObjectsOfTypeAll<KillsAndGoldCounter>())
        {
            if(counter==null || !counter.gameObject.scene.IsValid() || counter.t_gold==null)continue;
            var text=counter.t_gold;
            var rect=NativeItemLayout.ScreenBounds(text.rectTransform,NativeItemLayout.FindCanvasCamera(text.transform));
            rows.Add($"NativeGold active={text.gameObject.activeInHierarchy},enabled={text.enabled},text={text.text},rect={rect},anchoredPosition={text.rectTransform.anchoredPosition},size={text.rectTransform.sizeDelta};customGold=absent");
        }
        foreach(var experience in Resources.FindObjectsOfTypeAll<XpBarUI>())
        {
            if(experience==null || !experience.gameObject.scene.IsValid())continue;
            var text=experience.t_levelText;
            rows.Add($"NativeXpLevel enabled={(text!=null&&text.enabled)},active={(text!=null&&text.gameObject.activeInHierarchy)},text={(text!=null?text.text:"null")}");
        }
        rows.AddRange(EncounterSpellInventory.Diagnostics);
        rows.AddRange(DescribeTooltips());
        rows.AddRange(PostCombatDiagnostics.DescribeDamage());
        foreach(var timer in Resources.FindObjectsOfTypeAll<GameTimer>())
        {
            if(timer==null || !timer.gameObject.scene.IsValid())continue;
            var text=timer.t_timerRun;
            rows.Add($"NativeTimer active={timer.gameObject.activeInHierarchy},runEnabled={(text!=null&&text.enabled)},text={(text!=null?text.text:"null")},fontSize={(text!=null?text.fontSize:0)},position={(text!=null?text.rectTransform.anchoredPosition.ToString():"none")},size={(text!=null?text.rectTransform.sizeDelta.ToString():"none")}");
        }
        var hud=HudBehaviour.Current;
        if(hud!=null){rows.AddRange(hud.DescribePause());rows.AddRange(hud.DescribeResults());}
        return rows.ToArray();
    }
    public static string[] DescribeTooltips()
    {
        var rows=new List<string>();
        foreach(var tooltip in Resources.FindObjectsOfTypeAll<TooltipNew>())
        {
            if(tooltip==null || !tooltip.gameObject.scene.IsValid() || tooltip.t_tip==null)continue;
            var text=tooltip.t_tip;var renderer=text.canvasRenderer;
            if(text.gameObject.activeInHierarchy && text.enabled && renderer!=null && !renderer.cull && renderer.GetInheritedAlpha()>.001f)
                rows.Add("NativeTooltipNew visible=true text="+text.text);
        }
        foreach(var tooltip in Resources.FindObjectsOfTypeAll<ToolTipMouse>())
        {
            if(tooltip==null || !tooltip.gameObject.scene.IsValid() || tooltip.t_tooltip==null)continue;
            var text=tooltip.t_tooltip;var renderer=text.canvasRenderer;
            if(text.gameObject.activeInHierarchy && text.enabled && renderer!=null && !renderer.cull && renderer.GetInheritedAlpha()>.001f)
                rows.Add("NativeTooltipMouse visible=true text="+text.text);
        }
        if(rows.Count==0)rows.Add("NativeTooltip visible=false");return rows.ToArray();
    }
}

static class PostCombatFixture
{
    internal static bool Active;
    internal static bool Authorized=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1";
    internal static void Arm(){if(!Authorized)throw new InvalidOperationException("Post-combat fixture denied outside headless test");Active=true;}
}

[HarmonyPatch]
static class PostCombatFixtureSaveGuard
{
    static IEnumerable<MethodBase> TargetMethods()=>AccessTools.GetDeclaredMethods(typeof(SaveManager)).Where(m=>new[]{"SaveConfig","SaveStats","SaveProgression","SaveControllers","SaveTemp"}.Contains(m.Name)).Cast<MethodBase>();
    static bool Prefix()=>!PostCombatFixture.Active;
}
