using HarmonyLib;

namespace AzerothHeroes;

// Private source passives are neutral runtime shells, not Amog's native passive.
// GetName shares native RVA0x381570 with StageData.GetName, already patched by
// Elwynn. Keep source names in CharacterInfoUI and hook only unique GetDescription.
// Skip native SmartFormat only for exact owned PassiveData pointers: its None
// implementation cannot supply the borrowed Amog value1/stat1 placeholders.
[HarmonyPatch(typeof(PassiveData),nameof(PassiveData.GetDescription))]
static class SourcePassiveDescriptionPatch
{
    static bool Prefix(PassiveData __instance,ref string __result)
    {
        var source=Roster.ForPassive(__instance);if(source==null)return true;
        __result=source.TreeNames;return false;
    }
}
