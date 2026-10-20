using System.Reflection;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using Assets.Scripts.Inventory__Items__Pickups.Weapons.Attacks;
using Assets.Scripts.Inventory__Items__Pickups.Weapons.Projectiles;
using Assets.Scripts.Game.Combat.ConstantAttacks;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.MageVisuals;

internal static class NativeWeaponVisuals
{
    static readonly Dictionary<IntPtr,(Renderer Renderer,bool Enabled,string Source)> hidden=new();
    static Type bridge;
    internal static bool Active
    {
        get{bridge??=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothAbilities.HudBridge")).FirstOrDefault(t=>t!=null);return HeroAppearance.HasActiveSourceClass || (bridge!=null && (bool)(bridge.GetProperty("Active")?.GetValue(null)??false));}
    }
    internal static int Count=>hidden.Values.Count(x=>x.Renderer!=null);
    internal static int EnabledCount=>hidden.Values.Count(x=>x.Renderer!=null && x.Renderer.enabled);
    internal static int VisibleCount=>hidden.Values.Count(x=>x.Renderer!=null && x.Renderer.enabled && x.Renderer.gameObject.activeInHierarchy && !x.Renderer.forceRenderingOff);
    static bool Owned(WeaponBase weapon)
    {
        var inventory=MyPlayer.Instance?.inventory?.weaponInventory;
        if(weapon==null || inventory?.weapons==null)return false;
        foreach(var entry in inventory.weapons)if(entry.Value!=null && entry.Value.Pointer==weapon.Pointer)return true;
        return false;
    }
    internal static void Suppress(Transform node,string source)
    {
        if(!Active || node==null || !node.gameObject.scene.IsValid() || node.name.StartsWith("Azeroth",StringComparison.Ordinal) || NativeMapMarkers.IsProtected(node))return;
        var r=node.GetComponent<Renderer>();
        if(r!=null)
        {
            if(!hidden.ContainsKey(r.Pointer))hidden[r.Pointer]=(r,r.enabled,source);
            r.enabled=false;
        }
        for(int i=0;i<node.childCount;i++)Suppress(node.GetChild(i),source);
    }
    internal static void OnAttack(WeaponAttack attack)
    {
        var local=MyPlayer.Instance;
        if(attack==null || !Active || local==null || (attack.player!=local && !Owned(attack.weaponBase)))return;
        Suppress(attack.transform,"WeaponAttack.player/weaponBase");
        if(attack.muzzle!=null)Suppress(attack.muzzle.transform,"WeaponAttack.muzzle");
    }
    internal static void OnConstant(ConstantAttack attack){if(attack!=null && Owned(attack.weaponBase))Suppress(attack.transform,"ConstantAttack.weaponBase");}
    internal static void OnProjectile(ProjectileBase projectile){if(projectile!=null && Owned(projectile.weaponBase))Suppress(projectile.transform,"ProjectileBase.weaponBase");}
    internal static void Scan()
    {
        if(!Active)return;
        foreach(var attack in Resources.FindObjectsOfTypeAll<WeaponAttack>())OnAttack(attack);
        foreach(var attack in Resources.FindObjectsOfTypeAll<ConstantAttack>())OnConstant(attack);
        foreach(var projectile in Resources.FindObjectsOfTypeAll<ProjectileBase>())OnProjectile(projectile);
    }
    internal static void Tick()
    {
        if(!Active){Restore();return;}
        foreach(var key in hidden.Keys.ToArray())
        {
            var state=hidden[key];if(state.Renderer==null){hidden.Remove(key);continue;}
            if(NativeMapMarkers.IsProtected(state.Renderer.transform)){if(state.Enabled)state.Renderer.enabled=true;hidden.Remove(key);continue;}
            state.Renderer.enabled=false;
        }
    }
    internal static object Describe()=>hidden.Values.Where(x=>x.Renderer!=null).Select(x=>new{source=x.Source,path=Path(x.Renderer.transform),layer=x.Renderer.gameObject.layer,enabled=x.Renderer.enabled,active=x.Renderer.gameObject.activeInHierarchy}).ToArray();
    static string Path(Transform node){var names=new List<string>();for(var t=node;t!=null;t=t.parent)names.Add(t.name);names.Reverse();return string.Join("/",names);}
    internal static void Restore(){foreach(var state in hidden.Values)if(state.Renderer!=null && state.Enabled)state.Renderer.enabled=true;hidden.Clear();}
}

public sealed class MageWeaponVisibility:MonoBehaviour
{
    public MageWeaponVisibility(IntPtr p):base(p){}
    float nextScan,nextFailureLog;
    void LateUpdate()
    {
        try{if(NativeWeaponVisuals.Active && Time.unscaledTime>=nextScan){nextScan=Time.unscaledTime+.2f;NativeWeaponVisuals.Scan();}NativeWeaponVisuals.Tick();}
        catch(Exception ex){if(Time.unscaledTime>=nextFailureLog){nextFailureLog=Time.unscaledTime+5;Plugin.Logger.LogWarning("Native weapon visual scan will retry: "+ex);}}
    }
    void OnDestroy(){NativeWeaponVisuals.Restore();}
}
[HarmonyPatch(typeof(WeaponAttack),nameof(WeaponAttack.SetAttack))]
static class NativeWeaponAttackVisualPatch{static void Postfix(WeaponAttack __instance)=>NativeWeaponVisuals.OnAttack(__instance);}
[HarmonyPatch(typeof(ConstantAttack),nameof(ConstantAttack.Set))]
static class NativeConstantWeaponVisualPatch{static void Postfix(ConstantAttack __instance)=>NativeWeaponVisuals.OnConstant(__instance);}
[HarmonyPatch(typeof(ProjectileBase),nameof(ProjectileBase.Set))]
static class NativeWeaponProjectileVisualPatch{static void Postfix(ProjectileBase __instance)=>NativeWeaponVisuals.OnProjectile(__instance);}
[HarmonyPatch(typeof(WeaponInventory),nameof(WeaponInventory.AddWeapon))]
static class NativeWeaponInventoryVisualPatch{static void Postfix(){if(NativeWeaponVisuals.Active)NativeWeaponVisuals.Scan();}}
