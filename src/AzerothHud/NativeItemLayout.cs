using UnityEngine;
using UnityEngine.UI;

namespace AzerothHud;

// The in-run item strip belongs to ItemsHud, not InventoryHud or UpgradeInventoryUI.
// Move the native strip as one unit so counts, order, tooltips and pickup updates stay native.
sealed class NativeItemLayout
{
    sealed class SavedLayout
    {
        public RectTransform Rect;
        public Transform Parent;
        public Vector2 AnchoredPosition;
        public Camera Camera;
        public readonly List<RawImage> Icons=new();
        public float LastLoggedOffset=float.NaN;
        public int LastLoggedCount=-1;
    }

    readonly Dictionary<int,SavedLayout> saved=new();
    float nextDiscovery;

    public void Refresh(float bottomInsetScreenPixels)
    {
        bottomInsetScreenPixels=Mathf.Clamp(bottomInsetScreenPixels,0,Screen.height*.75f);
        var ui=UiManager.Instance;
        if(ui==null || ui.hud==null){Restore();return;}
        var hud=ui.hud.transform;
        if(Time.unscaledTime>=nextDiscovery)
        {
            nextDiscovery=Time.unscaledTime+.5f;
            Discover(hud);
        }

        int containers=0,visibleItems=0;
        float lowestItem=float.PositiveInfinity,lowestContainer=float.PositiveInfinity,maxOffset=0;
        foreach(var layout in saved.Values)
        {
            var rect=layout.Rect;
            if(rect==null || !Under(rect,hud) || !rect.gameObject.activeInHierarchy)continue;
            if(rect.parent!=layout.Parent)
            {
                // A native reparent starts a new coordinate space; never reuse an old offset there.
                layout.Parent=rect.parent;
                layout.AnchoredPosition=rect.anchoredPosition;
                layout.Camera=FindCanvasCamera(rect);
            }
            rect.anchoredPosition=layout.AnchoredPosition;
            float originalBottom=ScreenBottom(rect,layout.Camera);
            int count=0;
            foreach(var icon in layout.Icons)
            {
                if(!Candidate(icon))continue;
                var iconRect=icon.GetComponent<RectTransform>();
                if(iconRect==null)continue;
                originalBottom=Mathf.Min(originalBottom,ScreenBottom(iconRect,layout.Camera));
            }
            float lift=Mathf.Max(0,bottomInsetScreenPixels-originalBottom);
            if(lift>.01f)
            {
                var parentRect=layout.Parent!=null ? layout.Parent.GetComponent<RectTransform>() : null;
                if(parentRect!=null &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect,new Vector2(Screen.width*.5f,originalBottom),layout.Camera,out var from) &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect,new Vector2(Screen.width*.5f,originalBottom+lift),layout.Camera,out var to))
                    rect.anchoredPosition=layout.AnchoredPosition+(to-from);
                else
                {
                    // Canvas roots normally expose RectTransform; keep a safe overlay fallback.
                    float scale=Mathf.Max(.0001f,Mathf.Abs(rect.parent!=null ? rect.parent.lossyScale.y : 1));
                    rect.anchoredPosition=layout.AnchoredPosition+new Vector2(0,lift/scale);
                }
            }
            containers++;
            lowestContainer=Mathf.Min(lowestContainer,ScreenBottom(rect,layout.Camera));
            foreach(var icon in layout.Icons)
            {
                if(!Candidate(icon))continue;
                var iconRect=icon.GetComponent<RectTransform>();
                if(iconRect!=null && Renderable(icon,layout.Camera)){count++;lowestItem=Mathf.Min(lowestItem,ScreenBottom(iconRect,layout.Camera));}
            }
            visibleItems+=count;
            maxOffset=Mathf.Max(maxOffset,lift);
            if(float.IsNaN(layout.LastLoggedOffset) || Mathf.Abs(layout.LastLoggedOffset-lift)>1 || layout.LastLoggedCount!=count)
            {
                layout.LastLoggedOffset=lift;layout.LastLoggedCount=count;
                Plugin.Current?.Log.LogInfo($"ItemsHud layout: {rect.name}, visible item icons={count}, bottom inset={bottomInsetScreenPixels:F1}px, lift={lift:F1}px; anchored position={rect.anchoredPosition} (original={layout.AnchoredPosition}).");
            }
        }
        NativeItemLayoutDiagnostics.Containers=containers;
        NativeItemLayoutDiagnostics.VisibleItemCount=visibleItems;
        NativeItemLayoutDiagnostics.BottomInsetScreenPixels=bottomInsetScreenPixels;
        NativeItemLayoutDiagnostics.LowestVisibleItemScreenY=visibleItems>0 && !float.IsPositiveInfinity(lowestItem) ? lowestItem : -1;
        NativeItemLayoutDiagnostics.LowestContainerScreenY=containers>0 && !float.IsPositiveInfinity(lowestContainer) ? lowestContainer : -1;
        NativeItemLayoutDiagnostics.MaximumLiftScreenPixels=maxOffset;
    }

    void Discover(Transform hud)
    {
        var found=new HashSet<int>();
        foreach(var strip in Resources.FindObjectsOfTypeAll<ItemsHud>())
        {
            if(strip==null || !Under(strip.transform,hud))continue;
            var rect=strip.GetComponent<RectTransform>();
            if(rect==null)continue;
            int id=rect.GetInstanceID();found.Add(id);
            if(!saved.TryGetValue(id,out var layout))
            {
                layout=new SavedLayout{Rect=rect,Parent=rect.parent,AnchoredPosition=rect.anchoredPosition,Camera=FindCanvasCamera(rect)};
                saved[id]=layout;
            }
            layout.Icons.Clear();
        }
        // Prefab assets, pause-menu inventory and unrelated pickup windows are outside ItemsHud.
        foreach(var item in Resources.FindObjectsOfTypeAll<ItemsHudElementPrefab>())
        {
            if(item==null || item.icon==null)continue;
            foreach(var layout in saved.Values)
                if(layout.Rect!=null && Under(item.transform,layout.Rect))layout.Icons.Add(item.icon);
        }
        foreach(int stale in saved.Keys.Where(id=>!found.Contains(id)).ToArray())
        {
            var layout=saved[stale];
            if(layout.Rect!=null && layout.Rect.parent==layout.Parent)layout.Rect.anchoredPosition=layout.AnchoredPosition;
            saved.Remove(stale);
        }
    }

    public void Restore()
    {
        foreach(var layout in saved.Values)
            if(layout.Rect!=null && layout.Rect.parent==layout.Parent)layout.Rect.anchoredPosition=layout.AnchoredPosition;
        saved.Clear();nextDiscovery=0;
        NativeItemLayoutDiagnostics.Containers=0;
        NativeItemLayoutDiagnostics.VisibleItemCount=0;
        NativeItemLayoutDiagnostics.BottomInsetScreenPixels=0;
        NativeItemLayoutDiagnostics.LowestVisibleItemScreenY=-1;
        NativeItemLayoutDiagnostics.LowestContainerScreenY=-1;
        NativeItemLayoutDiagnostics.MaximumLiftScreenPixels=0;
    }

    internal static bool Candidate(RawImage icon)=>icon!=null && icon.enabled && icon.gameObject.activeInHierarchy && icon.texture!=null && icon.color.a>0;
    internal static bool Renderable(RawImage icon,Camera camera)
    {
        if(!Candidate(icon))return false;
        var renderer=icon.canvasRenderer;
        if(renderer==null || renderer.cull || renderer.GetInheritedAlpha()<=.001f || renderer.GetAlpha()<=.001f)return false;
        var rect=icon.GetComponent<RectTransform>();
        if(rect==null)return false;
        var bounds=ScreenBounds(rect,camera);
        var viewport=camera!=null ? camera.pixelRect : new Rect(0,0,Screen.width,Screen.height);
        if(bounds.width<=.01f || bounds.height<=.01f || bounds.xMin<viewport.xMin-.5f || bounds.xMax>viewport.xMax+.5f || bounds.yMin<viewport.yMin-.5f || bounds.yMax>viewport.yMax+.5f)return false;
        bool ignoreGroups=false;
        for(var ancestor=icon.transform;ancestor!=null;ancestor=ancestor.parent)
        {
            var owner=ancestor.GetComponent<Canvas>();
            if(owner!=null && !owner.isActiveAndEnabled)return false;
            if(!ignoreGroups)
            {
                var group=ancestor.GetComponent<CanvasGroup>();
                if(group!=null && group.enabled)
                {
                    if(group.alpha<=.001f)return false;
                    if(group.ignoreParentGroups)ignoreGroups=true;
                }
            }
            var mask=ancestor.GetComponent<RectMask2D>();
            var graphicMask=ancestor.GetComponent<Mask>();
            if((mask!=null && mask.isActiveAndEnabled) || (graphicMask!=null && graphicMask.isActiveAndEnabled))
            {
                var clipRect=ancestor.GetComponent<RectTransform>();
                if(clipRect==null)continue;
                var clip=ScreenBounds(clipRect,camera);
                if(bounds.xMin<clip.xMin-.5f || bounds.xMax>clip.xMax+.5f || bounds.yMin<clip.yMin-.5f || bounds.yMax>clip.yMax+.5f)return false;
            }
        }
        return true;
    }
    static bool Under(Transform child,Transform root)=>child!=null && root!=null && (child==root || child.IsChildOf(root));

    internal static Camera FindCanvasCamera(Transform node)
    {
        for(var parent=node;parent!=null;parent=parent.parent)
        {
            var canvas=parent.GetComponent<Canvas>();
            if(canvas==null)continue;
            var root=canvas.rootCanvas;
            if(root==null)root=canvas;
            return root.renderMode==RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }
        return null;
    }

    static float ScreenBottom(RectTransform rect,Camera camera)=>ScreenBounds(rect,camera).yMin;
    internal static Rect ScreenBounds(RectTransform rect,Camera camera)
    {
        var r=rect.rect;
        var a=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(r.xMin,r.yMin,0)));
        var b=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(r.xMax,r.yMin,0)));
        var c=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(r.xMin,r.yMax,0)));
        var d=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(r.xMax,r.yMax,0)));
        return Rect.MinMaxRect(Mathf.Min(Mathf.Min(a.x,b.x),Mathf.Min(c.x,d.x)),Mathf.Min(Mathf.Min(a.y,b.y),Mathf.Min(c.y,d.y)),Mathf.Max(Mathf.Max(a.x,b.x),Mathf.Max(c.x,d.x)),Mathf.Max(Mathf.Max(a.y,b.y),Mathf.Max(c.y,d.y)));
    }
}

