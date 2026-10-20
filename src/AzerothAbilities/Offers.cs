using Assets.Scripts._Data;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Progression;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Menu.Shop;
using HarmonyLib;
using UnityEngine;
using System.Runtime.InteropServices;
using NativeList=Il2CppSystem.Collections.Generic.List<Assets.Scripts._Data.IUpgradable>;

namespace AzerothAbilities;

internal static class AbilityOffers
{
    internal static readonly Dictionary<IntPtr,AbilityChoice> Bound=new();
    private static readonly HashSet<string> Used=new();
    private static readonly Dictionary<string,Texture2D> Icons=new();
    internal static void Begin(){Bound.Clear();Used.Clear();}
    internal static AbilityChoice Bind(IntPtr button,int rarity)
    {
        var choices=AbilityRuntime.Choices(rarity);if(choices.Count==0)return null;
        var unused=choices.Where(c=>!Used.Contains(c.Label)).ToList();if(unused.Count==0)unused=choices;
        var choice=unused[UnityEngine.Random.Range(0,unused.Count)];Used.Add(choice.Label);Bound[button]=choice;return choice;
    }
    internal static Texture2D Icon(SpellDefinition spell)
    {
        if(Icons.TryGetValue(spell.Id,out var cached))return cached;
        Texture2D texture=null;
        try
        {
            var directory=Path.Combine(BepInEx.Paths.PluginPath,"AzerothHud","assets","Interface","Icons");
            var rawPath=Path.Combine(directory,spell.Icon+".rgba");
            var sizePath=Path.ChangeExtension(rawPath,".size");
            if(!File.Exists(rawPath) || !File.Exists(sizePath))
                throw new FileNotFoundException("Prepared spell icon is missing",rawPath);
            var size=File.ReadAllText(sizePath).Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
            if(size.Length!=2 || !int.TryParse(size[0],out var width) || !int.TryParse(size[1],out var height)
                || width<=0 || height<=0 || width>4096 || height>4096)
                throw new InvalidDataException("Invalid spell icon dimensions: "+sizePath);
            var pixels=File.ReadAllBytes(rawPath);
            if(pixels.Length!=checked(width*height*4))
                throw new InvalidDataException("Invalid spell icon RGBA length: "+rawPath);

            // The generated LoadImage(byte[]) wrapper reaches unsupported IL2CPP Span constructors.
            // These offline RGBA assets have the bottom row first; pin only during the native upload.
            texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
            var pinned=GCHandle.Alloc(pixels,GCHandleType.Pinned);
            try { texture.LoadRawTextureData(pinned.AddrOfPinnedObject(),pixels.Length);texture.Apply(false,true); }
            finally { pinned.Free(); }
        }
        catch(Exception ex)
        {
            if(texture!=null)UnityEngine.Object.Destroy(texture);
            texture=null;
            Plugin.Current?.Log.LogError($"Spell icon {spell.Id} could not be loaded: {ex.Message}");
        }
        // RawImage consumes this texture directly. Retain it for the plugin lifetime and reuse it on cards.
        Icons[spell.Id]=texture;return texture;
    }
    internal static void ReplaceWeaponsWithBooks(NativeList offers)
    {
        for(int i=offers.Count-1;i>=0;i--)if(offers[i].TryCast<WeaponData>()!=null)offers.RemoveAt(i);
        var inventory=MyPlayer.Instance?.inventory;
        if(inventory==null || offers.Count>=3)return;
        var candidates=new List<TomeData>();
        foreach(var tome in UnlockUtility.GetAvailableTomes())
        {
            if(RunUnlockables.banishedUpgradables.Contains(tome))continue;
            var owned=inventory.tomeInventory.HasTome(tome.eTome);
            if(!owned && inventory.tomeInventory.GetNumTomes()>=InventoryUtility.GetNumAvailableTomeSlots())continue;
            if(owned && inventory.tomeInventory.GetTomeLevel(tome.eTome)>=InventoryUtility.GetTomeMaxLevel())continue;
            var already=false;for(int j=0;j<offers.Count;j++)if(offers[j].Pointer==tome.Pointer){already=true;break;}if(!already)candidates.Add(tome);
        }
        while(offers.Count<3 && candidates.Count>0)
        {
            var i=UnityEngine.Random.Range(0,candidates.Count);offers.Add(candidates[i].Cast<IUpgradable>());candidates.RemoveAt(i);
        }
    }
}

[HarmonyPatch(typeof(InventoryUtility),nameof(InventoryUtility.GetRandomUpgrades))]
internal static class OfferGenerationPatch
{
    private static void Postfix(ref NativeList __result)
    {
        if(!AbilityRuntime.Active || __result==null)return;
        AbilityOffers.Begin();
        if(AbilityRuntime.Choices().Count==0)AbilityOffers.ReplaceWeaponsWithBooks(__result);
    }
}

[HarmonyPatch(typeof(UpgradeButton),nameof(UpgradeButton.SetUpgrade))]
internal static class OfferCardPatch
{
    private static void Postfix(UpgradeButton __instance,IUpgradable upgradable)
    {
        AbilityOffers.Bound.Remove(__instance.Pointer);
        if(!AbilityRuntime.Active || upgradable==null || upgradable.TryCast<WeaponData>()==null)return;
        var sourceRarity=__instance.rarity;
        if(sourceRarity==ERarity.New)sourceRarity=Rarity.GetUpgradeOfferRarity(PlayerStats.GetStat(EStat.Luck));
        var choice=AbilityOffers.Bind(__instance.Pointer,Math.Clamp((int)sourceRarity-1,0,4));if(choice==null)return;
        __instance.t_name.text=choice.Upgrade==null?choice.Spell.Name:choice.Label;
        __instance.t_description.text=choice.Description;
        __instance.t_level.text=choice.Upgrade==null?"New spell · Rank 1":"Spell upgrade";
        __instance.t_rarity.text=choice.Upgrade==null?"Azeroth":sourceRarity.ToString();
        var icon=AbilityOffers.Icon(choice.Spell);
        __instance.icon.texture=icon;
        Plugin.Current?.Log.LogInfo($"Azeroth offer bound: {choice.Label}; native tome cards unchanged");
    }
}

[HarmonyPatch(typeof(UpgradeButton),nameof(UpgradeButton.SelectUpgrade))]
internal static class OfferSelectionPatch
{
    private static bool Prefix(UpgradeButton __instance)
    {
        if(!AbilityRuntime.Active || !AbilityOffers.Bound.TryGetValue(__instance.Pointer,out var choice))return true;
        AbilityRuntime.Apply(choice);
        AbilityOffers.Bound.Remove(__instance.Pointer);
        __instance.upgradePicker.levelupScreen.CloseLevelupScreen();
        return false;
    }
}


