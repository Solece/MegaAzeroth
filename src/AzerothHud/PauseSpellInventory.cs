using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Il2CppInterop.Runtime;

namespace AzerothHud;

// Replaces only PauseUi.inventory.weaponParent visuals. Native tomes, items and event handlers stay native.
sealed class PauseSpellInventory
{
    sealed class SavedGraphic { public Graphic Graphic;public bool Enabled; }
    sealed class Cell { public GameObject Root;public RawImage Icon;public TextMeshProUGUI Rank;public ToolTipObject Tooltip;public RectTransform Rect; }
    readonly Dictionary<int,SavedGraphic> masked=new();
    readonly List<Cell> cells=new();
    Transform nativeRow;
    GameObject root;
    RectTransform content,viewport;
    ScrollRect scroll;
    TMP_FontAsset font;
    Material material;
    string[] ids=Array.Empty<string>(),names=Array.Empty<string>(),descriptions=Array.Empty<string>();
    int[] ranks=Array.Empty<int>();
    Func<string,Texture2D> load;
    bool visible;

    public void Refresh(bool paused,bool mage,TMP_FontAsset gameFont,Material ownedMaterial,string[] spellIds,int[] spellRanks,string[] spellNames,string[] spellDescriptions,string[] icons,Func<string,Texture2D> textureLoader)
    {
        if(!paused || !mage){Restore();return;}
        var inventory=UiManager.Instance?.pause?.inventory;
        if(inventory==null || inventory.weaponParent==null || !inventory.gameObject.activeInHierarchy){Restore();return;}
        RefreshRow(inventory.weaponParent,gameFont,ownedMaterial,spellIds,spellRanks,spellNames,spellDescriptions,icons,textureLoader,true);
        PauseInventoryDiagnostics.NativeTomeParentUnchanged=inventory.tomeParent!=null;
        PauseInventoryDiagnostics.NativeItemParentUnchanged=inventory.itemParent!=null;
    }
    bool reportPause;
    public void RefreshRow(Transform row,TMP_FontAsset gameFont,Material ownedMaterial,string[] spellIds,int[] spellRanks,string[] spellNames,string[] spellDescriptions,string[] icons,Func<string,Texture2D> textureLoader,bool pauseDiagnostics=false)
    {
        if(row==null || !row.gameObject.activeInHierarchy){Restore();return;}
        reportPause=pauseDiagnostics;
        var nativeRect=row.GetComponent<RectTransform>();
        if(reportPause && nativeRect!=null)PauseInventoryDiagnostics.SourceRowBounds=NativeItemLayout.ScreenBounds(nativeRect,NativeItemLayout.FindCanvasCamera(nativeRect)).ToString();
        if(root==null || nativeRow!=row)
        {
            Dispose();nativeRow=row;font=gameFont;material=ownedMaterial;load=textureLoader;
            Build(row.GetComponent<RectTransform>());
        }
        if(root==null)return;
        ids=spellIds;names=spellNames;descriptions=spellDescriptions;ranks=spellRanks;
        visible=true;root.SetActive(true);
        foreach(var graphic in row.GetComponentsInChildren<Graphic>(true))
            if(graphic!=null && !masked.ContainsKey(graphic.GetInstanceID()))masked[graphic.GetInstanceID()]=new SavedGraphic{Graphic=graphic,Enabled=graphic.enabled};
        Enforce();
        Layout();
        for(int i=0;i<cells.Count;i++)
        {
            bool learned=i<ids.Length && i<ranks.Length && ranks[i]>0;
            var cell=cells[i];cell.Root.SetActive(learned);
            if(!learned)continue;
            string stem=i<icons.Length?icons[i]:"";
            cell.Icon.texture=string.IsNullOrWhiteSpace(stem)?null:load("Interface/Icons/"+stem+".png");
            cell.Rank.text="ур. "+ranks[i];
            string title=i<names.Length&&!string.IsNullOrWhiteSpace(names[i])?names[i]:ids[i];
            string body=i<descriptions.Length?descriptions[i]:"";
            cell.Tooltip.text=$"{title} — ранг {ranks[i]}\n{body}";
        }
        if(reportPause){PauseInventoryDiagnostics.Visible=true;PauseInventoryDiagnostics.LearnedSpellCount=Math.Min(cells.Count,ids.Length);PauseInventoryDiagnostics.NativeWeaponGraphicsHidden=masked.Count;}
    }
    void Build(RectTransform source)
    {
        if(source==null || source.parent==null || font==null)return;
        var row=Rect("AzerothPauseSpells",source.parent);
        root=row.gameObject;row.anchorMin=source.anchorMin;row.anchorMax=source.anchorMax;row.pivot=source.pivot;
        row.anchoredPosition=source.anchoredPosition;row.sizeDelta=source.sizeDelta;row.localScale=source.localScale;row.localRotation=source.localRotation;
        // DeathStatsUi uses a layout-only weapon Transform whose own rectangle can be zero-height.
        // Its native icons still have real geometry. Recover that footprint instead of clipping
        // thirteen live cells inside the controller's empty rectangle.
        if(source.rect.height<40 || source.rect.width<120)FitNativeIconFootprint(row,source);
        row.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
        root.AddComponent<RawImage>().color=new Color(.035f,.045f,.04f,.98f);
        scroll=root.AddComponent<ScrollRect>();scroll.horizontal=false;scroll.vertical=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=40;scroll.inertia=false;
        viewport=Rect("Viewport",row);viewport.anchorMin=Vector2.zero;viewport.anchorMax=Vector2.one;viewport.offsetMin=new Vector2(1,1);viewport.offsetMax=new Vector2(-1,-1);
        viewport.gameObject.AddComponent<RectMask2D>();
        var hit=viewport.gameObject.AddComponent<RawImage>();hit.color=new Color(0,0,0,0.001f);hit.raycastTarget=true;
        content=Rect("Spells",viewport);content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(0,1);content.pivot=new Vector2(0,1);
        scroll.viewport=viewport;scroll.content=content;
        for(int i=0;i<13;i++)
        {
            var rect=Rect("LearnedSpell"+i,content);rect.anchorMin=new Vector2(0,1);rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);
            var icon=rect.gameObject.AddComponent<RawImage>();icon.raycastTarget=true;icon.color=Color.white;
            var tooltip=rect.gameObject.AddComponent<ToolTipObject>();
            var textRect=Rect("Rank",rect);textRect.anchorMin=new Vector2(0,0);textRect.anchorMax=new Vector2(1,0);textRect.pivot=new Vector2(.5f,0);textRect.sizeDelta=new Vector2(0,20);textRect.anchoredPosition=new Vector2(0,-1);
            var rank=textRect.gameObject.AddComponent<TextMeshProUGUI>();rank.font=font;rank.fontSharedMaterial=material;rank.fontSize=16;rank.alignment=TextAlignmentOptions.Bottom;rank.color=Color.white;rank.raycastTarget=false;rank.richText=false;
            cells.Add(new Cell{Root=rect.gameObject,Rect=rect,Icon=icon,Rank=rank,Tooltip=tooltip});
        }
        root.transform.SetSiblingIndex(source.GetSiblingIndex()+1);
        scroll.verticalNormalizedPosition=1;
    }
    void Layout()
    {
        if(viewport==null || content==null)return;
        float width=Mathf.Max(24,viewport.rect.width),height=Mathf.Max(24,viewport.rect.height);
        int learned=Math.Max(1,Math.Min(13,ids.Length));
        float spacing=Mathf.Min(7,width*.009f),rankHeight=Mathf.Clamp(height*.28f,10,20);
        float cell=Mathf.Max(8,Mathf.Min(60,Mathf.Min((width-6-spacing*(learned-1))/learned,height-rankHeight-5)));
        content.sizeDelta=new Vector2(width,height);content.anchoredPosition=Vector2.zero;
        scroll.horizontal=false;scroll.vertical=false;scroll.velocity=Vector2.zero;
        for(int i=0;i<cells.Count;i++)
        {
            cells[i].Rect.sizeDelta=new Vector2(cell,cell);
            cells[i].Rect.anchoredPosition=new Vector2(3+i*(cell+spacing),-2);
            cells[i].Rank.fontSize=Mathf.Clamp(cell*.32f,8,14);
            cells[i].Rank.rectTransform.sizeDelta=new Vector2(0,rankHeight);
        }
        if(reportPause){PauseInventoryDiagnostics.Columns=learned;PauseInventoryDiagnostics.Rows=1;PauseInventoryDiagnostics.ScrollPosition=0;PauseInventoryDiagnostics.ViewportBounds=NativeItemLayout.ScreenBounds(viewport,NativeItemLayout.FindCanvasCamera(viewport)).ToString();}
    }
    public void Enforce(){if(!visible)return;foreach(var saved in masked.Values)if(saved.Graphic!=null)saved.Graphic.enabled=false;}
    public void Restore()
    {
        visible=false;if(root!=null)root.SetActive(false);
        foreach(var saved in masked.Values)if(saved.Graphic!=null)saved.Graphic.enabled=saved.Enabled;
        masked.Clear();if(reportPause){PauseInventoryDiagnostics.Visible=false;PauseInventoryDiagnostics.NativeWeaponGraphicsHidden=0;}
    }
    public void Dispose(){Restore();if(root!=null)UnityEngine.Object.Destroy(root);root=null;nativeRow=null;viewport=null;content=null;scroll=null;cells.Clear();}
    public void SetScroll(float value){Layout();}
    public void PlaceAboveBooks(Transform books,Transform statsWindow)
    {
        if(root==null || books==null || statsWindow==null)return;
        var parent=statsWindow.GetComponent<RectTransform>();if(parent==null)return;
        var camera=NativeItemLayout.FindCanvasCamera(books);
        float left=float.PositiveInfinity,top=float.NegativeInfinity,iconHeight=0;
        foreach(var native in books.GetComponentsInChildren<InventoryItemPrefabUI>(true))
        {
            if(native==null || native.icon==null || !native.icon.gameObject.activeInHierarchy)continue;
            var rect=native.icon.GetComponent<RectTransform>();if(rect==null)continue;
            var bounds=NativeItemLayout.ScreenBounds(rect,camera);
            if(bounds.width<1 || bounds.height<1)continue;
            left=Mathf.Min(left,bounds.xMin);top=Mathf.Max(top,bounds.yMax);iconHeight=Mathf.Max(iconHeight,bounds.height);
        }
        if(float.IsPositiveInfinity(left))
        {
            var rect=books.GetComponent<RectTransform>();if(rect==null)return;
            var bounds=NativeItemLayout.ScreenBounds(rect,camera);left=bounds.xMin;top=bounds.yMax;
        }
        float scale=Screen.height/1080f;
        float height=Mathf.Max(64*scale,iconHeight+12*scale),gap=8*scale;
        float topOfSlots=top+height+gap;
        float width=Screen.width*.31f;
        // Locate a real central-panel ancestor, excluding zero-sized row controllers/fullscreen roots.
        for(var node=books.parent;node!=null;node=node.parent)
        {
            var rect=node.GetComponent<RectTransform>();if(rect==null)continue;
            var bounds=NativeItemLayout.ScreenBounds(rect,camera);
            if(bounds.width<Screen.width*.20f || bounds.width>Screen.width*.45f)continue;
            if(bounds.xMax>left+120*scale){width=Mathf.Min(width,bounds.xMax-left-18*scale);break;}
        }
        width=Mathf.Max(180*scale,width);
        if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,new Vector2(left,topOfSlots),camera,out var upperLeft) ||
           !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,new Vector2(left+width,topOfSlots-height),camera,out var lowerRight))return;
        var own=root.GetComponent<RectTransform>();
        own.SetParent(parent,false);own.anchorMin=new Vector2(.5f,.5f);own.anchorMax=own.anchorMin;own.pivot=new Vector2(0,1);
        own.localScale=Vector3.one;own.localRotation=Quaternion.identity;own.localPosition=new Vector3(upperLeft.x,upperLeft.y,0);
        own.sizeDelta=new Vector2(Mathf.Abs(lowerRight.x-upperLeft.x),Mathf.Abs(upperLeft.y-lowerRight.y));
        own.SetAsLastSibling();Canvas.ForceUpdateCanvases();Layout();
        PostCombatDiagnostics.ResultViewport=$"left={left:F1},top={Screen.height-topOfSlots:F1},width={width:F1},height={height:F1},bookLeft={left:F1},bookTop={Screen.height-top:F1}";
    }
    public bool ShowTooltip(int index)
    {
        if(!visible || index<0 || index>=ids.Length || index>=cells.Count)return false;
        foreach(var cell in cells)if(cell.Tooltip!=null)cell.Tooltip.OnPointerExit(null);
        // Use the real native hover pipeline and its correct tooltip manager, not the first
        // inactive TooltipNew returned by Resources. Native pointer-enter does not read eventData.
        cells[index].Tooltip.OnPointerEnter(null);return true;
    }
    static void FitNativeIconFootprint(RectTransform target,RectTransform source)
    {
        var parent=source.parent.GetComponent<RectTransform>();
        if(parent==null)return;
        float left=float.PositiveInfinity,top=float.NegativeInfinity,bottom=float.PositiveInfinity,right=float.NegativeInfinity;
        foreach(var native in source.GetComponentsInChildren<InventoryItemPrefabUI>(true))
        {
            if(native==null || native.icon==null)continue;
            var icon=native.icon.GetComponent<RectTransform>();if(icon==null)continue;
            var a=parent.InverseTransformPoint(icon.TransformPoint(new Vector3(icon.rect.xMin,icon.rect.yMin,0)));
            var b=parent.InverseTransformPoint(icon.TransformPoint(new Vector3(icon.rect.xMax,icon.rect.yMax,0)));
            left=Mathf.Min(left,Mathf.Min(a.x,b.x));right=Mathf.Max(right,Mathf.Max(a.x,b.x));
            bottom=Mathf.Min(bottom,Mathf.Min(a.y,b.y));top=Mathf.Max(top,Mathf.Max(a.y,b.y));
        }
        if(float.IsPositiveInfinity(left))
        {
            var anchor=parent.InverseTransformPoint(source.position);left=anchor.x;top=anchor.y;
            bottom=top-72;right=left+Mathf.Max(240,parent.rect.width-20);
        }
        target.anchorMin=new Vector2(.5f,.5f);target.anchorMax=target.anchorMin;target.pivot=new Vector2(0,1);
        target.localScale=Vector3.one;target.localRotation=Quaternion.identity;
        target.localPosition=new Vector3(left,top,0);
        target.sizeDelta=new Vector2(Mathf.Max(120,parent.rect.xMax-left-10),Mathf.Max(64,top-bottom+12));
    }
    static RectTransform Rect(string name,Transform parent)
    {
        var go=new GameObject(name,new[]{Il2CppType.Of<RectTransform>()});go.transform.SetParent(parent,false);return go.GetComponent<RectTransform>();
    }
    public string[] Describe()=>new[]{$"spellPanel scrolling=False,scrollbar=False,rootRect={root?.GetComponent<RectTransform>()?.rect},viewportRect={viewport?.rect},contentRect={content?.rect},screen={(viewport!=null?NativeItemLayout.ScreenBounds(viewport,NativeItemLayout.FindCanvasCamera(viewport)).ToString():"none")}"}.Concat(cells.Select((cell,i)=>$"spell[{i}] active={cell.Root!=null&&cell.Root.activeInHierarchy}, id={(i<ids.Length?ids[i]:"none")}, rank={(i<ranks.Length?ranks[i]:0)}, icon={(cell.Icon.texture!=null?cell.Icon.texture.name:"null")}, description={cell.Tooltip.text}")).ToArray();
}

public static class PauseInventoryDiagnostics
{
    public static bool Visible{get;internal set;}
    public static int LearnedSpellCount{get;internal set;}
    public static int NativeWeaponGraphicsHidden{get;internal set;}
    public static bool NativeTomeParentUnchanged{get;internal set;}
    public static bool NativeItemParentUnchanged{get;internal set;}
    public static int Columns{get;internal set;}
    public static int Rows{get;internal set;}
    public static float ScrollPosition{get;internal set;}
    public static string ViewportBounds{get;internal set;}="";
    public static string SourceRowBounds{get;internal set;}="";
}
