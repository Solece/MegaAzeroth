using Assets.Scripts.Actors.Player;
using Assets.Scripts.Utility;
using UnityEngine;

namespace AzerothAbilities;

// Unity's clock keeps running while Megabonk reward/shop windows pause MyTime.
// All source abilities, talent procs and VFX share this clock instead.
public static class CombatSimulation
{
    private static int frame=-1;
    private static float time,delta;
    public static string PauseReason
    {
        get
        {
            if(!AbilityRuntime.Active)return "class-inactive";
            var player=MyPlayer.Instance;var game=GameManager.Instance;
            if(player==null || player.inventory==null || !player.hasStarted)return "no-started-player";
            if(game==null || game.isGameOver || !game.IsPlaying())return "run-not-playing";
            if(game.cutscene)return "cutscene";
            if(MyTime.paused)return "native-pause";
            if(UnityEngine.Time.timeScale<=0 || MyTime.timeScale<=0)return "timescale-zero";
            if(player.inventory.pause)return "inventory-pause";
            if(LevelupScreen.isLevelingUp)return "levelup-choice";
            var ui=game.uiManager??UiManager.Instance;
            if(ui!=null)
            {
                if(ui.pause!=null && ui.pause.IsPaused())return "pause-menu";
                if(ui.encounterWindows!=null && ui.encounterWindows.encounterInProgress)return "encounter-choice";
            }
            return "";
        }
    }
    public static bool CanSimulate=>PauseReason.Length==0;
    private static void Step()
    {
        if(frame==UnityEngine.Time.frameCount)return;
        frame=UnityEngine.Time.frameCount;
        delta=CanSimulate?Mathf.Max(0,MyTime.deltaTime):0;
        time+=delta;
    }
    public static float Time {get{Step();return time;}}
    public static float DeltaTime {get{Step();return CanSimulate?delta:0;}}
    internal static void Reset(){time=delta=0;frame=UnityEngine.Time.frameCount;}
}
