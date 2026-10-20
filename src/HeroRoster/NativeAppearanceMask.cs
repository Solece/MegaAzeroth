using System.Text.Json;
using Il2CppInterop.Runtime.Attributes;
using UnityEngine;
using TMPro;

namespace AzerothHeroes;

// Scope every change to explicit native cosmetic fields for an actually selected
// private source clone. Do not infer a parent to hide from its name or geometry.
static class NativeAppearanceMask
{
    sealed class Scope{internal CharacterMenu Menu;internal CharacterInfoUI Info;internal bool Source;}
    sealed class GoState{internal GameObject Value;internal bool Active;internal string Field;}
    sealed class BehaviourState{internal Behaviour Value;internal bool Enabled;internal string Field;}
    static readonly Dictionary<int,Scope> scopes=new();
    static readonly Dictionary<int,GoState> objects=new();
    static readonly Dictionary<int,BehaviourState> behaviours=new();
    static readonly HashSet<int> wantedObjects=new(),wantedBehaviours=new();
    static readonly List<string> guarded=new();
    internal static void SelectMenu(CharacterMenu menu,MyButtonCharacter selected)
    {
        if(menu==null)return;bool source=Roster.IsClone(selected?.characterData);
        scopes[menu.GetInstanceID()]=new Scope{Menu=menu,Source=source};
        foreach(var info in Resources.FindObjectsOfTypeAll<CharacterInfoUI>())
            if(info!=null&&info.characterMenu==menu)scopes[info.GetInstanceID()]=new Scope{Menu=menu,Info=info,Source=source};
        Enforce();
    }
    internal static void SelectInfo(CharacterInfoUI info,MyButtonCharacter selected)
    {
        if(info==null)return;scopes[info.GetInstanceID()]=new Scope{Menu=info.characterMenu,Info=info,Source=Roster.IsClone(selected?.characterData)};Enforce();
    }
    internal static void Enforce()
    {
        wantedObjects.Clear();wantedBehaviours.Clear();guarded.Clear();
        foreach(var pair in scopes.ToArray())
        {
            var scope=pair.Value;
            if(scope.Menu==null&&scope.Info==null){scopes.Remove(pair.Key);continue;}
            if(!scope.Source)continue;
            // A hidden parent menu is left alone. Its saved target state is restored
            // below; the selection callback/next LateUpdate re-applies on reopening.
            if(scope.Menu!=null&&!scope.Menu.gameObject.activeInHierarchy)continue;
            var protect=Protected(scope);
            if(scope.Info!=null)Skin(scope.Info.skinSelection,"CharacterInfoUI.skinSelection",protect);
            else if(scope.Menu!=null)
            {
                Skin(scope.Menu.skinSelection,"CharacterMenu.skinSelection",protect);
                Hide(scope.Menu.hatSelection,"CharacterMenu.hatSelection",protect);
                Hat(scope.Menu.b_hats,"CharacterMenu.b_hats",protect);
                Hat(scope.Menu.b_hatPrev,"CharacterMenu.b_hatPrev",protect);
            }
        }
        foreach(var pair in objects.ToArray())
        {
            var saved=pair.Value;if(saved.Value==null){objects.Remove(pair.Key);continue;}
            if(wantedObjects.Contains(pair.Key)){if(saved.Value.activeSelf)saved.Value.SetActive(false);}
            else{saved.Value.SetActive(saved.Active);objects.Remove(pair.Key);}
        }
        foreach(var pair in behaviours.ToArray())
        {
            var saved=pair.Value;if(saved.Value==null){behaviours.Remove(pair.Key);continue;}
            if(wantedBehaviours.Contains(pair.Key))saved.Value.enabled=false;
            else{saved.Value.enabled=saved.Enabled;behaviours.Remove(pair.Key);}
        }
    }
    static List<Transform> Protected(Scope scope)
    {
        var list=new List<Transform>();
        void Add(Component c){if(c!=null)list.Add(c.transform);}
        if(scope.Menu!=null){Add(scope.Menu.b_confirm);Add(scope.Menu.b_back);Add(scope.Menu.b_purchase);if(scope.Menu.characterGridParent!=null)list.Add(scope.Menu.characterGridParent);}
        // If a native SkinSelection component shares a larger info-panel object,
        // do not hide source identity, ranks, runs or progress along with its skins.
        var infos=scope.Info!=null?new[]{scope.Info}:Resources.FindObjectsOfTypeAll<CharacterInfoUI>().Where(i=>i!=null&&i.characterMenu==scope.Menu).ToArray();
        foreach(var info in infos){Add(info.t_name);Add(info.t_description);Add(info.i_character);Add(info.i_weapon);Add(info.i_passive);Add(info.t_rank);Add(info.t_runs);Add(info.progressBar);}
        return list;
    }
    static bool Shared(GameObject go,List<Transform> protect)=>protect.Any(t=>t!=null&&(t==go.transform||t.IsChildOf(go.transform)));
    static void Skin(SkinSelection skin,string field,List<Transform> protect)
    {
        if(skin==null)return;Disable(skin,field);
        if(Shared(skin.gameObject,protect))
        {
            guarded.Add(field+":"+Path(skin.transform)+" shared root preserved; exact SkinContainer children only");
            foreach(var cell in skin.GetComponentsInChildren<SkinContainer>(true))if(cell!=null)Hide(cell.gameObject,field+".SkinContainer",protect);
        }
        else Hide(skin.gameObject,field,protect);
    }
    static void Hat(MyButton button,string field,List<Transform> protect)
    {
        if(button==null)return;
        if(Shared(button.gameObject,protect)){guarded.Add(field+":"+Path(button.transform)+" protected native control component preserved");return;}
        Disable(button,field);Hide(button.gameObject,field,protect);
    }
    static void Hide(GameObject go,string field,List<Transform> protect)
    {
        if(go==null)return;
        if(Shared(go,protect)){guarded.Add(field+":"+Path(go.transform)+" protected native control ancestor preserved");return;}
        int id=go.GetInstanceID();wantedObjects.Add(id);
        if(!objects.ContainsKey(id))objects[id]=new GoState{Value=go,Active=go.activeSelf,Field=field};
    }
    static void Disable(Behaviour value,string field)
    {
        if(value==null)return;int id=value.GetInstanceID();wantedBehaviours.Add(id);
        if(!behaviours.ContainsKey(id))behaviours[id]=new BehaviourState{Value=value,Enabled=value.enabled,Field=field};
    }
    internal static void RestoreAll()
    {
        foreach(var saved in objects.Values)if(saved.Value!=null)saved.Value.SetActive(saved.Active);
        foreach(var saved in behaviours.Values)if(saved.Value!=null)saved.Value.enabled=saved.Enabled;
        objects.Clear();behaviours.Clear();scopes.Clear();wantedObjects.Clear();wantedBehaviours.Clear();guarded.Clear();
    }
    static string Path(Transform t){var names=new List<string>();for(var p=t;p!=null;p=p.parent)names.Add(p.name);names.Reverse();return string.Join("/",names);}
    internal static object Snapshot()=>new{
        scopeCount=scopes.Count,sourceSelections=scopes.Values.Count(s=>s.Source),
        maskedObjects=objects.Values.Select(s=>new{field=s.Field,path=s.Value!=null?Path(s.Value.transform):"destroyed",originalActive=s.Active,currentActive=s.Value!=null&&s.Value.activeSelf,currentVisible=s.Value!=null&&s.Value.activeInHierarchy}).ToArray(),
        disabledBehaviours=behaviours.Values.Select(s=>new{field=s.Field,path=s.Value!=null?Path(s.Value.transform):"destroyed",originalEnabled=s.Enabled,currentEnabled=s.Value!=null&&s.Value.enabled}).ToArray(),
        guardedSharedRoots=guarded.ToArray(),
        // Readonly discovery for the root's real scene screenshot/header check.
        skinHeaders=SkinHeaders(),
        confirms=scopes.Values.Where(s=>s.Menu!=null).Select(s=>s.Menu).Distinct().Select(m=>new{menu=Path(m.transform),present=m.b_confirm!=null,active=m.b_confirm!=null&&m.b_confirm.gameObject.activeInHierarchy,enabled=m.b_confirm!=null&&m.b_confirm.enabled}).ToArray(),
        policy="Only exact native cosmetic fields and SkinContainer children of a protected shared root. Header discovery is readonly; ranks/runs/progress and confirm controls are protected."};
    static object[] SkinHeaders()
    {
        var rows=new List<object>();var seen=new HashSet<int>();
        foreach(var scope in scopes.Values)
        {
            if(!scope.Source)continue;
            SkinSelection[] skins={scope.Info!=null?scope.Info.skinSelection:null,scope.Menu!=null?scope.Menu.skinSelection:null};
            foreach(var skin in skins)
            {
                if(skin==null)continue;
                foreach(var text in skin.GetComponentsInChildren<TMP_Text>(true))
                    if(text!=null&&seen.Add(text.GetInstanceID())&&text.text is "Скин" or "Скины" or "Skin" or "Skins")rows.Add(new{text=text.text,path=Path(text.transform),active=text.gameObject.activeInHierarchy});
            }
        }
        return rows.ToArray();
    }
    internal static string DiagnosticsJson=>JsonSerializer.Serialize(Snapshot());
}

public sealed class NativeAppearanceMaskBehaviour:MonoBehaviour
{
    static NativeAppearanceMaskBehaviour current;
    public NativeAppearanceMaskBehaviour(IntPtr pointer):base(pointer){current=this;}
    void LateUpdate()=>NativeAppearanceMask.Enforce();
    void OnDestroy(){if(current==this){NativeAppearanceMask.RestoreAll();current=null;}}
    [HideFromIl2Cpp] internal static void Shutdown(){if(current!=null)current.enabled=false;NativeAppearanceMask.RestoreAll();}
}
