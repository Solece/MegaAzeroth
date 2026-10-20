using System.Text.Json;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Utility;
using UnityEngine;

namespace AzerothMegabonk.MageVisuals;

// Animation-only accumulated time. Never writes native time, input or physics.
// MyTime.time itself still advances in slow motion; stopped intervals must not
// be added later when the scene resumes.
internal static class MageAnimationClock
{
    static double seconds;
    static int sampledFrame=-1;
    static bool canAnimate,nativePaused,uiPaused,inventoryPaused,encounter,levelup,inBattle;
    static float nativeScale,unityScale,nativeDelta;
    static string blocked="not-started";
    internal static float Now {get{Sample();return (float)seconds;}}
    internal static bool CanAnimate {get{Sample();return canAnimate;}}
    static void Sample()
    {
        var player=MyPlayer.Instance;var ui=UiManager.Instance;
        nativePaused=MyTime.paused;nativeScale=MyTime.timeScale;unityScale=Time.timeScale;nativeDelta=MyTime.deltaTime;
        inBattle=player!=null && player.hasStarted && player.inventory!=null && GameManager.Instance!=null && !GameManager.Instance.isGameOver;
        inventoryPaused=player?.inventory!=null && player.inventory.pause;
        uiPaused=ui?.pause!=null && ui.pause.IsPaused();encounter=ui?.encounterWindows!=null && ui.encounterWindows.encounterInProgress;
        levelup=LevelupScreen.isLevelingUp;
        blocked=!inBattle?"not-in-battle":nativePaused?"native-pause":uiPaused?"pause-menu":inventoryPaused?"inventory-pause":encounter?"encounter":levelup?"levelup":nativeScale<.999f?"native-slow-motion":unityScale<.999f?"unity-slow-motion":"";
        canAnimate=blocked.Length==0 && float.IsFinite(nativeDelta) && nativeDelta>=0;
        int frame=Time.frameCount;if(sampledFrame==frame)return;sampledFrame=frame;
        if(canAnimate)seconds+=Math.Max(0,nativeDelta);
    }
    internal static object Snapshot()
    {
        Sample();return new{clock=seconds,canAnimate,nativePaused,nativeScale,unityScale,nativeDelta,uiPaused,inventoryPaused,encounter,levelup,inBattle,blocked,sampledFrame};
    }
    internal static string Describe()=>JsonSerializer.Serialize(Snapshot());
}