public static class NativeItemLayoutDiagnostics
{
    public static int Containers {get;internal set;}
    public static int VisibleItemCount {get;internal set;}
    public static float BottomInsetScreenPixels {get;internal set;}
    // -1 means no actual visible item icon was found, rather than an empty strip passing the test.
    public static float LowestVisibleItemScreenY {get;internal set;}=-1;
    public static float LowestContainerScreenY {get;internal set;}=-1;
    public static float MaximumLiftScreenPixels {get;internal set;}

    // Call after Canvas.ForceUpdateCanvases, including after a probe switches overlay canvases
    // to camera mode. This reports the actual capture geometry rather than cached layout values.
    public static string[] DescribeItems()
    {
        var output=new List<string>();
        var hud=UiManager.Instance?.hud;
        if(hud==null)return new[]{"ItemsHud diagnostics: native HUD missing"};
        foreach(var item in Resources.FindObjectsOfTypeAll<ItemsHudElementPrefab>())
        {
            if(item==null || item.icon==null || !item.transform.IsChildOf(hud.transform))continue;
            var icon=item.icon;
            var rect=icon.GetComponent<RectTransform>();
            if(rect==null)continue;
            var camera=NativeItemLayout.FindCanvasCamera(rect);
            var bounds=NativeItemLayout.ScreenBounds(rect,camera);
            var renderer=icon.canvasRenderer;
            var path=PathOf(item.transform);
            output.Add($"Item icon {path}: active={icon.gameObject.activeInHierarchy},enabled={icon.enabled},renderEligible={NativeItemLayout.Renderable(icon,camera)},texture={(icon.texture!=null ? icon.texture.name : "null")},colorAlpha={icon.color.a:F3},rendererAlpha={(renderer!=null ? renderer.GetAlpha() : -1):F3},inheritedAlpha={(renderer!=null ? renderer.GetInheritedAlpha() : -1):F3},cull={(renderer!=null && renderer.cull)},screen=[{bounds.xMin:F1},{bounds.yMin:F1}..{bounds.xMax:F1},{bounds.yMax:F1}],screenSize={Screen.width}x{Screen.height},cameraViewport={(camera!=null ? camera.pixelRect.ToString() : "overlay")},rect={rect.rect},anchor={rect.anchoredPosition},camera={(camera!=null ? camera.name : "overlay")}");
            for(var ancestor=rect.transform;ancestor!=null;ancestor=ancestor.parent)
            {
                var group=ancestor.GetComponent<CanvasGroup>();
                var canvas=ancestor.GetComponent<Canvas>();
                var mask=ancestor.GetComponent<RectMask2D>();
                var graphicMask=ancestor.GetComponent<Mask>();
                var raw=ancestor.GetComponent<RawImage>();
                string detail="";
                if(group!=null)detail+=$" CanvasGroup(enabled={group.enabled},alpha={group.alpha:F3},ignoreParents={group.ignoreParentGroups})";
                if(canvas!=null)detail+=$" Canvas(enabled={canvas.enabled},root={canvas.isRootCanvas},mode={canvas.renderMode},order={canvas.sortingOrder},override={canvas.overrideSorting},scale={canvas.scaleFactor:F3},worldCamera={(canvas.worldCamera!=null ? canvas.worldCamera.name : "null")})";
                if(mask!=null)detail+=$" RectMask2D(enabled={mask.enabled})";
                if(graphicMask!=null)detail+=$" Mask(enabled={graphicMask.enabled},showGraphic={graphicMask.showMaskGraphic})";
                if(raw!=null && raw!=icon)detail+=$" parentRawImage(enabled={raw.enabled},alpha={raw.color.a:F3},texture={(raw.texture!=null ? raw.texture.name : "null")})";
                if(detail.Length>0)
                {
                    var ancestorRect=ancestor.GetComponent<RectTransform>();
                    if(ancestorRect!=null){var a=NativeItemLayout.ScreenBounds(ancestorRect,camera);detail+=$" screen=[{a.xMin:F1},{a.yMin:F1}..{a.xMax:F1},{a.yMax:F1}]";}
                    output.Add("  "+PathOf(ancestor)+detail);
                }
            }
        }
        if(output.Count==0)output.Add("ItemsHud diagnostics: no real item icon components under native HUD");
        return output.ToArray();
    }
    static string PathOf(Transform node)
    {
        string path="";
        for(var current=node;current!=null;current=current.parent)path=current.name+"["+current.GetSiblingIndex()+"]"+(path.Length>0 ? "/"+path : "");
        return path;
    }
}
