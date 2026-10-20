using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using AzerothAbilities;
using Assets.Scripts.Utility;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace MageTalents;

// Verified original Rust talent window:1600x860 logical canvas, three concurrent
// 360x520 trees,46px cells,90x(520/7) grid and310px right sidebar.
public sealed class TalentPanel:MonoBehaviour
{
    internal static TalentPanel Current;
    public TalentPanel(IntPtr pointer):base(pointer){Current=this;}
    GameObject root,panel,treeLayer,tooltip;
    TextMeshProUGUI title,points,detail,hint,note;
    TMP_FontAsset font;Material material;
    RawImage portrait;
    readonly List<RectTransform> treeRects=new();
    readonly List<TextMeshProUGUI> treePoints=new();
    RectTransform sidebarRect;
    readonly Dictionary<string,Texture2D> textures=new();
    readonly List<UnityAction> clicks=new();
    readonly List<UnityAction<BaseEventData>> pointers=new();
    readonly List<Node> data=new();readonly List<View> views=new();readonly List<Tree> trees=new();readonly List<Tab> tabs=new();readonly List<Link> links=new();
    string classId="mage",treeId="",selected="",lastCatalog="",assetRoot;
    bool opened,failed,oldNativePaused;float next,oldTimeScale;CursorLockMode oldCursor;bool oldCursorVisible;
    sealed class Node{internal string Id,Name,Icon,Tree,TreeId,Prerequisite,Description;internal int Tier,Column,MaxRank,RequiredPoints;}
    sealed class Tree{internal string ClassId,Id,Name,Icon,Background;}
    sealed class View{internal Node Node;internal RawImage Icon,Frame;internal TextMeshProUGUI Rank;internal Button Button;}
    sealed class Tab{internal Tree Tree;internal GameObject Root;internal RawImage[] Parts;internal TextMeshProUGUI Name;}
    sealed class Link{internal RawImage Image;internal Node Target;}
    static readonly Color Gold=new(.973f,.824f,.416f),Green=new(.302f,1,.416f),Gray=new(.169f,.129f,.071f);
    void Update()
    {
        if(failed||Plugin.Catalog==null)return;
        try
        {
            if(Input.GetKeyDown(KeyCode.F6)&&AbilityRuntime.Active)Toggle();if(opened&&Input.GetKeyDown(KeyCode.Escape))Close();
            if(Time.unscaledTime<next)return;next=Time.unscaledTime+.15f;
            if(root==null)
            {
                foreach(var t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                {
                    if(t==null||t.font==null||t.fontSharedMaterial==null)continue;font=t.font;material=new Material(t.fontSharedMaterial);
                    foreach(var key in new[]{"_OutlineWidth","_OutlineSoftness","_FaceDilate"})if(material.HasProperty(key))material.SetFloat(key,0);
                    material.DisableKeyword("UNDERLAY_ON");material.DisableKeyword("UNDERLAY_INNER");break;
                }
                if(font!=null)Build();
            }
            if(root==null)return;hint.gameObject.SetActive(AbilityRuntime.Active&&!opened);if(opened)Refresh();
        }
        catch(Exception ex){Close();failed=true;Plugin.Current.Log.LogError("Source talent tree UI disabled: "+ex);}
    }
    [HideFromIl2Cpp] internal bool SetProbeVisible(bool visible){if(root==null)return false;if(opened!=visible)Toggle();return opened==visible;}
    [HideFromIl2Cpp] internal bool ProbeTree(int index){var all=ClassTrees();if(index<0||index>=all.Count)return false;SelectTree(all[index].Id);return true;}
    [HideFromIl2Cpp] internal bool ProbeHover(string id){if(!views.Any(v=>v.Node.Id==id))return false;selected=id;Detail();return true;}
    [HideFromIl2Cpp] internal void ProbeScroll(float value){/* Original Rust view shows all seven tiers concurrently; retained probe ABI. */}
    [HideFromIl2Cpp] internal bool ProbeClick(string id,bool refund)
    {
        var view=views.FirstOrDefault(v=>v.Node.Id==id);if(view==null)return false;selected=id;Detail();
        var button=refund?tooltip.transform.Find("Refund")?.GetComponent<Button>():view.Button;
        if(button==null||!button.interactable)return false;button.onClick.Invoke();return true;
    }
    [HideFromIl2Cpp] internal bool ProbeRightClick(string id)
    {
        var view=views.FirstOrDefault(v=>v.Node.Id==id);if(view==null)return false;
        var trigger=view.Button.GetComponent<EventTrigger>();if(trigger==null)return false;
        var pointer=new PointerEventData(EventSystem.current);pointer.button=PointerEventData.InputButton.Right;
        foreach(var entry in trigger.triggers)if(entry.eventID==EventTriggerType.PointerClick){entry.callback.Invoke(pointer);return true;}
        return false;
    }
    [HideFromIl2Cpp] internal bool ProbeReset(){var button=panel?.transform.Find("Reset")?.GetComponent<Button>();if(button==null)return false;button.onClick.Invoke();return true;}
    [HideFromIl2Cpp] void Toggle()
    {
        if(root==null)return;if(opened){Close();return;}ReadCatalog();opened=true;
        oldTimeScale=Time.timeScale;oldCursor=Cursor.lockState;oldCursorVisible=Cursor.visible;oldNativePaused=MyTime.paused;
        if(!oldNativePaused)MyTime.Pause();Time.timeScale=0;
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;panel.SetActive(true);Refresh();
    }
    [HideFromIl2Cpp] void Close()
    {
        if(!opened)return;opened=false;
        if(!oldNativePaused && MyTime.paused && UiManager.Instance?.pause?.IsPaused()!=true && !LevelupScreen.isLevelingUp && GameManager.Instance?.isGameOver!=true)MyTime.Unpause();
        Time.timeScale=oldTimeScale;Cursor.lockState=oldCursor;Cursor.visible=oldCursorVisible;
        if(panel!=null)panel.SetActive(false);TalentTreeDiagnostics.Visible=false;
    }
    void OnDestroy(){Close();if(root!=null)Object.Destroy(root);foreach(var t in textures.Values)if(t!=null)Object.Destroy(t);if(material!=null)Object.Destroy(material);textures.Clear();clicks.Clear();pointers.Clear();if(Current==this)Current=null;}
    [HideFromIl2Cpp] public static void Shutdown(){var panel=Current;if(panel!=null){panel.enabled=false;panel.OnDestroy();}}
    [HideFromIl2Cpp] static GameObject Go(string name,Transform parent,float x,float y,float w,float h)
    {
        var go=new GameObject(name,new[]{Il2CppType.Of<RectTransform>()});if(parent!=null)go.transform.SetParent(parent,false);
        var r=go.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);r.localScale=Vector3.one;return go;
    }
    [HideFromIl2Cpp] Texture2D Tex(string path)
    {
        if(textures.TryGetValue(path,out var cached))return cached;
        var raw=Path.Combine(assetRoot,path.Replace('/',Path.DirectorySeparatorChar)+".rgba");var sizeFile=Path.ChangeExtension(raw,".size");
        if(!File.Exists(raw)||!File.Exists(sizeFile)){textures[path]=null;TalentTreeDiagnostics.MissingTextures++;Plugin.Current.Log.LogWarning("Missing source talent art: "+raw);return null;}
        var size=File.ReadAllText(sizeFile).Split(' ',StringSplitOptions.RemoveEmptyEntries);int w=int.Parse(size[0]),h=int.Parse(size[1]);var bytes=File.ReadAllBytes(raw);
        if(w<=0||h<=0||bytes.Length!=checked(w*h*4))throw new InvalidDataException(raw);
        var tex=new Texture2D(w,h,TextureFormat.RGBA32,false);tex.name=Path.GetFileName(path);tex.hideFlags=HideFlags.DontUnloadUnusedAsset;
        var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);try{tex.LoadRawTextureData(pin.AddrOfPinnedObject(),bytes.Length);tex.Apply(false,false);}finally{pin.Free();}
        textures[path]=tex;return tex;
    }
    [HideFromIl2Cpp] RawImage Art(Transform parent,string name,string path,float x,float y,float w,float h,Rect? uv=null)
    {var image=Go(name,parent,x,y,w,h).AddComponent<RawImage>();image.texture=path==null?null:Tex(path);image.color=Color.white;image.raycastTarget=false;image.uvRect=uv??new Rect(0,0,1,1);return image;}
    [HideFromIl2Cpp] TextMeshProUGUI Text(Transform parent,string name,string value,float x,float y,float w,float h,float size=12)
    {var t=Go(name,parent,x,y,w,h).AddComponent<TextMeshProUGUI>();t.font=font;t.fontSharedMaterial=material;t.fontSize=size;t.richText=false;t.raycastTarget=false;t.text=value;t.color=Gold;t.alignment=TextAlignmentOptions.MidlineLeft;return t;}
    [HideFromIl2Cpp] Button Click(GameObject go,Action callback)
    {
        var button=go.AddComponent<Button>();var graphic=go.GetComponent<Graphic>();if(graphic==null){var image=go.AddComponent<Image>();image.color=Color.clear;graphic=image;}
        graphic.raycastTarget=true;button.targetGraphic=graphic;button.transition=Selectable.Transition.None;var a=(UnityAction)callback;clicks.Add(a);button.onClick.AddListener(a);return button;
    }
    [HideFromIl2Cpp] void BindPointer(GameObject go,EventTriggerType kind,Action<BaseEventData> callback)
    {var trigger=go.GetComponent<EventTrigger>()??go.AddComponent<EventTrigger>();var entry=new EventTrigger.Entry();entry.eventID=kind;var a=(UnityAction<BaseEventData>)callback;pointers.Add(a);entry.callback.AddListener(a);trigger.triggers.Add(entry);}
    [HideFromIl2Cpp] Button Button(Transform parent,string name,string value,float x,float y,float width,Action callback)
    {
        var go=Go(name,parent,x,y,width,22);const string path="Interface/Buttons/UI-Panel-Button-Up";
        // UIPanelButtonTemplate2: preserve12px endcaps. XML top-origin0..0.6875
        // becomes Unity bottom-origin0.3125..1 after offline bottom-up RGBA upload.
        Art(go.transform,"Left",path,0,0,12,22,new Rect(0,.3125f,.09375f,.6875f));
        Art(go.transform,"Middle",path,12,0,width-24,22,new Rect(.09375f,.3125f,.4375f,.6875f));
        Art(go.transform,"Right",path,width-12,0,12,22,new Rect(.53125f,.3125f,.09375f,.6875f));
        var label=Text(go.transform,"Label",value,3,0,width-6,22,11);label.alignment=TextAlignmentOptions.Center;return Click(go,callback);
    }
    [HideFromIl2Cpp] static bool Prop(JsonElement row,string key,out JsonElement value)
    {foreach(var p in row.EnumerateObject())if(p.Name.Equals(key,StringComparison.OrdinalIgnoreCase)){value=p.Value;return true;}value=default;return false;}
    [HideFromIl2Cpp] static string S(JsonElement row,string key)=>Prop(row,key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():"";
    [HideFromIl2Cpp] static int N(JsonElement row,string key)=>Prop(row,key,out var value)&&value.TryGetInt32(out var n)?n:0;
    [HideFromIl2Cpp] List<Tree> ClassTrees()=>trees.Where(t=>t.ClassId==classId).ToList();
    [HideFromIl2Cpp] void Build()
    {
        assetRoot=Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location),"assets");
        using(var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(assetRoot,"talent-art.json"))))
            foreach(var t in document.RootElement.GetProperty("trees").EnumerateArray())trees.Add(new Tree{ClassId=S(t,"classId"),Id=S(t,"treeId"),Name=S(t,"name"),Icon=S(t,"icon"),Background=S(t,"background")});
        root=Go("AzerothSourceTalentCanvas",null,0,0,1600,860);Object.DontDestroyOnLoad(root);
        var canvas=root.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=5300;
        var scaler=root.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,860);scaler.matchWidthOrHeight=.5f;root.AddComponent<GraphicRaycaster>();
        hint=Text(root.transform,"F6Hint","F6 — таланты",1180,10,400,32,20);hint.alignment=TextAlignmentOptions.Right;
        panel=Go("SourceTalentView1600x860",root.transform,0,0,1600,860);
        // Original ViewRoot uses a full-screen dimmer. Main content is a row of
        // three380px padded tree cards and the310px fixed-width source sidebar.
        var dim=Art(panel.transform,"ViewDimmer",null,-1600,-860,4800,2580);dim.color=new Color(0,0,0,.72f);dim.raycastTarget=true;
        title=Text(panel.transform,"Title","Таланты",75,38,1370,50,29);
        Click(Art(panel.transform,"Close","Interface/Buttons/UI-Panel-MinimizeButton-Up",1480,38,38,38).gameObject,Close);
        treeLayer=Go("ThreeTreeRow",panel.transform,75,138,1140,610);
        tooltip=Go("SourceSidebar310",panel.transform,1215,138,310,610);sidebarRect=tooltip.GetComponent<RectTransform>();
        var bg=Art(tooltip.transform,"Background",null,0,0,310,610);bg.color=new Color(.045f,.034f,.022f,.98f);Border(tooltip.transform,310,610);
        portrait=Art(tooltip.transform,"Portrait",null,16,12,54,54);
        points=Text(tooltip.transform,"Points","",82,12,212,66,17);points.alignment=TextAlignmentOptions.TopLeft;
        detail=Text(tooltip.transform,"Details","",16,96,278,365,17);detail.color=new Color(1,.94f,.8f);detail.alignment=TextAlignmentOptions.TopLeft;
        Button(tooltip.transform,"Learn","Изучить +1",16,478,132,()=>{if(selected.Length>0)TalentBridge.Spend(selected);Refresh();});
        Button(tooltip.transform,"Refund","Вернуть −1",162,478,132,()=>{if(selected.Length>0)TalentBridge.Refund(selected);Refresh();});
        Text(tooltip.transform,"MouseHint","ЛКМ — изучить · ПКМ — вернуть",16,512,278,26,14);
        Button(panel.transform,"Reset","Сбросить таланты",1231,696,278,()=>{TalentBridge.Reset();Refresh();});
        note=Text(panel.transform,"Note","",75,765,1450,54,17);note.color=new Color(.82f,.75f,.58f);
        ReadCatalog();panel.SetActive(false);Plugin.Current.Log.LogInfo("Original Rust source geometry talent view built:1600x860,three360x520trees,46px cells,310px sidebar; allocation remains TalentBridge-owned.");
    }
    [HideFromIl2Cpp] void Border(Transform parent,float w,float h)
    {
        const string path="Interface/Tooltips/UI-Tooltip-Border";
        Art(parent,"Left",path,0,8,8,h-16,new Rect(0,0,.125f,1));Art(parent,"Right",path,w-8,8,8,h-16,new Rect(.125f,0,.125f,1));
        Art(parent,"Top",path,8,0,w-16,8,new Rect(.25f,0,.125f,1));Art(parent,"Bottom",path,8,h-8,w-16,8,new Rect(.375f,0,.125f,1));
        for(int i=0;i<4;i++)Art(parent,"Corner"+i,path,i%2==0?0:w-8,i<2?0:h-8,8,8,new Rect(.5f+i*.125f,0,.125f,1));
    }
    [HideFromIl2Cpp] void ReadCatalog()
    {
        var json=TalentBridge.CatalogJson;if(json==lastCatalog)return;lastCatalog=json;using var document=JsonDocument.Parse(json);var catalog=document.RootElement;
        classId=S(catalog,"classId");if(classId.Length==0)classId="mage";data.Clear();
        if(Prop(catalog,"talents",out var all))foreach(var t in all.EnumerateArray())data.Add(new Node{Id=S(t,"id"),Name=S(t,"name"),Icon=S(t,"icon"),Tree=S(t,"tree"),TreeId=S(t,"treeId"),Prerequisite=S(t,"prerequisite"),Description=S(t,"description"),Tier=N(t,"tier"),Column=N(t,"column"),MaxRank=N(t,"maxRank"),RequiredPoints=N(t,"requiredTreePoints")});
        var available=ClassTrees();if(available.Count!=3)throw new InvalidDataException("Expected three source backgrounds for "+classId);
        foreach(var node in data)if(node.TreeId.Length==0)node.TreeId=available.FirstOrDefault(t=>t.Name==node.Tree)?.Id??"";
        if(!available.Any(t=>t.Id==treeId))treeId=available[0].Id;BuildTabs();SelectTree(treeId);
    }
    [HideFromIl2Cpp] void BuildTabs()
    {
        views.Clear();links.Clear();tabs.Clear();treeRects.Clear();treePoints.Clear();
        foreach(var child in treeLayer.GetComponentsInChildren<RectTransform>(true))if(child.parent==treeLayer.transform)Object.Destroy(child.gameObject);
        var allTrees=ClassTrees();
        for(int index=0;index<allTrees.Count;index++)
        {
            var tree=allTrees[index];var card=Go("TreeCard_"+tree.Id,treeLayer.transform,index*380,0,380,610);
            var back=Art(card.transform,"CardBackground",null,0,0,380,610);back.color=new Color(.045f,.034f,.022f,.98f);Border(card.transform,380,610);
            Art(card.transform,"TreeIcon","Interface/Icons/"+tree.Icon,16,13,34,34);
            var name=Text(card.transform,"TreeName",tree.Name,62,10,302,44,23);
            var tab=new Tab{Tree=tree,Root=card,Name=name,Parts=Array.Empty<RawImage>()};tabs.Add(tab);
            var hit=Go("SelectTree",card.transform,0,0,380,56);Click(hit,()=>SelectTree(tree.Id));
            treePoints.Add(Text(card.transform,"TreePoints","",16,579,348,24,17));
            var viewport=Go("SourceTree360x520",card.transform,10,54,360,520);viewport.AddComponent<RectMask2D>();treeRects.Add(viewport.GetComponent<RectTransform>());
            string prefix="Interface/TalentFrame/"+tree.Background+"-";
            const float scale=1.5805470943450928f,xOffset=-57.082061767578125f,overlap=1.5f;
            string[] names={"TopLeft","TopRight","BottomLeft","BottomRight"};float[] xs={0,256,0,256},ys={0,0,256,256},ws={256,64,256,64},hs={256,256,128,128};
            for(int q=0;q<4;q++){var art=Art(viewport.transform,"Background"+names[q],prefix+names[q],xs[q]*scale+xOffset,ys[q]*scale,ws[q]*scale+overlap,hs[q]*scale+overlap);art.color=new Color(.75f,.72f,.68f,1);}
            var nodes=data.Where(n=>n.TreeId==tree.Id).ToList();
            foreach(var node in nodes){var from=nodes.FirstOrDefault(n=>n.Id==node.Prerequisite);if(from!=null)DrawLink(viewport.transform,from,node);}
            foreach(var node in nodes)
            {
                float x=(node.Column+.5f)*90-23,y=(node.Tier+.5f)*(520f/7)-23;
                var go=Go(node.Id,viewport.transform,x,y,46,46);var frame=Art(go.transform,"CellBorder2",null,0,0,46,46);
                Art(go.transform,"CellBacking",null,2,2,42,42).color=new Color(.02f,.018f,.012f,1);
                var icon=Art(go.transform,"Icon","Interface/Icons/"+node.Icon,2,2,42,42);
                var button=Click(go,()=>{selected=node.Id;treeId=node.TreeId;TalentBridge.Spend(node.Id);Refresh();});
                BindPointer(go,EventTriggerType.PointerEnter,_=>{selected=node.Id;treeId=node.TreeId;Detail();});
                BindPointer(go,EventTriggerType.PointerClick,e=>{if(e.TryCast<PointerEventData>()?.button==PointerEventData.InputButton.Right){selected=node.Id;treeId=node.TreeId;TalentBridge.Refund(node.Id);Refresh();}});
                var rankBack=Art(go.transform,"RankBacking",null,19,29,34,22);rankBack.color=new Color(.02f,.018f,.012f,.95f);
                var rank=Text(go.transform,"Rank","",20,28,34,23,15);rank.alignment=TextAlignmentOptions.Center;
                views.Add(new View{Node=node,Icon=icon,Frame=frame,Rank=rank,Button=button});
            }
        }
    }
    [HideFromIl2Cpp] void SelectTree(string id)
    {
        treeId=id;selected=data.FirstOrDefault(n=>n.TreeId==id)?.Id??"";Refresh();
    }
    [HideFromIl2Cpp] void DrawLink(Transform parent,Node from,Node to)
    {
        float x1=(from.Column+.5f)*90,y1=(from.Tier+.5f)*(520f/7),x2=(to.Column+.5f)*90,y2=(to.Tier+.5f)*(520f/7);
        // Original prerequisites use4px orthogonal connector segments. Draw before
        // cells, so endpoints remain underneath the actual46px source cells.
        if(Mathf.Abs(x2-x1)>.1f)links.Add(new Link{Image=Art(parent,"BranchH_"+to.Id,null,Mathf.Min(x1,x2),y1-2,Mathf.Abs(x2-x1)+2,4),Target=to});
        if(y2>y1)links.Add(new Link{Image=Art(parent,"BranchV_"+to.Id,null,x2-2,y1,4,y2-y1),Target=to});
    }
    [HideFromIl2Cpp] bool Can(string method,string id)
    {
        var api=typeof(TalentBridge).GetMethod(method,BindingFlags.Public|BindingFlags.Static);if(api!=null)return api.Invoke(null,new object[]{id}) is true;
        return method=="CanSpend"?Plugin.Saved.Clone().Spend(Plugin.Catalog,id):Plugin.Saved.Clone().Refund(Plugin.Catalog,id);
    }
    [HideFromIl2Cpp] void Refresh()
    {
        if(root==null)return;ReadCatalog();title.text="Таланты — "+ClassName(classId);hint.text="F6 — таланты: "+ClassName(classId);
        points.text=$"Свободно: {TalentBridge.AvailablePoints}\nПотрачено: {TalentBridge.SpentPoints}/{TalentBridge.EarnedPoints}";
        note.text=TalentBridge.PendingNextRun?"Изменения вступят в силу со следующего забега.":"ЛКМ — изучить. ПКМ — вернуть. Сброс бесплатный.";
        for(int i=0;i<tabs.Count;i++){var tab=tabs[i];tab.Name.color=tab.Tree.Id==treeId?Color.white:Gold;treePoints[i].text=$"Потрачено очков: {data.Where(n=>n.TreeId==tab.Tree.Id).Sum(n=>TalentBridge.Rank(n.Id))}";}
        foreach(var view in views)
        {
            int rank=TalentBridge.Rank(view.Node.Id);bool available=Can("CanSpend",view.Node.Id);view.Icon.color=rank>0||available?Color.white:new Color(.55f,.55f,.55f,1);
            view.Frame.color=rank==view.Node.MaxRank?Gold:available?Green:rank>0?Gold:Gray;view.Rank.text=$"{rank}/{view.Node.MaxRank}";view.Rank.color=rank==view.Node.MaxRank?Gold:rank>0||available?Green:Color.gray;
        }
        foreach(var link in links)link.Image.color=TalentBridge.Rank(link.Target.Prerequisite)>0?new Color(.8f,.69f,.3f,1):new Color(.25f,.23f,.19f,1);
        var portraitType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothMegabonk.MageVisuals.MageVisualBridge",false)).FirstOrDefault(t=>t!=null);
        var heroType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHeroes.HeroBridge",false)).FirstOrDefault(t=>t!=null);
        portrait.texture=heroType?.GetMethod("GetClassPortrait")?.Invoke(null,new object[]{classId}) as Texture??portraitType?.GetProperty("PortraitTexture")?.GetValue(null) as Texture??Tex("Interface/Icons/"+ClassTrees()[0].Icon);Detail();
        TalentTreeDiagnostics.Visible=opened;TalentTreeDiagnostics.ClassId=classId;TalentTreeDiagnostics.TreeId=treeId;TalentTreeDiagnostics.NodeCount=views.Count;TalentTreeDiagnostics.CatalogNodeCount=data.Count;
        TalentTreeDiagnostics.Background=ClassTrees().First(t=>t.Id==treeId).Background;TalentTreeDiagnostics.LoadedTextures=textures.Count;TalentTreeDiagnostics.ScrollPosition=1;
        TalentTreeDiagnostics.PreviousNativePaused=oldNativePaused;TalentTreeDiagnostics.PreviousUnityTimeScale=oldTimeScale;
        TalentTreeDiagnostics.FrameBounds=Bounds(panel.GetComponent<RectTransform>());TalentTreeDiagnostics.ViewportBounds=string.Join(";",treeRects.Select(Bounds));
        TalentTreeDiagnostics.TreeBounds=treeRects.Select(Bounds).ToArray();TalentTreeDiagnostics.SidebarBounds=Bounds(sidebarRect);TalentTreeDiagnostics.TreeNodeCounts=ClassTrees().Select(t=>data.Count(n=>n.TreeId==t.Id)).ToArray();TalentTreeDiagnostics.Backgrounds=ClassTrees().Select(t=>t.Background).ToArray();
        TalentTreeDiagnostics.Nodes=views.Select(v=>$"{v.Node.Id}:label={v.Node.Name},tier={v.Node.Tier},column={v.Node.Column},rank={TalentBridge.Rank(v.Node.Id)}/{v.Node.MaxRank},rankLabel={v.Rank.text},locked={!Can("CanSpend",v.Node.Id)&&TalentBridge.Rank(v.Node.Id)==0},maxed={TalentBridge.Rank(v.Node.Id)>=v.Node.MaxRank},canSpend={Can("CanSpend",v.Node.Id)},canRefund={Can("CanRefund",v.Node.Id)},icon={v.Icon.texture?.name??"MISSING"},artPath=Interface/Icons/{v.Node.Icon},bounds={Bounds(v.Button.GetComponent<RectTransform>())},prerequisite={v.Node.Prerequisite}").ToArray();
        TalentTreeDiagnostics.CatalogNodes=data.Select(n=>$"{n.Id}:name={n.Name},tree={n.TreeId},tier={n.Tier},column={n.Column},rank={TalentBridge.Rank(n.Id)}/{n.MaxRank},artPath=Interface/Icons/{n.Icon},prerequisite={n.Prerequisite}").ToArray();
        TalentTreeDiagnostics.Connections=links.Select(l=>$"{l.Target.Prerequisite}->{l.Target.Id}:met={TalentBridge.Rank(l.Target.Prerequisite)>0},art={l.Image.texture?.name??"MISSING"},uv={l.Image.uvRect},bounds={Bounds(l.Image.rectTransform)}").ToArray();
    }
    [HideFromIl2Cpp] static string Bounds(RectTransform rect)
    {
        if(rect==null)return "none";Camera camera=null;
        for(var p=rect.transform;p!=null;p=p.parent){var canvas=p.GetComponent<Canvas>();if(canvas==null)continue;var owner=canvas.rootCanvas??canvas;camera=owner.renderMode==RenderMode.ScreenSpaceOverlay?null:owner.worldCamera;break;}
        var r=rect.rect;var a=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(r.xMin,r.yMin,0)));var b=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(new Vector3(r.xMax,r.yMax,0)));
        return $"[{Mathf.Min(a.x,b.x):F1},{Mathf.Min(a.y,b.y):F1}..{Mathf.Max(a.x,b.x):F1},{Mathf.Max(a.y,b.y):F1}]";
    }
    [HideFromIl2Cpp] void Detail()
    {
        var node=data.FirstOrDefault(n=>n.Id==selected);if(detail==null||node==null)return;int rank=TalentBridge.Rank(node.Id);
        string description=Plugin.Catalog.Find(node.Id)?.DisplayDescription(rank)??node.Description;var prerequisite=data.FirstOrDefault(n=>n.Id==node.Prerequisite);
        detail.text=$"{node.Name}\nРанг {rank}/{node.MaxRank}\n\n{description}\n\n"+(node.RequiredPoints>0?$"Требуется {node.RequiredPoints} очков в предыдущих рядах.\n":"")+(prerequisite!=null?$"Требуется: {prerequisite.Name}.\n":"")+(Can("CanSpend",node.Id)?"Можно изучить следующий ранг.":rank>=node.MaxRank?"Максимальный ранг.":"Следующий ранг пока недоступен.");
        var learn=tooltip.transform.Find("Learn")?.GetComponent<Button>();var refund=tooltip.transform.Find("Refund")?.GetComponent<Button>();if(learn!=null)learn.interactable=Can("CanSpend",node.Id);if(refund!=null)refund.interactable=Can("CanRefund",node.Id);
        TalentTreeDiagnostics.SelectedTalent=node.Id;TalentTreeDiagnostics.TooltipText=detail.text;
    }
    [HideFromIl2Cpp] static string ClassName(string id)
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothHeroes.HeroBridge",false)).FirstOrDefault(t=>t!=null);
        return type?.GetMethod("GetClassNameRu")?.Invoke(null,new object[]{id}) as string??(id=="mage"?"Маг":id);
    }
}

