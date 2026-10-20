using System.Reflection;
using System.Text.Json;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups.AbilitiesPassive;
using HarmonyLib;
using UnityEngine;

namespace AzerothHeroes;

// The root orchestrator calls this from Unity's main thread. No desktop or input automation.
public static class HeroProbeApi
{
    public static string BatchProbe(string action)
    {
        action=(action??"").Trim().ToLowerInvariant();
        if(action.Length==0 || action=="describe")return SnapshotJson;
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1")
            throw new InvalidOperationException("Hero mutation probes require batchmode and AZEROTH_LAB_HEADLESS_TEST=1.");
        var menu=Resources.FindObjectsOfTypeAll<CharacterMenu>().FirstOrDefault(m=>m!=null&&m.gameObject.scene.IsValid());
        if(menu==null)throw new InvalidOperationException("No scene CharacterMenu.");
        if(action=="focus")
        {
            var main=menu.mainMenu??Resources.FindObjectsOfTypeAll<MainMenu>().FirstOrDefault(m=>m!=null&&m.gameObject.scene.IsValid());
            if(main==null)throw new InvalidOperationException("No scene MainMenu for the native character-selection route.");
            // Native GoToCharacterSelection activates its target, then deactivates
            // currentTab. Reopening the same current tab would hide it immediately.
            if(main.tabCharacters==null)throw new InvalidOperationException("Native character tab is absent.");
            if(!main.tabCharacters.activeSelf)main.GoToCharacterSelection();
            menu.FocusWindow();Canvas.ForceUpdateCanvases();return SnapshotJson;
        }
        var parts=action.Split(':',2);if(parts.Length!=2 || Roster.ForClass(parts[1])==null)throw new ArgumentException("Use select:<class>,click:<class>,confirm:<class>,focus or describe.");
        var button=menu.characterGridParent.GetComponentsInChildren<MyButtonCharacter>(true)
            .FirstOrDefault(b=>Roster.ForClone(b.characterData)?.Id==parts[1]);
        if(button==null)throw new InvalidOperationException("Native class button is absent: "+parts[1]);
        button.StartHover(); // Real native A_Select -> CharacterMenu.OnSelectCharacter.
        if(parts[0]=="click")button.GetButton().onClick.Invoke();
        else if(parts[0]=="confirm")AccessTools.Method(typeof(CharacterMenu),"OnConfirmCharacter").Invoke(menu,new object[]{button});
        else if(parts[0]!="select")throw new ArgumentException("Unknown hero action.");
        Canvas.ForceUpdateCanvases();return SnapshotJson;
    }
    public static string SnapshotJson
    {
        get
        {
            var buttons=new List<object>();
            foreach(var menu in Resources.FindObjectsOfTypeAll<CharacterMenu>())
            {
                if(menu==null || !menu.gameObject.scene.IsValid() || menu.characterGridParent==null)continue;
                foreach(var button in menu.characterGridParent.GetComponentsInChildren<MyButtonCharacter>(true))
                {
                    var c=Roster.ForClone(button.characterData);
                    buttons.Add(new{ClassId=c?.Id??"native",NativeId=(int)(button.characterData?.eCharacter??0),
                        Active=button.gameObject.activeInHierarchy,button.canUseCharacter,
                        Text=button.t_name?.text??"",Icon=button.i_icon?.texture?.name??"",PrivateClone=c!=null,
                        PortraitLoaded=c?.Portrait!=null,PortraitWidth=c?.Portrait?.width??0,PortraitHeight=c?.Portrait?.height??0,
                        StartingSpell=c?.StartingSpell??"",StartingSpellName=c?.StartingSpellName??"",StartingSpellIcon=c?.StartingSpellIcon??"",TreeIcon=c?.TreeIcon??"",
                        NativeClickHandler=button.GetButton()!=null});
                }
            }
            var inv=MyPlayer.Instance?.inventory;
            var aliases=Enumerable.Range(0,21).Select(i=>new{NativeId=i,ClassId=Roster.ForId((ECharacter)i)?.Id??""}).ToArray();
            var info=new List<object>();
            foreach(var panel in Resources.FindObjectsOfTypeAll<CharacterInfoUI>())
                if(panel!=null && panel.gameObject.scene.IsValid())info.Add(new{Active=panel.gameObject.activeInHierarchy,Name=panel.t_name?.text??"",Portrait=panel.i_character?.texture?.name??"",SpellName=panel.t_weaponName?.text??"",SpellIcon=panel.i_weapon?.texture?.name??"",TalentName=panel.t_passiveName?.text??"",TalentDescription=panel.t_passiveDesc?.text??"",TalentIcon=panel.i_passive?.texture?.name??""});
            return JsonSerializer.Serialize(new{HeroBridge.SelectedClassId,HeroBridge.ActiveClassId,HeroBridge.SelectedRaceId,HeroBridge.ActiveRaceId,HeroBridge.SourceRaceId,HeroBridge.ActiveFormSpellId,HeroBridge.ClassNameRu,
                Roster.Ready,ClassCount=Roster.Classes.Count,Buttons=buttons,LegacyAliases=aliases,
                SelectedHeroPanels=info,NativeCosmetics=NativeAppearanceMask.Snapshot(),
                Roster.LastSelected,Roster.LastConfirmed,Roster.LastStarted,Roster.NativeSelections,Roster.NativeConfirmations,Roster.NativeStarts,
                InventoryPrivateClone=Roster.IsClone(inv?.characterData),InventoryNativeId=(int)(inv?.characterData?.eCharacter??0),
                NativePassive=inv?.passiveAbility?.GetPassiveType().ToString()??"null",
                NativePassiveType=inv?.passiveAbility?.GetIl2CppType().FullName??"null",
                NativeClassStatModifiers=inv?.characterData?.statModifiers?.Count??-1,
                NativeActiveAbsent=inv!=null && inv.activeAbility==null,Roster.NativeActivesBlocked,Roster.NativeWeaponsBlocked,
                UnlockPolicy="Scoped runtime getters only; module has no achievement, purchase or silver write calls"});
        }
    }
}
