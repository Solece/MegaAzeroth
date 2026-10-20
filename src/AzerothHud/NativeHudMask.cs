using UnityEngine;
using UnityEngine.UI;

namespace AzerothHud;

// Managed helper only. Native event subscribers remain enabled; only their visible Graphics are masked.
sealed class NativeHudMask
{
    sealed class SavedGraphic { public Graphic Graphic; public bool Enabled; public bool PlayerWorld; public bool ExactXp; }
    readonly Dictionary<int,SavedGraphic> saved=new();
    readonly List<Transform> branches=new();
    readonly List<Transform> protectedBranches=new();
    readonly List<Transform> playerWorldBranches=new();
    Transform nativeHudRoot;
    float nextDiscovery,nextXpDiscovery;
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
            if(inventory==null)continue;
            // Controllers can live on a shared ancestor; membership belongs to the concrete fields.
            AddBranch(inventory.weaponParent,hud);AddBranch(inventory.tomeParent,hud);
        }
        foreach(var health in Resources.FindObjectsOfTypeAll<HealthUI>())
        {
            if(health==null || !health.gameObject.scene.IsValid())continue;
            // HealthUI subscribes to PlayerHealth/OnPlayerInventoryInit. Its followPlayer instance
            // draws the local player's feet bar outside UiManager.hud; enemy/boss UI uses other types.
            bool playerWorld=health.followPlayer && health.gameObject.activeInHierarchy;
            AddBranch(health.transform,hud,playerWorld);
            AddBranch(health.healthBar,hud,playerWorld);AddBranch(health.shieldBar,hud,playerWorld);AddBranch(health.overhealBar,hud,playerWorld);
            if(health.t_hp!=null)AddBranch(health.t_hp.transform,hud,playerWorld);
            if(health.t_shield!=null)AddBranch(health.t_shield.transform,hud,playerWorld);
        }
        foreach(var experience in Resources.FindObjectsOfTypeAll<XpBarUI>())
        {
            if(experience==null || !experience.gameObject.scene.IsValid())continue;
            // This controller may be on the shared HUD root. Never select its subtree or level.
            DiscoverXpStrip(experience);
        }
        // GameTimer is a controller and may share a broad HUD container with inventory/HP.
        // Protect its actual text widgets, never the controller's entire Transform subtree.
        int protectedTimerText=0;
        foreach(var timer in Resources.FindObjectsOfTypeAll<GameTimer>())
        {
            if(timer==null)continue;
            ProtectTimerText(timer.t_timerRun,ref protectedTimerText);
            ProtectTimerText(timer.t_timerStage,ref protectedTimerText);
            ProtectTimerText(timer.t_timerSpeedrun,ref protectedTimerText);
        }
        HudDiagnostics.NativeTimerTextWidgetsProtected=protectedTimerText;
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
        // Discovery can change as native UI rebinds. Never retain a protected text in the mask.
        foreach(int id in saved.Keys.ToArray())
        {
            var entry=saved[id];
            if(entry.Graphic==null){saved.Remove(id);continue;}
            if(!entry.ExactXp && IsProtected(entry.Graphic.transform)){entry.Graphic.enabled=entry.Enabled;saved.Remove(id);}
        }
        Enforce();
        if(saved.Count!=lastCount)
        {
            lastCount=saved.Count;
            Plugin.Current.Log.LogInfo($"HUD selectively masked {lastCount} native Graphics in HP/XP, InventoryHud.weaponParent/tomeParent and local-player world HealthUI ({HudDiagnostics.PlayerWorldHealthGraphicsHidden} Graphics); native gold/counters, timer, level, objectives and dialogs retained.");
        }
    }
    void DiscoverXpStrip(XpBarUI experience)
    {
        var fill=experience.xpBar;if(fill==null)return;
        SaveXpGraphic(fill);
        var camera=NativeItemLayout.FindCanvasCamera(fill.transform);
        var fillRect=NativeItemLayout.ScreenBounds(fill.rectTransform,camera);
        Transform dedicatedStrip=null;
        var ancestor=fill.transform.parent;
        for(int depth=0;ancestor!=null && depth<4;depth++,ancestor=ancestor.parent)
        {
            var parentRect=ancestor.GetComponent<RectTransform>();if(parentRect==null)continue;
            var bounds=NativeItemLayout.ScreenBounds(parentRect,NativeItemLayout.FindCanvasCamera(ancestor));
            if(bounds.width<Screen.width*.4f || bounds.height<=0 || bounds.height>64)continue;
            if(fillRect.xMin<bounds.xMin-4 || fillRect.xMax>bounds.xMax+4 || fillRect.yMin<bounds.yMin-4 || fillRect.yMax>bounds.yMax+4)continue;
            dedicatedStrip=ancestor;
            // A native XP fill can resize to zero at level start; its dedicated shallow
            // container still supplies the complete background/frame rectangle.
            fillRect=bounds;
        }
        if(fillRect.width<Screen.width*.4f || fillRect.height<=0)return;
        // Background/frame siblings outlive a zero-width fill. Restrict matching to this
        // long, shallow screen band and to ancestors of the authoritative typed XP field.
        var band=new Rect(fillRect.xMin-12,fillRect.yMin-32,fillRect.width+24,fillRect.height+64);
        ancestor=fill.transform.parent;
        for(int depth=0;ancestor!=null && depth<4;depth++,ancestor=ancestor.parent)
        {
            foreach(var graphic in ancestor.GetComponentsInChildren<Graphic>(true))
            {
                if(graphic==null || graphic is TMPro.TMP_Text || graphic==experience.t_levelText)continue;
                var rect=NativeItemLayout.ScreenBounds(graphic.rectTransform,NativeItemLayout.FindCanvasCamera(graphic.transform));
                bool dedicated=dedicatedStrip!=null && Under(graphic.transform,dedicatedStrip);
                if((!dedicated && rect.width<fillRect.width*.8f) || rect.height>64 || rect.height<=0)continue;
                if(rect.xMin<band.xMin || rect.xMax>band.xMax || rect.yMin<band.yMin || rect.yMax>band.yMax)continue;
                SaveXpGraphic(graphic);
            }
        }
    }
    void SaveXpGraphic(Graphic graphic)
    {
        int id=graphic.GetInstanceID();
        if(!saved.ContainsKey(id))saved[id]=new SavedGraphic{Graphic=graphic,Enabled=graphic.enabled,ExactXp=true};
        else saved[id].ExactXp=true;
    }
    public void Enforce()
    {
        int playerWorld=0,exactXp=0;var xpRows=new List<string>();
        foreach(var entry in saved.Values)
        {
            if(entry.Graphic==null)continue;
            if(entry.Graphic.enabled)entry.Graphic.enabled=false;
            if(entry.PlayerWorld)playerWorld++;
            if(entry.ExactXp)
            {
                exactXp++;
                var bounds=NativeItemLayout.ScreenBounds(entry.Graphic.rectTransform,NativeItemLayout.FindCanvasCamera(entry.Graphic.transform));
                xpRows.Add($"{entry.Graphic.name}:enabled={entry.Graphic.enabled},active={entry.Graphic.gameObject.activeInHierarchy},rect={bounds}");
            }
        }
        HudDiagnostics.NativeGraphicsHidden=saved.Count;
        HudDiagnostics.PlayerWorldHealthGraphicsHidden=playerWorld;
        HudDiagnostics.NativeXpFillGraphicsHidden=exactXp;
        HudDiagnostics.NativeXpGraphics=string.Join(";",xpRows);
    }
    public void EnforceXpOnly()
    {
        if(Time.unscaledTime>=nextXpDiscovery)
        {
            nextXpDiscovery=Time.unscaledTime+1;
            foreach(var experience in Resources.FindObjectsOfTypeAll<XpBarUI>())
                if(experience!=null && experience.gameObject.scene.IsValid())DiscoverXpStrip(experience);
        }
        int count=0;
        foreach(var entry in saved.Values)
            if(entry.ExactXp && entry.Graphic!=null){entry.Graphic.enabled=false;count++;}
        HudDiagnostics.NativeXpFillGraphicsHidden=count;
        HudDiagnostics.NativeXpGraphics=string.Join(";",saved.Values.Where(e=>e.ExactXp&&e.Graphic!=null).Select(e=>e.Graphic.name+":enabled="+e.Graphic.enabled+",rect="+NativeItemLayout.ScreenBounds(e.Graphic.rectTransform,NativeItemLayout.FindCanvasCamera(e.Graphic.transform))));
    }
    public void Restore(bool keepXp=false)
    {
        foreach(var key in saved.Keys.ToArray())
        {
            var entry=saved[key];
            if(keepXp && entry.ExactXp && entry.Graphic!=null)continue;
            if(entry.Graphic!=null)entry.Graphic.enabled=entry.Enabled;
            saved.Remove(key);
        }
        branches.Clear();protectedBranches.Clear();playerWorldBranches.Clear();
        nextDiscovery=0;lastCount=0;HudDiagnostics.NativeGraphicsHidden=saved.Count;HudDiagnostics.PlayerWorldHealthGraphicsHidden=0;
        if(keepXp)EnforceXpOnly();
        else{nextXpDiscovery=0;HudDiagnostics.NativeXpFillGraphicsHidden=0;HudDiagnostics.NativeXpGraphics="";}
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
    void ProtectTimerText(Graphic value,ref int count)
    {
        if(value==null)return;
        // A TextMeshProUGUI owns only its text and optional glyph submesh children.
        protectedBranches.Add(value.transform);count++;
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
    public static bool Paused {get;internal set;}
    public static bool PostCombat {get;internal set;}
    public static int NativeXp {get;internal set;}
    public static int Level {get;internal set;}
    public static int XpInLevel {get;internal set;}
    public static int XpLevelMaximum {get;internal set;}
    public static float XpProgress {get;internal set;}
    public static string ExperienceSource {get;internal set;}="";
    public static bool CustomTimerVisible=>false;
    public static int NativeTimerTextWidgetsProtected {get;internal set;}
    public static int NativeXpFillGraphicsHidden {get;internal set;}
    public static string NativeXpGraphics {get;internal set;}="";
    public static float PlayerClusterTopScreenPixels {get;internal set;}
    public static float NativeTopRowBottomScreenPixels {get;internal set;}
    public static string PlayerClusterScreenBounds {get;internal set;}="";
    public static string ScreenSafeArea {get;internal set;}="";
}
