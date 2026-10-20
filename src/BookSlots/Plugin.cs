using Assets.Scripts.Inventory__Items__Pickups;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace AzerothMegabonk.BookSlots;

[BepInPlugin(PluginId, "Azeroth Megabonk - Four Tome Slots", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    public const string PluginId = "local.azerothmegabonk.bookslots";
    internal static ManualLogSource? ModLog;
    private Harmony? _harmony;

    public override void Load()
    {
        ModLog = Log;
        _harmony = new Harmony(PluginId);
        _harmony.PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("Four tome slots enabled at runtime. No save changes. Weapon slots retain native behavior.");
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        return true;
    }
}

[HarmonyPatch(typeof(InventoryUtility), nameof(InventoryUtility.GetNumAvailableTomeSlots))]
internal static class FourTomeSlotsPatch
{
    internal const int TomeSlotCount = 4;
    private static bool _reported;

    [HarmonyPrefix]
    private static bool Prefix(ref int __result)
    {
        __result = TomeSlotCount;
        if (!_reported)
        {
            _reported = true;
            Plugin.ModLog?.LogInfo("Verified GetNumAvailableTomeSlots override = 4 (first runtime call).");
        }
        return false;
    }
}
