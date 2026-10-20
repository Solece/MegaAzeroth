using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory__Items__Pickups.Stats;
using Assets.Scripts.Inventory__Items__Pickups.AbilitiesPassive;
using Assets.Scripts.Inventory__Items__Pickups.AbilitiesActives;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using Assets.Scripts.Saves___Serialization.Progression.Achievements;
using Assets.Scripts.Saves___Serialization.Progression.Unlocks;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothHeroes;

[BepInPlugin("local.azerothmegabonk.heroes","Azeroth Native Hero Roster","0.1.4")]
public sealed class Plugin:BasePlugin
{
    internal static Plugin Current;
    internal static string Root;
    Harmony harmony;
    public override void Load()
    {
        Current=this;Root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        Roster.Load(Path.Combine(Root,"classes.json"));
        harmony=new Harmony("local.azerothmegabonk.heroes");harmony.PatchAll(typeof(Plugin).Assembly);AddComponent<NativeAppearanceMaskBehaviour>();
        Log.LogInfo("Source roster: nine native character choices; runtime-only unlocks, common neutral engine character, no native character passives.");
    }
    public override bool Unload(){harmony?.UnpatchSelf();NativeAppearanceMaskBehaviour.Shutdown();Roster.Dispose();return true;}
}

public static class HeroBridge
{
    public static string[] ClassIds=>Roster.Classes.Select(c=>c.Id).ToArray();
    public static string SelectedClassId=>Roster.ExplicitClass??Roster.ForId(CharacterMenu.selectedCharacter)?.Id??"mage";
    public static string ActiveClassId
    {
        get
        {
            var player=MyPlayer.Instance;
            return player?.inventory?.characterData!=null && Roster.IsClone(player.inventory.characterData)
                ?Roster.ForId(player.inventory.characterData.eCharacter)?.Id??"":"";
        }
    }
    public static string ClassNameRu=>GetClassNameRu(ActiveClassId.Length>0?ActiveClassId:SelectedClassId);
    // This is the laboratory's visual preset, not a claim about original class defaults.
    public static string SelectedRaceId=>RaceForClass(SelectedClassId);
    public static string ActiveRaceId=>ActiveClassId.Length>0?RaceForClass(ActiveClassId):"";
    public static int SourceRaceId=>ActiveRaceId switch{"human"=>1,"orc"=>2,"dwarf"=>3,"night_elf"=>4,_=>0};
    public static string RaceForClass(string classId)=>classId switch{"hunter"=>"dwarf","shaman"=>"orc","druid"=>"night_elf",null or ""=>"",_=>"human"};
    public static string StartingSpellId=>Roster.ForClass(ActiveClassId.Length>0?ActiveClassId:SelectedClassId)?.StartingSpell??"";
    public static string StartingSpellName=>Roster.ForClass(ActiveClassId.Length>0?ActiveClassId:SelectedClassId)?.StartingSpellName??"";
    public static string NativeCosmeticDiagnosticsJson=>NativeAppearanceMask.DiagnosticsJson;
    public static Texture GetClassPortrait(string classId)=>Roster.ForClass(classId)?.Portrait;
    public static Texture GetClassGlyph(string classId)=>Roster.ForClass(classId)?.Glyph;
    public static int ActiveFormSpellId{get;private set;}
    public static bool SetFormForRuntime(int spellId)
    {
        var id=ActiveClassId;
        if(spellId!=0 && !(id=="druid"&&(spellId==9634||spellId==768)) && !(id=="priest"&&spellId==15473))return false;
        if(ActiveFormSpellId==spellId)return true;
        ActiveFormSpellId=spellId;Changed(id);return true;
    }
    public static event Action<string> ClassChanged;
    public static string GetClassNameRu(string id)=>Roster.ForClass(id)?.NameRu??id??"";
    internal static void Changed(string id){try{ClassChanged?.Invoke(id);}catch(Exception ex){Plugin.Current.Log.LogWarning("ClassChanged consumer: "+ex.Message);}}
}

sealed class SourceClass
{
    public string Id,Name,NameRu,Description,StartingSpell,Icon,StartingSpellName,StartingSpellIcon,TreeIcon,TreeNames;
    public int Index;
    public CharacterData Data;
    public PassiveData Passive;
    public Texture2D Texture,Portrait,Glyph,SpellTexture,TreeTexture;
}

