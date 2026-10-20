using UnityEngine;
using UnityEngine.UI;

namespace AzerothHud;

// Managed helper only. Native event subscribers remain enabled; only their visible Graphics are masked.
sealed class NativeHudMask
{
    sealed class SavedGraphic { public Graphic Graphic; public bool Enabled; public bool PlayerWorld; }
    readonly Dictionary<int,SavedGraphic> saved=new();
    readonly List<Transform> branches=new();
    readonly List<Transform> protectedBranches=new();
    readonly List<Transform> playerWorldBranches=new();
    Transform nativeHudRoot;
    float nextDiscovery;
    int lastCount;

    public void Refresh()
    {
        if(Time.unscaledTime<nextDiscovery){Enforce();return;}
        nextDiscovery=Time.unscaledTime+1;
        var ui=UiManager.Instance;
        if(ui==null || ui.hud==null)return;
        var hud=ui.hud.transform;
        nativeHudRoot=hud;
        branches.Clear();protectedBranches.Clear();playerWorldBranches.Clear();
        Protect(ui.objective);Protect(ui.objectiveArrow);Protect(ui.encounterWindows);Protect(ui.pause);
        Protect(ui.deathScreen);Protect(ui.scoreUi);Protect(ui.alertUi);Protect(ui.shrineLogs);
        Protect(ui.feed);Protect(ui.mapTile);Protect(ui.cinematicBars);Protect(ui.colorFilterUi);
        foreach(var levelup in Resources.FindObjectsOfTypeAll<LevelupScreen>())Protect(levelup);

        foreach(var inventory in Resources.FindObjectsOfTypeAll<InventoryHud>())
        {
            if(inventory==null || !Under(inventory.transform,hud))continue;
            AddBranch(inventory.weaponParent,hud);AddBranch(inventory.tomeParent,hud);
        }
        foreach(var health in Resources.FindObjectsOfTypeAll<HealthUI>())
        {
            if(health==null)continue;
            // HealthUI subscribes to PlayerHealth/OnPlayerInventoryInit. Its followPlayer instance
            // draws the local player's feet bar outside UiManager.hud; enemy/boss UI uses other types.
            bool playerWorld=health.followPlayer && health.gameObject.activeInHierarchy;
            if(!Under(health.transform,hud) && !playerWorld)continue;
            AddBranch(health.transform,hud,playerWorld);
            AddBranch(health.healthBar,hud,playerWorld);AddBranch(health.shieldBar,hud,playerWorld);AddBranch(health.overhealBar,hud,playerWorld);
            if(health.t_hp!=null)AddBranch(health.t_hp.transform,hud,playerWorld);
            if(health.t_shield!=null)AddBranch(health.t_shield.transform,hud,playerWorld);
        }
        foreach(var experience in Resources.FindObjectsOfTypeAll<XpBarUI>())
        {
            if(experience==null || !Under(experience.transform,hud))continue;
            AddBranch(experience.transform,hud);
            if(experience.xpBar!=null)AddBranch(experience.xpBar.transform,hud);
            if(experience.t_levelText!=null)AddBranch(experience.t_levelText.transform,hud);
        }
        foreach(var timer in Resources.FindObjectsOfTypeAll<GameTimer>())
        {
            if(timer==null || !Under(timer.transform,hud))continue;
            AddBranch(timer.transform,hud);
            if(timer.t_timerRun!=null)AddBranch(timer.t_timerRun.transform,hud);
            if(timer.t_timerStage!=null)AddBranch(timer.t_timerStage.transform,hud);
            if(timer.t_timerSpeedrun!=null)AddBranch(timer.t_timerSpeedrun.transform,hud);
        }
        foreach(var counter in Resources.FindObjectsOfTypeAll<KillsAndGoldCounter>())
        {
            if(counter==null || !Under(counter.transform,hud) || counter.t_gold==null)continue;
            var text=counter.t_gold.transform;
            var group=text.parent;
            // A dedicated coin row includes its icon. Never absorb kills/silver/chest price siblings.
            if(group!=null && group!=counter.transform &&
                !Contains(group,counter.t_kills) && !Contains(group,counter.t_silver) && !Contains(group,counter.t_chestPrice))
                AddBranch(group,hud);
            AddBranch(text,hud);
        }
        foreach(var graphic in Resources.FindObjectsOfTypeAll<Graphic>())
        {
            if(graphic==null || IsProtected(graphic.transform))continue;
            bool selected=false;
            foreach(var branch in branches)if(Under(graphic.transform,branch)){selected=true;break;}
            if(!selected)continue;
            int id=graphic.GetInstanceID();
            bool isPlayerWorld=false;
            foreach(var branch in playerWorldBranches)if(Under(graphic.transform,branch)){isPlayerWorld=true;break;}
            if(!saved.ContainsKey(id))saved[id]=new SavedGraphic{Graphic=graphic,Enabled=graphic.enabled,PlayerWorld=isPlayerWorld};
        }
        Enforce();
        if(saved.Count!=lastCount)
        {
            lastCount=saved.Count;
            Plugin.Current.Log.LogInfo($"HUD selectively masked {lastCount} native Graphics in HP/XP/timer/gold, InventoryHud.weaponParent/tomeParent and local-player world HealthUI ({HudDiagnostics.PlayerWorldHealthGraphicsHidden} Graphics); objectives, dialogs, enemy/boss bars and scripts retained.");
        }
    }
    public void Enforce()
    {
        int playerWorld=0;
        foreach(var entry in saved.Values)
        {
            if(entry.Graphic==null)continue;
            if(entry.Graphic.enabled)entry.Graphic.enabled=false;
            if(entry.PlayerWorld)playerWorld++;
        }
        HudDiagnostics.NativeGraphicsHidden=saved.Count;
        HudDiagnostics.PlayerWorldHealthGraphicsHidden=playerWorld;
    }
    public void Restore()
    {
        if(saved.Count==0)return;
        foreach(var entry in saved.Values)if(entry.Graphic!=null)entry.Graphic.enabled=entry.Enabled;
        saved.Clear();branches.Clear();protectedBranches.Clear();playerWorldBranches.Clear();
        nextDiscovery=0;lastCount=0;HudDiagnostics.NativeGraphicsHidden=0;HudDiagnostics.PlayerWorldHealthGraphicsHidden=0;
    }
    void AddBranch(Transform candidate,Transform hud,bool playerWorld=false)
    {
        if(candidate==null || candidate==hud || (!Under(candidate,hud) && !playerWorld))return;
        // Never select an ancestor of an unrelated interactive/objective widget.
        foreach(var protectedBranch in protectedBranches)if(Under(protectedBranch,candidate))return;
        if(IsProtected(candidate))return;
        branches.Add(candidate);
        if(playerWorld)playerWorldBranches.Add(candidate);
    }
    void Protect(Component value)
    {
        // Some manager scripts live on a shared root. Such a root cannot identify one widget.
        if(value!=null && !Under(nativeHudRoot,value.transform))protectedBranches.Add(value.transform);
    }
    bool IsProtected(Transform candidate)
    {
        foreach(var branch in protectedBranches)if(Under(candidate,branch))return true;
        return false;
    }
    static bool Contains(Transform root,Component value)=>value!=null && Under(value.transform,root);
    static bool Under(Transform candidate,Transform root)=>candidate!=null && root!=null && (candidate==root || candidate.IsChildOf(root));
}

public static class HudDiagnostics
{
    public static int NativeGraphicsHidden {get;internal set;}
    public static int PlayerWorldHealthGraphicsHidden {get;internal set;}
    public static int BookSlotCount=>4;
}
