using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Menu.Shop;
using UnityEngine;

namespace AzerothAbilities;

internal static class TomeCompatibility
{
    // WeaponUtility.GetAttackQuantity (RVA4346F0) truncates the PLAYER bonus,
    // then adds it to the weapon/source quantity. RoundToInt is not equivalent.
    internal static int ExtraProjectiles=>(int)AbilityRuntime.Stat(EStat.Projectiles);
    internal static int Quantity(LearnedSpell spell)=>ClassCombat.GenericActive?ClassSpellBehaviors.Quantity(spell):Math.Max(1,spell.Definition.Targets+spell.ExtraTargets+ExtraProjectiles);
    internal static int ExtraBounces=>Math.Max(0,(int)AbilityRuntime.Stat(EStat.ProjectileBounces));
    internal static float Size=>Mathf.Max(.1f,AbilityRuntime.Stat(EStat.SizeMultiplier));
    internal static float ProjectileSpeed=>Mathf.Max(.1f,AbilityRuntime.Stat(EStat.ProjectileSpeedMultiplier));
    internal static float Duration=>Mathf.Max(.1f,AbilityRuntime.Stat(EStat.DurationMultiplier));
    // The Duration tome changes DurationMultiplier (base1). The similarly named
    // EffectDurationMultiplier has native base0 and is not this tome's stat.
    internal static float EffectDuration=>Duration;
    internal static float SpellDuration(LearnedSpell spell)=>ClassCombat.GenericActive?ClassSpellBehaviors.Duration(spell):spell.Definition.Duration*(AbilityRuntime.Catalog?.DurationScale??1)*spell.DurationFactor*Duration;
    internal static void ApplyCritical(DamageContainer dc)
    {
        // Native DamageUtility handles chance over100% and its extra multiplier.
        // Damage already contains global DamageMultiplier; never call native
        // GetDamageContainer here, because that applies the same multiplier again.
        dc.crit=DamageUtility.GetCritDamageMultiplier(Mathf.Max(0,AbilityRuntime.Stat(EStat.CritChance)),out var multiplier);
        if(dc.crit)dc.damage*=multiplier*Mathf.Max(0,AbilityRuntime.Stat(EStat.CritDamage));
    }
    internal static Vector3 LaunchOffset(Vector3 origin,Vector3 toward,int index,int count)
    {
        if(count<=1)return origin;
        var forward=toward-origin;forward.y=0;if(forward.sqrMagnitude<.001f)forward=Vector3.forward;
        var right=Vector3.Cross(Vector3.up,forward.normalized);
        // Separate damaging projectiles visibly even when only one enemy is in range.
        return origin+right*((index-(count-1)*.5f)*.28f*Size);
    }
}