static class Roster
{
    internal static readonly List<SourceClass> Classes=new();
    internal static readonly List<Object> Owned=new();
    internal static readonly Dictionary<int,CharacterData> Originals=new();
    internal static bool Building,Ready;
    internal static int InventoryConstructionDepth;
    internal static int NativeWeaponsBlocked,NativeActivesBlocked;
    internal static string ExplicitClass;
    internal static string LastSelected="",LastStarted="",LastConfirmed="";
    internal static int NativeSelections,NativeConfirmations,NativeStarts;
    // Preserve all old preference enum values. Amog (the previous Mage shell) maps to Mage.
    static readonly int[] Aliases={0,1,2,3,4,5,6,7,8,0,1,2,3,4,6,5,6,7,8,0,1};
    static readonly string[] NamesRu={"Воин","Паладин","Охотник","Разбойник","Жрец","Шаман","Маг","Чернокнижник","Друид"};
    internal static void Load(string path)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(path));
        foreach(var c in document.RootElement.GetProperty("classes").EnumerateArray())
        {
            int i=Classes.Count;
            var trees=c.GetProperty("talentTrees").EnumerateArray().ToArray();
            Classes.Add(new SourceClass{Id=c.GetProperty("id").GetString(),Name=c.GetProperty("name").GetString(),NameRu=NamesRu[i],
                StartingSpell=c.GetProperty("startingSpell").GetString(),Icon=c.GetProperty("icon").GetString(),Description=c.GetProperty("description").GetString(),Index=i,
                StartingSpellName=c.TryGetProperty("startingSpellName",out var spellName)?spellName.GetString():c.GetProperty("startingSpell").GetString(),
                StartingSpellIcon=c.TryGetProperty("startingSpellIcon",out var spellIcon)?spellIcon.GetString():c.GetProperty("icon").GetString(),
                TreeIcon=trees[0].GetProperty("icon").GetString(),TreeNames=string.Join(" · ",trees.Select(t=>t.GetProperty("name").GetString()))});
        }
        if(Classes.Count!=9)throw new InvalidDataException("Expected the nine source classes.");
        var args=Environment.GetCommandLineArgs();
        var option=args.FirstOrDefault(s=>s.StartsWith("-azeroth-hero=",StringComparison.OrdinalIgnoreCase));
        if(option!=null)
        {
            ExplicitClass=option.Substring("-azeroth-hero=".Length).ToLowerInvariant();
            if(ForClass(ExplicitClass)==null)throw new InvalidDataException("Unknown -azeroth-hero class: "+ExplicitClass);
        }
        else if(args.Contains("-azeroth-test-battle"))ExplicitClass="mage";
    }
    internal static SourceClass ForId(ECharacter id)=>Classes.Count==9?Classes[Aliases[Math.Clamp((int)id,0,20)]]:null;
    internal static SourceClass ForClass(string id)=>Classes.FirstOrDefault(c=>c.Id==id);
    internal static SourceClass ForClone(CharacterData data)=>data==null?null:Classes.FirstOrDefault(c=>c.Data!=null && c.Data.Pointer==data.Pointer);
    internal static SourceClass ForPassive(PassiveData data)=>data==null?null:Classes.FirstOrDefault(c=>c.Passive!=null && c.Passive.Pointer==data.Pointer);
    internal static bool IsClone(CharacterData data)=>ForClone(data)!=null;
    internal static bool SourceInventory=>IsClone(MyPlayer.Instance?.inventory?.characterData);
    internal static bool Ensure()
    {
        if(Ready)return true;if(Building || DataManager.Instance==null)return false;
        Building=true;
        try
        {
            var basis=DataManager.Instance.GetCharacterData(ECharacter.Amog);
            if(basis==null)return false;
            for(int i=0;i<21;i++)
            {
                var original=DataManager.Instance.GetCharacterData((ECharacter)i);
                if(original!=null)Originals[i]=original;
            }
            foreach(var c in Classes)
            {
                c.Data=Object.Instantiate(basis);c.Data.name="AzerothClass_"+c.Id;
                c.Data.hideFlags=HideFlags.DontUnloadUnusedAsset;Owned.Add(c.Data);
                c.Data.eCharacter=(ECharacter)c.Index;
                c.Data.statModifiers=new Il2CppSystem.Collections.Generic.List<StatModifier>();
                c.Data.isEnabled=true;c.Data.price=0;c.Data.showInUnlocks=false;
                c.Data.achievementRequirement=null;c.Data.numQuestsRequiredForVisibilityInCharacterSelection=0;
                c.Passive=ScriptableObject.CreateInstance<PassiveData>();c.Passive.name="AzerothNeutralPassive_"+c.Id;
                c.Passive.hideFlags=HideFlags.DontUnloadUnusedAsset;c.Passive.ePassive=EPassive.None;
                c.Passive.localizedName=basis.passive.localizedName;c.Passive.localizedDescription=basis.passive.localizedDescription;
                c.Passive.Init(); // Native GetDescription requires its dummy passive to be initialized.
                c.Passive.icon=null;Owned.Add(c.Passive);c.Data.passive=c.Passive;
                c.Glyph=LoadIcon(c.Icon);c.Portrait=LoadPortrait(c.Id);c.Texture=c.Portrait??c.Glyph;
                c.SpellTexture=LoadIcon(c.StartingSpellIcon);c.TreeTexture=LoadIcon(c.TreeIcon);c.Data.icon=c.Texture;
                // All clones retain one common engine prefab/mesh/weapon placeholder. No original
                // character asset is mutated, and per-class native stat/perk sources are absent.
            }
            Ready=true;return true;
        }
        finally{Building=false;}
    }
    static Texture2D LoadIcon(string stem)
    {
        var roots=new[]{Path.Combine(Plugin.Root,"assets","Interface","Icons"),Path.Combine(Plugin.Root,"..","AzerothHud","assets","Interface","Icons")};
        foreach(var root in roots)
        {
            var raw=Path.Combine(root,stem+".rgba");var sizePath=Path.Combine(root,stem+".size");
            if(!File.Exists(raw)||!File.Exists(sizePath))continue;
            var size=File.ReadAllText(sizePath).Split(' ',StringSplitOptions.RemoveEmptyEntries);int w=int.Parse(size[0]),h=int.Parse(size[1]);
            return LoadRaw(raw,w,h,stem);
        }
        Plugin.Current.Log.LogWarning("Missing original class icon: "+stem);return null;
    }
    static Texture2D LoadPortrait(string classId)
    {
        var roots=new[]{Path.Combine(Paths.GameRootPath,"AzerothHeroAssets",classId),Path.Combine(Plugin.Root,"AzerothHeroAssets",classId),Path.Combine(Plugin.Root,"assets","portraits",classId)};
        foreach(var directory in roots)
        {
            var raw=Path.Combine(directory,"portrait.rgba");var metadata=raw+".json";if(!File.Exists(raw)||!File.Exists(metadata))continue;
            using var json=JsonDocument.Parse(File.ReadAllText(metadata));var row=json.RootElement;
            if(row.TryGetProperty("rowOrder",out var order)&&order.GetString()!="bottom-up")throw new InvalidDataException("Expected prepared bottom-up source portrait: "+raw);
            return LoadRaw(raw,row.GetProperty("width").GetInt32(),row.GetProperty("height").GetInt32(),"AzerothPortrait_"+classId);
        }
        Plugin.Current.Log.LogWarning("Missing original rendered class portrait: "+classId);return null;
    }
    static Texture2D LoadRaw(string raw,int width,int height,string name)
    {
        var bytes=File.ReadAllBytes(raw);if(width<=0||height<=0||bytes.Length!=checked(width*height*4))throw new InvalidDataException(raw);
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);texture.name=name;texture.hideFlags=HideFlags.DontUnloadUnusedAsset;
        var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);try{texture.LoadRawTextureData(pinned.AddrOfPinnedObject(),bytes.Length);texture.Apply(false,false);}finally{pinned.Free();}
        Owned.Add(texture);return texture;
    }
    internal static void RefreshButton(MyButtonCharacter button)
    {
        var source=ForClone(button?.characterData);if(source==null)return;
        if(button.t_name!=null)button.t_name.text=source.NameRu;
        if(button.i_icon!=null){button.i_icon.texture=source.Texture;button.i_icon.material=null;}
        button.canUseCharacter=true;button.cantUseCharacterReason="";button.SetInteractable(true);
        if(button.unavailableOverlay!=null)button.unavailableOverlay.SetActive(false);
        if(button.requiresPurchaseOverlay!=null)button.requiresPurchaseOverlay.SetActive(false);
    }
    internal static void Dispose()
    {
        // Existing native menu objects may outlive plugin unload. Rebind their original
        // data before releasing private Unity assets; a live run keeps its data until exit.
        foreach(var button in Resources.FindObjectsOfTypeAll<MyButtonCharacter>())
            if(button!=null && IsClone(button.characterData) && Originals.TryGetValue((int)button.characterData.eCharacter,out var original))button.SetCharacter(original);
        if(SourceInventory)
        {
            Plugin.Current.Log.LogWarning("Source inventory still owns private character assets; retain them until process exit.");
            return;
        }
        foreach(var value in Owned)if(value!=null)Object.Destroy(value);
        Owned.Clear();Originals.Clear();Classes.Clear();Ready=false;
    }
}

