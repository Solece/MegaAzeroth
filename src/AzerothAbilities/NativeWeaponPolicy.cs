using System.Text.Json;
using Assets.Scripts.Actors.Player;
using Assets.Scripts._Data;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using HarmonyLib;
using UnityEngine;

namespace AzerothAbilities;

internal static class NativeWeaponPolicy
{
    internal static int UsesBlocked,TicksBlocked,GrantsBlocked,PlaceholderGrants;
    internal static int UnboundCardsHidden,SourceCardsBound,SourceCardsSelected,NativeSelectionsBlocked;
    internal static bool AllowNativeUse()
    {
        if(!AbilityRuntime.Active)return true;
        UsesBlocked++;return false;
    }
}

public static class NativeWeaponDiagnostics
{
    public static string LastGrantProbeJson {get;private set;}="{}";
    public static bool AttemptBlockedGrantForProbe()
    {
        if(!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")!="1" || !AbilityRuntime.Active)return false;
        var inventory=MyPlayer.Instance?.inventory?.weaponInventory;
        if(inventory?.weapons==null || inventory.GetNumWeapons()<1)return false;
        WeaponData candidate=null;
        foreach(var data in Resources.FindObjectsOfTypeAll<WeaponData>())
            if(data!=null && !inventory.weapons.ContainsKey(data.eWeapon)){candidate=data;break;}
        if(candidate==null)
        {
            LastGrantProbeJson=JsonSerializer.Serialize(new{Passed=false,Reason="No different loaded native WeaponData"});return false;
        }
        int before=inventory.GetNumWeapons(),blocked=NativeWeaponPolicy.GrantsBlocked;
        // The real IL2CPP AddWeapon wrapper must reach the installed Harmony
        // policy; no dictionary manipulation or direct counter injection.
        inventory.AddWeapon(candidate,null);
        int after=inventory.GetNumWeapons(),blockedAfter=NativeWeaponPolicy.GrantsBlocked;
        bool passed=after==before && blockedAfter==blocked+1 && !inventory.weapons.ContainsKey(candidate.eWeapon);
        LastGrantProbeJson=JsonSerializer.Serialize(new{Passed=passed,AttemptedWeapon=candidate.eWeapon.ToString(),CountBefore=before,CountAfter=after,BlockedBefore=blocked,BlockedAfter=blockedAfter});
        Plugin.Current?.Log.LogInfo("Native weapon grant probe: "+LastGrantProbeJson);
        return passed;
    }
    public static string SnapshotJson
    {
        get
        {
            var inventory=MyPlayer.Instance?.inventory?.weaponInventory;
            var weapons=new List<object>();
            if(inventory?.weapons!=null)foreach(var pair in inventory.weapons)
                weapons.Add(new{id=pair.Key.ToString(),level=pair.Value?.level??0,enginePlaceholder=AbilityRuntime.Active,attackAllowed=!AbilityRuntime.Active});
            int sourceCards=0,nativeCards=0;
            foreach(var button in Resources.FindObjectsOfTypeAll<UpgradeButton>())
            {
                if(button==null || !button.gameObject.activeInHierarchy)continue;
                if(AbilityOffers.Bound.ContainsKey(button.Pointer)){sourceCards++;continue;}
                if(AbilityOffers.NativeWeaponCards.Contains(button.Pointer) || (!button.isItem && button.upgradable!=null && button.upgradable.TryCast<WeaponData>()!=null))nativeCards++;
            }
            return JsonSerializer.Serialize(new{
                MageActive=AbilityRuntime.Active,NativeAttacksSuppressed=AbilityRuntime.Active,
                EnginePlaceholderCount=inventory?.GetNumWeapons()??0,EngineWeapons=weapons,
                ActiveSourceSpellCards=sourceCards,VisibleNativeWeaponCards=nativeCards,LastGrantProbeJson,
                NativeWeaponPolicy.UsesBlocked,NativeWeaponPolicy.TicksBlocked,NativeWeaponPolicy.GrantsBlocked,NativeWeaponPolicy.PlaceholderGrants,
                NativeWeaponPolicy.UnboundCardsHidden,NativeWeaponPolicy.SourceCardsBound,NativeWeaponPolicy.SourceCardsSelected,NativeWeaponPolicy.NativeSelectionsBlocked});
        }
    }
}

[HarmonyPatch(typeof(WeaponInventory),nameof(WeaponInventory.Tick))]
internal static class NativeWeaponInventoryTickPatch
{
    private static bool Prefix()
    {
        if(!AbilityRuntime.Active)return true;
        NativeWeaponPolicy.TicksBlocked++;return false;
    }
}

[HarmonyPatch(typeof(WeaponInventory),nameof(WeaponInventory.AddWeapon))]
internal static class NativeWeaponGrantPatch
{
    private static bool Prefix(WeaponInventory __instance)
    {
        if(!AbilityRuntime.Active)return true;
        // The native run initializer expects an inventory-owned starting weapon.
        // Keep one internal placeholder; native Tick/Use and all further grants
        // are blocked, while source spells and native tomes/items remain active.
        if(__instance!=null && (__instance.weapons==null || __instance.weapons.Count==0))
        {
            NativeWeaponPolicy.PlaceholderGrants++;return true;
        }
        NativeWeaponPolicy.GrantsBlocked++;return false;
    }
}
