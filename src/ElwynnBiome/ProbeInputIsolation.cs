using System.Reflection;
using HarmonyLib;

namespace AzerothMegabonk.ElwynnBiome;

// A real keyboard used in the visible game must not close native windows in
// this separate batch test process. Direct native probe actions remain usable.
[HarmonyPatch]
static class ProbeButtonIsolation
{
    static bool Prepare()=>Plugin.FeatureSmoke;
    static IEnumerable<MethodBase> TargetMethods()=>new[]{"GetButton","GetButtonDown","GetButtonUp"}.Select(n=>AccessTools.Method(typeof(MyInputManager),n,new[]{typeof(string)}));
    static bool Prefix(ref bool __result){__result=false;return false;}
}
[HarmonyPatch(typeof(MyInputManager),nameof(MyInputManager.GetAxis),new[]{typeof(string)})]
static class ProbeAxisIsolation
{
    static bool Prepare()=>Plugin.FeatureSmoke;
    static bool Prefix(ref float __result){__result=0;return false;}
}