[HarmonyPatch(typeof(DataManager),nameof(DataManager.GetCharacterData))]
static class CharacterLookupPatch
{
    static bool Prefix(ECharacter __0,ref CharacterData __result)
    {
        if(Roster.Building || !Roster.Ensure())return true;
        __result=Roster.ForId(__0).Data;return false;
    }
    static void Postfix(ECharacter __0,CharacterData __result)
    {
        if(__result!=null && !Roster.IsClone(__result))Roster.Originals[(int)__0]=__result;
    }
}

[HarmonyPatch(typeof(CharacterMenu),"TryInit")]
static class NativeGridPatch
{
    static void Prefix(ref Il2CppSystem.Collections.Generic.List<CharacterData> __state)
    {
        if(!Roster.Ensure())return;
        var manager=DataManager.Instance;__state=manager.unsortedCharacterData;
        var source=new Il2CppSystem.Collections.Generic.List<CharacterData>();foreach(var c in Roster.Classes)source.Add(c.Data);
        manager.unsortedCharacterData=source;
        CharacterMenu.selectedCharacter=Roster.ExplicitClass!=null?(ECharacter)Roster.ForClass(Roster.ExplicitClass).Index: Roster.ForId(CharacterMenu.selectedCharacter).Data.eCharacter;
    }
    static Exception Finalizer(Exception __exception,Il2CppSystem.Collections.Generic.List<CharacterData> __state)
    {
        if(__state!=null && DataManager.Instance!=null)DataManager.Instance.unsortedCharacterData=__state;
        return __exception;
    }
}

