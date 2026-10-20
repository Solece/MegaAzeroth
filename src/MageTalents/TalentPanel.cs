using AzerothAbilities;
using Assets.Scripts.Actors.Player;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UObject=UnityEngine.Object;

namespace MageTalents;

// Native Canvas/TMP only. This panel edits the next run's allocation; combat snapshots
// never change halfway through a run, matching the original between-runs talent rules.
public sealed class TalentPanel:MonoBehaviour
{
    internal static TalentPanel Current;
    public TalentPanel(IntPtr ptr):base(ptr){}
    GameObject root;
    TextMeshProUGUI summary,detail,hint;
    TMP_FontAsset font;Material fontMaterial;
    readonly List<Row> rows=new();
    readonly List<UnityAction> actions=new();
    string tree="Arcane",selected="arcane_focus";
    bool opened,failed;
    float next;
    float oldTimeScale;
    CursorLockMode oldCursor;
    bool oldCursorVisible;
    sealed class Row {internal GameObject Root;internal TextMeshProUGUI Name,Rank;internal Button Add,Remove;internal TalentDefinition Talent;}
    void Update()
    {
        Current=this;
        if(failed || Plugin.Catalog==null)return;
        try
        {
            if(Input.GetKeyDown(KeyCode.F6) && AbilityRuntime.Active)Toggle();
            if(opened && Input.GetKeyDown(KeyCode.Escape))Toggle();
            if(Time.unscaledTime<next)return;next=Time.unscaledTime+.2f;
            if(root==null)
            {
                foreach(var t in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
                {if(t==null||!t.gameObject.activeInHierarchy||t.font==null)continue;font=t.font;fontMaterial=t.fontSharedMaterial;break;}
                if(font!=null)Build();
            }
            if(root==null)return;
            hint.gameObject.SetActive(AbilityRuntime.Active&&!opened);
            if(opened)Refresh();
        }
        catch(Exception ex)
        {
            Close();failed=true;if(root!=null)UObject.Destroy(root);Plugin.Current.Log.LogError("Mage talent panel disabled: "+ex);
        }
    }
    void OnDestroy(){Close();if(root!=null)UObject.Destroy(root);Current=null;}
    [HideFromIl2Cpp] internal bool SetProbeVisible(bool visible){if(root==null)return false;if(opened!=visible)Toggle();return opened==visible;}
    [HideFromIl2Cpp] void Toggle(){if(root==null)return;if(opened){Close();return;}opened=true;oldTimeScale=Time.timeScale;oldCursor=Cursor.lockState;oldCursorVisible=Cursor.visible;Time.timeScale=0;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;root.transform.Find("Panel").gameObject.SetActive(true);Refresh();}
    [HideFromIl2Cpp] void Close(){if(!opened)return;opened=false;Time.timeScale=oldTimeScale;Cursor.lockState=oldCursor;Cursor.visible=oldCursorVisible;if(root!=null)root.transform.Find("Panel").gameObject.SetActive(false);}
    [HideFromIl2Cpp] static GameObject RectGo(string name,Transform parent,float x,float y,float w,float h)
    {
        var g=new GameObject(name,new[]{Il2CppType.Of<RectTransform>()});if(parent!=null)g.transform.SetParent(parent,false);
        var r=g.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);r.localScale=Vector3.one;return g;
    }
    [HideFromIl2Cpp] TextMeshProUGUI Text(Transform parent,string name,string value,float x,float y,float w,float h,float size=22)
    {
        var g=RectGo(name,parent,x,y,w,h);var t=g.AddComponent<TextMeshProUGUI>();t.font=font;t.fontSharedMaterial=fontMaterial;t.fontSize=size;t.richText=false;t.text=value;t.color=Color.white;t.alignment=TextAlignmentOptions.MidlineLeft;t.raycastTarget=false;return t;
    }
    [HideFromIl2Cpp] Button Button(Transform parent,string name,string value,float x,float y,float w,float h,Action action)
    {
        var g=RectGo(name,parent,x,y,w,h);var image=g.AddComponent<Image>();image.color=new Color(.16f,.18f,.24f,.98f);var b=g.AddComponent<Button>();b.targetGraphic=image;
        var a=(UnityAction)action;actions.Add(a);b.onClick.AddListener(a);var text=Text(g.transform,name+"Label",value,9,0,w-18,h,21);text.alignment=TextAlignmentOptions.Center;return b;
    }
    [HideFromIl2Cpp] void Build()
    {
        rows.Clear();actions.Clear();
        root=RectGo("AzerothMageTalentsCanvas",null,0,0,1920,1080);
        UObject.DontDestroyOnLoad(root);
        var c=root.AddComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;c.sortingOrder=500;
        var scale=root.AddComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1920,1080);scale.matchWidthOrHeight=.5f;root.AddComponent<GraphicRaycaster>();
        hint=Text(root.transform,"F6Hint","F6 — таланты мага",1520,10,380,35,21);hint.alignment=TextAlignmentOptions.Right;
        var panel=RectGo("Panel",root.transform,450,70,1020,940);var bg=panel.AddComponent<Image>();bg.color=new Color(.025f,.03f,.055f,.99f);
        Text(panel.transform,"Title","Таланты мага — Azeroth",24,10,800,43,30);
        Button(panel.transform,"Close","Закрыть [F6]",800,12,195,42,Close);
        summary=Text(panel.transform,"Summary","",24,59,970,64,20);
        var names=new[]{"Тайная магия","Огонь","Лёд"};var ids=new[]{"Arcane","Fire","Frost"};
        for(var i=0;i<3;i++){var id=ids[i];Button(panel.transform,"Tree"+id,names[i],24+i*327,131,315,42,()=>SelectTree(id));}
        for(var i=0;i<13;i++)
        {
            var row=new Row();row.Root=RectGo("Talent"+i,panel.transform,24,184+i*45,970,41);
            row.Name=Text(row.Root.transform,"Name","",8,0,710,41,20);row.Rank=Text(row.Root.transform,"Rank","",720,0,65,41,20);
            Button(row.Root.transform,"Details","?",792,0,43,41,()=>{if(row.Talent!=null){selected=row.Talent.Id;Refresh();}});
            row.Remove=Button(row.Root.transform,"Refund","−",844,0,52,41,()=>{if(row.Talent!=null)TalentBridge.Refund(row.Talent.Id);Refresh();});
            row.Add=Button(row.Root.transform,"Learn","+",905,0,52,41,()=>{if(row.Talent!=null){selected=row.Talent.Id;TalentBridge.Spend(row.Talent.Id);}Refresh();});rows.Add(row);
        }
        detail=Text(panel.transform,"Detail","",24,786,970,100,19);
        Button(panel.transform,"Respec","Сбросить таланты бесплатно",24,894,360,34,()=>{TalentBridge.Reset();Refresh();});
        Text(panel.transform,"Note","Очки: опыт за время, убийства и боссов. Порог: 80 + 15 × номер очка; максимум 51.",398,892,590,39,17);
        panel.SetActive(false);Plugin.Current.Log.LogInfo("Mage talent UI built: native Canvas/TMP,39 source nodes,3 trees,F6; allocations apply next run; free respec.");
    }
    [HideFromIl2Cpp] void SelectTree(string id)
    {tree=id;foreach(var t in Plugin.Catalog.Talents)if(t.Tree==tree){selected=t.Id;break;}Refresh();}
    [HideFromIl2Cpp] void Refresh()
    {
        if(summary==null)return;var s=Plugin.Saved;var catalog=Plugin.Catalog;var earned=catalog.PointsForXp(s.TalentXp);var into=s.TalentXp-catalog.XpToPoint(earned);var threshold=catalog.XpForNext(earned);
        summary.text=TalentState.TemporaryPointBudget>0?$"Тест: {TalentState.TemporaryPointBudget} очков    Свободно: {s.Available(catalog)}    Потрачено: {s.Spent}\nИзменения применяются сразу. Уровни изученных заклинаний сохраняются.":$"Свободно: {s.Available(catalog)}    Потрачено: {s.Spent} / {earned}    Опыт талантов: {into} / {threshold}\n"+(TalentBridge.PendingNextRun?"Изменения вступят в силу со следующего забега.":"Новый набор применяется при следующем старте боя.");
        var nodes=new List<TalentDefinition>();foreach(var t in catalog.Talents)if(t.Tree==tree)nodes.Add(t);
        for(var i=0;i<rows.Count;i++)
        {
            var row=rows[i];var t=nodes[i];row.Talent=t;var rank=s.Rank(t.Id);row.Name.text=$"{t.Name}  ·  ряд {t.Tier+1} / {t.RequiredTreePoints} очков";row.Rank.text=$"{rank}/{t.MaxRank}";
            var can=s.Clone();row.Add.interactable=can.Spend(catalog,t.Id);can=s.Clone();row.Remove.interactable=can.Refund(catalog,t.Id);
            row.Name.color=rank>0?new Color(1,.84f,.35f):new Color(.75f,.77f,.82f);
        }
        var d=catalog.Find(selected);detail.text=d==null?"":d.Name+" — "+d.DisplayDescription(s.Rank(d.Id))+(d.Prerequisite.Length>0?"\nТребуется: "+catalog.Find(d.Prerequisite)?.Name:"");
    }
}