public static class TalentTreeDiagnostics
{
    public static int LogicalWidth=>1600;
    public static int LogicalHeight=>860;
    public static int ConcurrentTrees=>3;
    public static string UiRevision=>"source-three-trees-0.1.3";
    public static bool NativePaused=>MyTime.paused;
    public static float UnityTimeScale=>Time.timeScale;
    public static int AvailablePoints=>TalentBridge.AvailablePoints;
    public static int SpentPoints=>TalentBridge.SpentPoints;
    public static bool PreviousNativePaused{get;internal set;}
    public static float PreviousUnityTimeScale{get;internal set;}
    public static string[] Backgrounds{get;internal set;}=Array.Empty<string>();
    public static string[] TreeBounds{get;internal set;}=Array.Empty<string>();
    public static int[] TreeNodeCounts{get;internal set;}=Array.Empty<int>();
    public static string SidebarBounds{get;internal set;}="";
    public static string FrameBounds{get;internal set;}="";
    public static string ViewportBounds{get;internal set;}="";
    public static bool Visible{get;internal set;}
    public static string ClassId{get;internal set;}="";
    public static string TreeId{get;internal set;}="";
    public static string Background{get;internal set;}="";
    public static string SelectedTalent{get;internal set;}="";
    public static string TooltipText{get;internal set;}="";
    public static int NodeCount{get;internal set;}
    public static int CatalogNodeCount{get;internal set;}
    public static int LoadedTextures{get;internal set;}
    public static int MissingTextures{get;internal set;}
    public static float ScrollPosition{get;internal set;}
    public static string[] Nodes{get;internal set;}=Array.Empty<string>();
    public static string[] CatalogNodes{get;internal set;}=Array.Empty<string>();
    public static string[] Connections{get;internal set;}=Array.Empty<string>();
    static bool Authorized=>Application.isBatchMode&&Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1";
    public static bool SelectTreeForProbe(int index)=>Authorized&&TalentPanel.Current!=null&&TalentPanel.Current.ProbeTree(index);
    public static bool HoverForProbe(string id)=>Authorized&&TalentPanel.Current!=null&&TalentPanel.Current.ProbeHover(id);
    public static bool ScrollForProbe(float position){if(!Authorized||TalentPanel.Current==null)return false;TalentPanel.Current.ProbeScroll(position);return true;}
    public static bool ClickNodeForProbe(string id,bool refund)=>Authorized&&TalentPanel.Current!=null&&TalentPanel.Current.ProbeClick(id,refund);
    public static bool RightClickNodeForProbe(string id)=>Authorized&&TalentPanel.Current!=null&&TalentPanel.Current.ProbeRightClick(id);
    public static string Describe()=>JsonSerializer.Serialize(new{UiRevision,LogicalWidth,LogicalHeight,ConcurrentTrees,Visible,ClassId,TreeId,Background,SelectedTalent,TooltipText,NodeCount,CatalogNodeCount,LoadedTextures,MissingTextures,AvailablePoints,SpentPoints,NativePaused,UnityTimeScale,PreviousNativePaused,PreviousUnityTimeScale,FrameBounds,ViewportBounds,TreeBounds,TreeNodeCounts,SidebarBounds,Backgrounds,ScrollPosition,Nodes,CatalogNodes,Connections});
    public static string BatchProbe(string action)
    {
        action=(action??"").Trim();if(action.Length==0||action=="describe")return Describe();if(!Authorized)throw new InvalidOperationException("Talent UI mutation requires batchmode and AZEROTH_LAB_HEADLESS_TEST=1.");
        var panel=TalentPanel.Current;if(panel==null)throw new InvalidOperationException("Talent UI has not built yet.");var parts=action.Split(':',2);
        bool ok=action switch{"open"=>panel.SetProbeVisible(true),"close"=>panel.SetProbeVisible(false),"reset"=>panel.ProbeReset(),_=>parts.Length==2&&parts[0] switch{"tree"=>int.TryParse(parts[1],out var index)&&panel.ProbeTree(index),"hover"=>panel.ProbeHover(parts[1]),"spend"=>panel.ProbeClick(parts[1],false),"refund"=>panel.ProbeRightClick(parts[1]),"scroll"=>ScrollForProbe(float.Parse(parts[1],System.Globalization.CultureInfo.InvariantCulture)),_=>false}};
        if(!ok)throw new InvalidOperationException("Talent UI action unavailable: "+action);Canvas.ForceUpdateCanvases();return Describe();
    }
}