[HarmonyPatch(typeof(MyButtonCharacter),nameof(MyButtonCharacter.SetCharacter))]
static class NativeButtonPatch {static void Postfix(MyButtonCharacter __instance)=>Roster.RefreshButton(__instance);}

[HarmonyPatch(typeof(CharacterMenu),"OnSelectCharacter")]
static class NativeSelectPatch
{
    static void Postfix(CharacterMenu __instance,MyButtonCharacter __0)
    {
        NativeAppearanceMask.SelectMenu(__instance,__0);
        var c=Roster.ForClone(__0?.characterData);if(c==null)return;
        Roster.LastSelected=c.Id;Roster.NativeSelections++;HeroBridge.SetFormForRuntime(0);HeroBridge.Changed(c.Id);
    }
}

[HarmonyPatch(typeof(CharacterInfoUI),"OnCharacterSelected")]
static class NativeInfoPatch
{
    static void Postfix(CharacterInfoUI __instance,MyButtonCharacter __0)
    {
        NativeAppearanceMask.SelectInfo(__instance,__0);
        var c=Roster.ForClone(__0?.characterData);if(c==null)return;
        if(__instance.t_name!=null)__instance.t_name.text=c.NameRu;
        if(__instance.t_description!=null)__instance.t_description.text=c.Description;
        if(__instance.i_character!=null)__instance.i_character.texture=c.Texture;
        if(__instance.t_weaponName!=null)__instance.t_weaponName.text=c.StartingSpellName;
        if(__instance.t_weaponDesc!=null)__instance.t_weaponDesc.text="Стартовое умение Azeroth Survivors: "+c.StartingSpellName;
        if(__instance.i_weapon!=null)__instance.i_weapon.texture=c.SpellTexture;
        if(__instance.t_passiveName!=null)__instance.t_passiveName.text="Таланты "+c.NameRu;
        if(__instance.t_passiveDesc!=null)__instance.t_passiveDesc.text=c.TreeNames;
        if(__instance.i_passive!=null)__instance.i_passive.texture=c.TreeTexture;
    }
}

