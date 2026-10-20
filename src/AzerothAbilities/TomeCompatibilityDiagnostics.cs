using System.Text.Json;
using Assets.Scripts._Data.Tomes;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Menu.Shop;
using Assets.Scripts.Utility;
using UnityEngine;

namespace AzerothAbilities;

// Read snapshots on the Unity thread. Mutating helpers require an explicitly
// armed batch test and only change its in-memory inventory.
public static class TomeCompatibilityDiagnostics
{
    internal static bool SuppressAutomaticCasting;
    private static int created,visuals,hits,bounces,criticalHits;
    private static object lastHit;
    private static TomeInventory originalTomes,probeTomes;
    private static IntPtr probeInventory;
    private static bool ProbeAllowed=>Application.isBatchMode && Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1" && AbilityRuntime.Active;
    internal static void Reset(){created=visuals=hits=bounces=criticalHits=0;lastHit=null;}
    internal static void ProjectileCreated()=>created++;
    internal static void ProjectileVisualCreated()=>visuals++;
    internal static void ProjectileHit()=>hits++;
    internal static void ProjectileBounced()=>bounces++;
    internal static void RecordHit(LearnedSpell spell,Enemy target,DamageContainer dc)
    {
        if(dc.crit)criticalHits++;
        lastHit=new{Spell=spell.Definition.Id,Enemy=target.id,RequestedDamage=dc.damage,Critical=dc.crit,Knockback=dc.knockback,CombatTime=CombatSimulation.Time};
    }
    public static string SnapshotJson=>JsonSerializer.Serialize(new{
        CanSimulate=CombatSimulation.CanSimulate,PauseReason=CombatSimulation.PauseReason,CombatTime=CombatSimulation.Time,CombatDelta=CombatSimulation.DeltaTime,
        NativeTime=MyTime.time,NativeDelta=MyTime.deltaTime,NativePaused=MyTime.paused,UnityTime=Time.time,UnityTimeScale=Time.timeScale,
        NativeStats=NativeStats(),
        CreatedProjectiles=created,CreatedProjectileVisuals=visuals,ProjectileHits=hits,ProjectileBounces=bounces,CriticalHits=criticalHits,LastHit=lastHit,
        Casts=AbilityRuntime.CastCount,Hits=AbilityRuntime.HitCount,Damage=AbilityRuntime.DamageDealt,SuppressAutomaticCasting,
        Slots=InventoryUtility.GetNumAvailableTomeSlots(),
        Spells=AbilityRuntime.Spells.Select(s=>new{Id=s.Definition.Id,s.Rank,Damage=AbilityRuntime.Damage(s),Cooldown=AbilityRuntime.Cooldown(s),CooldownRemaining=Mathf.Max(0,s.NextCast-CombatSimulation.Time),QuantityAffected=s.Definition.Kind is "projectile" or "missiles" or "instant",Quantity=s.Definition.Kind is "projectile" or "missiles" or "instant"?TomeCompatibility.Quantity(s):1,Radius=s.Definition.Radius*s.RadiusFactor*MageSpellBehaviors.RadiusScale(s)*TomeCompatibility.Size,Duration=TomeCompatibility.SpellDuration(s)}).ToArray(),
        Projectiles=AbilityRuntime.Projectiles.Select(p=>p.Snapshot()).ToArray(),Behaviors=MageSpellBehaviors.Snapshot(),
        Vfx=JsonSerializer.Deserialize<JsonElement>(SpellVfx.ResourceSnapshot())});
    private static Dictionary<string,float> NativeStats()
    {
        var stats=new Dictionary<string,float>();foreach(var stat in Enum.GetValues<EStat>())stats[stat.ToString()]=AbilityRuntime.Stat(stat);return stats;
    }
    public static string NativeTomeCatalogJson=>JsonSerializer.Serialize(Resources.FindObjectsOfTypeAll<TomeData>().Where(t=>t!=null).OrderBy(t=>(int)t.eTome).Select(t=>new{
        Id=t.eTome.ToString(),Stat=t.statModifier?.stat.ToString(),ModifyType=t.statModifier?.modifyType.ToString(),Value=t.statModifier?.modification,
        Offers=Enumerable.Range(0,6).Select(r=>new{Rarity=r,Values=OfferSnapshot(t,r)}).ToArray()}).ToArray());
    private static object[] OfferSnapshot(TomeData data,int rarity)
    {
        var offer=data.GetUpgradeOffer((ERarity)rarity);var values=new List<object>();
        if(offer!=null)foreach(var m in offer)values.Add(new{Stat=m.stat.ToString(),Type=m.modifyType.ToString(),Value=m.modification});
        return values.ToArray();
    }
    public static bool SetAutomaticCastingForProbe(bool enabled)
    {if(!ProbeAllowed)return false;SuppressAutomaticCasting=!enabled;return true;}
    public static bool GrantTomeForProbe(string id,int rarity=1,int levels=1)
    {
        if(!ProbeAllowed || levels<1 || levels>30 || rarity<0 || rarity>5 || !Enum.TryParse<ETome>(id,true,out var tome))return false;
        var inventory=MyPlayer.Instance?.inventory;if(inventory?.tomeInventory==null)return false;
        var data=Resources.FindObjectsOfTypeAll<TomeData>().FirstOrDefault(t=>t!=null && t.eTome==tome);if(data==null)return false;
        if(!inventory.tomeInventory.HasTome(tome) && inventory.tomeInventory.GetNumTomes()>=InventoryUtility.GetNumAvailableTomeSlots())return false;
        for(int i=0;i<levels;i++)
        {
            if(inventory.tomeInventory.GetTomeLevel(tome)>=data.GetMaxLevel())break;
            inventory.tomeInventory.AddTome(data,data.GetUpgradeOffer((ERarity)rarity),(ERarity)rarity);
        }
        RefreshNativeStats(inventory);
        Plugin.Current?.Log.LogInfo($"Native tome probe grant: {tome},level={inventory.tomeInventory.GetTomeLevel(tome)},stat={data.statModifier.stat},native={AbilityRuntime.Stat(data.statModifier.stat)}");
        return inventory.tomeInventory.HasTome(tome);
    }
    public static bool UseIsolatedTomeForProbe(string id,int levels=1,int rarity=1)
    {
        if(!ProbeAllowed)return false;var inventory=MyPlayer.Instance?.inventory;if(inventory?.tomeInventory==null)return false;
        if(originalTomes==null){originalTomes=inventory.tomeInventory;probeInventory=inventory.Pointer;}
        if(probeInventory!=inventory.Pointer)return false;
        inventory.tomeInventory=probeTomes=new TomeInventory();RefreshNativeStats(inventory);
        return levels==0 || GrantTomeForProbe(id,rarity,levels);
    }
    public static bool RestoreOriginalTomesForProbe()
    {
        if(!ProbeAllowed || originalTomes==null)return false;var inventory=MyPlayer.Instance?.inventory;
        if(inventory==null || inventory.Pointer!=probeInventory)return false;
        inventory.tomeInventory=originalTomes;originalTomes=probeTomes=null;probeInventory=IntPtr.Zero;RefreshNativeStats(inventory);return true;
    }
    private static void RefreshNativeStats(PlayerInventory inventory)
    {
        // Real AddTome queues its stat through A_TomeUpgrade. Queue all keys too
        // when replacing/restoring the isolated test inventory.
        foreach(var stat in Enum.GetValues<EStat>())inventory.playerStats.QueueUpdateStat(stat);
        inventory.playerStats.ForceUpdateStats();
    }
    public static bool CastSpellForProbe(string id)
    {if(!ProbeAllowed)return false;return AbilityRuntime.CastFreeSpell(id);}
    public static bool LaunchSlowProjectileForProbe(string id="fireball",float speed=.25f)
    {
        if(!ProbeAllowed || !CombatSimulation.CanSimulate || speed<=0)return false;
        var player=MyPlayer.Instance;var definition=AbilityRuntime.Catalog.Spells.FirstOrDefault(s=>s.Id==id);
        if(player==null || definition==null)return false;
        var target=AbilityRuntime.Targets(player.transform.position,definition.Range,true).FirstOrDefault();if(target==null)return false;
        var spell=AbilityRuntime.Spells.FirstOrDefault(s=>s.Definition.Id==id)??new LearnedSpell(definition);
        AbilityRuntime.Projectiles.Add(new SpellProjectile(spell,target,player.transform.position+Vector3.up*.8f,AbilityRuntime.Damage(spell),speed));return true;
    }
    public static bool ClearProjectilesForProbe()
    {
        if(!ProbeAllowed)return false;
        foreach(var projectile in AbilityRuntime.Projectiles)projectile.Dispose();AbilityRuntime.Projectiles.Clear();return true;
    }
}
