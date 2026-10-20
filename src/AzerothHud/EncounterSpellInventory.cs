using TMPro;
using UnityEngine;

namespace AzerothHud;

// Applies the same real learned-spell view to native level-up/shop inventories.
// Native tome/item containers and all selection handlers remain in place.
sealed class EncounterSpellInventory
{
    sealed class Entry
    {
        internal UpgradeInventoryUI Inventory;
        internal readonly PauseSpellInventory Panel=new();
        internal readonly List<(TMP_Text Text,string Original)> Headings=new();
        internal bool Active;
    }
    readonly Dictionary<long,Entry> entries=new();
    internal static string[] Diagnostics=Array.Empty<string>();
    public void Refresh(bool mage,TMP_FontAsset font,Material material,string[] ids,int[] ranks,string[] names,string[] descriptions,string[] icons,Func<string,Texture2D> loader)
    {
        if(!mage){Restore();return;}
        var seen=new HashSet<long>();var notes=new List<string>();
        var pause=UiManager.Instance?.pause?.inventory;
        foreach(var inventory in Resources.FindObjectsOfTypeAll<UpgradeInventoryUI>())
        {
            if(inventory==null || !inventory.gameObject.scene.IsValid() || !inventory.gameObject.activeInHierarchy || inventory.weaponParent==null || (pause!=null && inventory.Pointer==pause.Pointer))continue;
            long key=inventory.Pointer.ToInt64();seen.Add(key);
            if(!entries.TryGetValue(key,out var entry))entries[key]=entry=new Entry{Inventory=inventory};
            entry.Active=true;
            entry.Panel.RefreshRow(inventory.weaponParent,font,material,ids,ranks,names,descriptions,icons,loader);
            foreach(var text in inventory.GetComponentsInChildren<TMP_Text>(true))
            {
                if(text==null || text.text is not ("Оружие" or "Weapons" or "Weapon"))continue;
                if(!entry.Headings.Any(h=>h.Text==text))entry.Headings.Add((text,text.text));
                text.text="Способности";
            }
            notes.Add($"EncounterSpellInventory active=True,learned={ids.Length},nativeTomesRetained={inventory.tomeParent!=null},nativeItemsRetained={inventory.itemParent!=null},name={inventory.gameObject.name}");
            notes.AddRange(entry.Panel.Describe());
        }
        foreach(var pair in entries.ToArray())
        {
            if(seen.Contains(pair.Key))continue;
            Restore(pair.Value);
            if(pair.Value.Inventory==null){pair.Value.Panel.Dispose();entries.Remove(pair.Key);}
        }
        Diagnostics=notes.ToArray();
    }
    public void Enforce()
    {
        foreach(var entry in entries.Values)
            if(entry.Active){entry.Panel.Enforce();foreach(var h in entry.Headings)if(h.Text!=null)h.Text.text="Способности";}
    }
    static void Restore(Entry entry)
    {
        entry.Active=false;entry.Panel.Restore();
        foreach(var h in entry.Headings)if(h.Text!=null)h.Text.text=h.Original;
        entry.Headings.Clear();
    }
    public void Restore(){foreach(var entry in entries.Values)Restore(entry);Diagnostics=Array.Empty<string>();}
    public void Dispose(){Restore();foreach(var entry in entries.Values)entry.Panel.Dispose();entries.Clear();}
}