[HarmonyPatch(typeof(MyPlayer),nameof(MyPlayer.StartPlayer))]
static class NativeStartPatch
{
    [HarmonyPriority(Priority.First)]
    static void Prefix(ref ECharacter __0,out bool __state)
    {
        __state=false;if(!Roster.Ensure())return;
        var c=Roster.ExplicitClass!=null?Roster.ForClass(Roster.ExplicitClass):Roster.ForId(__0);
        __0=(ECharacter)c.Index;CharacterMenu.selectedCharacter=__0;
        Roster.LastStarted=c.Id;Roster.NativeStarts++;HeroBridge.SetFormForRuntime(0);HeroBridge.Changed(c.Id);
        // Scope the native inventory constructor reached inside this method without
        // detouring an IL2CPP constructor. Native passive creation remains untouched.
        Roster.InventoryConstructionDepth++;__state=true;
    }
    static Exception Finalizer(Exception __exception,bool __state){if(__state)Roster.InventoryConstructionDepth--;return __exception;}
}

[HarmonyPatch(typeof(ActiveAbilityFactory),nameof(ActiveAbilityFactory.CreateAbility))]
static class NativeActivePatch
{
    static bool Prefix(ref ActiveAbility __result)
    {
        if(Roster.InventoryConstructionDepth==0)return true;
        Roster.NativeActivesBlocked++;__result=null;return false;
    }
}

[HarmonyPatch(typeof(PlayerInventory),nameof(PlayerInventory.HasPassive))]
static class NativePassiveQueryPatch
{
    static bool Prefix(PlayerInventory __instance,ref bool __result){if(!Roster.IsClone(__instance.characterData))return true;__result=false;return false;}
}

[HarmonyPatch(typeof(WeaponBase),nameof(WeaponBase.Use))]
static class SourceWeaponUsePatch
{
    static bool Prefix(){if(!Roster.SourceInventory)return true;Roster.NativeWeaponsBlocked++;return false;}
}

[HarmonyPatch(typeof(WeaponInventory),nameof(WeaponInventory.Tick))]
static class SourceWeaponTickPatch {static bool Prefix()=>!Roster.SourceInventory;}

[HarmonyPatch(typeof(WeaponInventory),nameof(WeaponInventory.AddWeapon))]
static class SourceWeaponGrantPatch
{
    static bool Prefix(WeaponInventory __instance)
    {
        if(!Roster.SourceInventory)return true;
        return __instance.weapons==null || __instance.weapons.Count==0;
    }
}

[HarmonyPatch(typeof(CharacterData),nameof(CharacterData.IsBlackedOutInCharacterSelectionScreen))]
static class RuntimeVisibilityPatch {static bool Prefix(CharacterData __instance,ref bool __result){if(!Roster.IsClone(__instance))return true;__result=false;return false;}}

[HarmonyPatch(typeof(UnlockableBase),nameof(UnlockableBase.GetName))]
static class RuntimeNamePatch
{
    static bool Prefix(UnlockableBase __instance,ref string __result){var c=Roster.ForClone(__instance.TryCast<CharacterData>());if(c==null)return true;__result=c.NameRu;return false;}
}

[HarmonyPatch(typeof(UnlockableBase),nameof(UnlockableBase.GetDescription))]
static class RuntimeDescriptionPatch
{
    static bool Prefix(UnlockableBase __instance,ref string __result){var c=Roster.ForClone(__instance.TryCast<CharacterData>());if(c==null)return true;__result=c.Description;return false;}
}

[HarmonyPatch(typeof(CharacterMenu),"OnConfirmCharacter")]
static class NativeConfirmPatch
{
    static void Postfix(MyButtonCharacter __0)
    {
        var c=Roster.ForClone(__0?.characterData);if(c==null)return;
        Roster.LastConfirmed=c.Id;Roster.NativeConfirmations++;
    }
}

[HarmonyPatch(typeof(MyAchievements),nameof(MyAchievements.IsUnlocked),new[]{typeof(UnlockableBase),typeof(string)},new[]{ArgumentType.Normal,ArgumentType.Out})]
static class RuntimeUnlockedPatch
{
    static bool Prefix(UnlockableBase __0,ref string __1,ref bool __result)
    {
        if(Roster.ForClone(__0?.TryCast<CharacterData>())==null)return true;
        __1="";__result=true;return false;
    }
}

[HarmonyPatch]
static class RuntimeAvailabilityPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        foreach(var name in new[]{"IsPurchased","IsAvailable","IsActivated"})
            yield return AccessTools.Method(typeof(MyAchievements),name,new[]{typeof(UnlockableBase)});
    }
    static bool Prefix(UnlockableBase __0,ref bool __result)
    {
        if(Roster.ForClone(__0?.TryCast<CharacterData>())==null)return true;
        __result=true;return false;
    }
}
