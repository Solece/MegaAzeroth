using System.Reflection;
using System.Text.Json;
using AzerothAbilities;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Managers;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothMegabonk.AdventureFlow;

// Batch-only regression for native asset collection during actual stage travel.
internal static class VisualTransitionProbe
{
    internal static bool Enabled=>Application.isBatchMode && Environment.GetCommandLineArgs().Contains("-azeroth-visual-smoke");
    static readonly string[] ids={"fireball","frostbolt","arcane_missiles","frost_nova","arcane_explosion","blizzard","flamestrike","fire_blast","cone_of_cold","blast_wave","pyroblast","ice_barrier","polymorph"};
    static readonly HashSet<string> stages=new(),bosses=new();
    static string key="",bossKey="";static int phase,bossPhase;static float wait,deadline,bossWait;
    static AsyncOperation unloading;
    static Enemy scorpion;
    static Type TypeNamed(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).First(t=>t!=null);
    static object Call(string type,string method,params object[] args)=>TypeNamed(type).GetMethod(method,BindingFlags.Public|BindingFlags.Static).Invoke(null,args);
    static string BodyAssets()=>(string)Call("AzerothMegabonk.MageVisuals.MageVisualBridge","AssetDiagnostics");
    static string EnemyAssets()=>(string)Call("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge","AssetDiagnostics");

    internal static bool StageReady()
    {
        if(!Enabled)return true;
        string current=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name+"-"+MapController.GetStageIndex()+"-"+MapController.IsFinalBossStage();
        if(stages.Contains(current))return true;
        if(key!=current){key=current;phase=0;deadline=Time.realtimeSinceStartup+45;}
        if(Time.realtimeSinceStartup>deadline)throw new InvalidOperationException("Visual stage probe timed out: "+key+" phase="+phase);
        var player=MyPlayer.Instance;
        if(phase==0)
        {
            var bridge=TypeNamed("AzerothMegabonk.MageVisuals.MageVisualBridge");
            if(!(bool)bridge.GetProperty("IsAttached").GetValue(null))return false;
            // Stage generation can finish in Update before the source skin's
            // first LateUpdate rebinds to the new native player. Wait for the
            // active source instance, with the existing stage deadline intact.
            using(var ready=JsonDocument.Parse((string)Call("AzerothMegabonk.MageVisuals.MageVisualBridge","WeaponDiagnostics")))
                if(!ready.RootElement.GetProperty("attached").GetBoolean() || !ready.RootElement.GetProperty("active").GetBoolean())return false;
            foreach(var id in ids)if(!HudBridge.LearnSpellForProbe(id))throw new InvalidOperationException("Cannot exercise source spell "+id);
            // Reserve the effect budget during this batch-only cleanup probe.
            // Existing projectiles and their lifetimes continue ticking normally.
            foreach(var spell in AbilityRuntime.Spells)spell.NextCast=Time.time+15;
            if(!SpellVfxDiagnostics.PrimeAllAssetsForProbe() || !(bool)Call("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge","PrimeAllModelsForProbe"))throw new InvalidOperationException("Cannot prime original model caches");
            var sheep=(GameObject)Call("AzerothMegabonk.MageVisuals.CreatureVisualBridge","Create","sheep",player.transform.position+Vector3.right*8,.8f);
            Call("AzerothMegabonk.MageVisuals.CreatureVisualBridge","Destroy",sheep);
            if(!NativeWeaponDiagnostics.AttemptBlockedGrantForProbe())throw new InvalidOperationException("Native weapon grant bypassed Mage policy");
            Validate("before explicit cleanup");wait=Time.realtimeSinceStartup+1;phase=1;return false;
        }
        if(Time.realtimeSinceStartup<wait)return false;
        if(phase==1){unloading=Resources.UnloadUnusedAssets();phase=2;Plugin.Note("VISUAL forced native UnloadUnusedAssets queued: "+key);return false;}
        if(phase==2)
        {
            if(!unloading.isDone)return false;
            Validate("immediately after cleanup");
            int spawned=HudBridge.SourceVfxSpawned;
            foreach(var id in new[]{"fireball","frost_nova","arcane_missiles","blizzard"})
                SpellVfxDiagnostics.PlayForProbe(id,"cast",player.transform.position+Vector3.up*1.5f,.6f);
            if(HudBridge.SourceVfxSpawned<=spawned)throw new InvalidOperationException("No new original spell visuals after cleanup");
            wait=Time.realtimeSinceStartup+.2f;phase=3;return false;
        }
        Validate("rendered after cleanup");
        Capture("scene-visual-"+Safe(key)+".ppm");foreach(var spell in AbilityRuntime.Spells)spell.NextCast=Time.time;stages.Add(key);Plugin.Note("PASS VISUAL STAGE "+key+"; all13 spells retained; original body/cache/VFX survived native cleanup");return true;
    }
    internal static bool BossReady(Enemy boss)
    {
        if(!Enabled)return true;
        if(boss==null)return false;
        string current=key+":"+boss.id+":"+boss.Pointer;
        if(bosses.Contains(current))return true;
        if(bossKey!=current)
        {
            bossKey=current;bossPhase=0;
            boss.MakeInvulnerable(true);PlacePlayerForBoss(boss);bossWait=Time.realtimeSinceStartup+1.5f;return false;
        }
        if(Time.realtimeSinceStartup<bossWait)return false;
        if(bossPhase==0)
        {
            string description=(string)Call("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge","Describe",boss);
            AssertBoss(description);Plugin.Note("VISUAL live native boss "+description);Validate("live boss after transition");
            Capture("boss-visual-"+Safe(key)+".ppm");
            if(MapController.GetStageIndex()==0 && !MapController.IsFinalBossStage())
            {
                var cam=Camera.main;var dir=cam.transform.forward;dir.y=0;dir.Normalize();
                var p=MyPlayer.Instance.transform.position+dir*28;var mask=GameManager.Instance.whatIsGroundAndObjects;
                if(Physics.Raycast(p+Vector3.up*100,Vector3.down,out var hit,220,mask,QueryTriggerInteraction.Ignore))p=hit.point;
                scorpion=EnemyManager.Instance.SpawnBoss((Actors.Enemies.EEnemy)27,-1,EEnemyFlag.Boss,p,1);
                if(scorpion==null)throw new InvalidOperationException("Native miniboss Scorpion failed to spawn");
                scorpion.MakeInvulnerable(true);PlacePlayerForBoss(scorpion);bossWait=Time.realtimeSinceStartup+1.5f;bossPhase=1;return false;
            }
        }
        if(bossPhase==1)
        {
            string description=(string)Call("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge","Describe",scorpion);
            AssertBoss(description);Plugin.Note("VISUAL user-reported spider boss "+description);
            Capture("boss-scorpion-ground.ppm");
            scorpion.MakeInvulnerable(false);scorpion.Kill("AzerothVisualRegressionProbe");
        }
        boss.MakeInvulnerable(false);bosses.Add(current);Plugin.Note("PASS VISUAL BOSS "+current);return true;
    }
    static void AssertBoss(string description)
    {
        using var d=JsonDocument.Parse(description);var j=d.RootElement;
        if(!j.GetProperty("visualActive").GetBoolean() || !j.GetProperty("modelAssetsValid").GetBoolean() || !j.GetProperty("canVisible").GetBoolean() || (j.GetProperty("groundFound").GetBoolean() && j.GetProperty("groundClearance").GetSingle()<-.1f))throw new InvalidOperationException("Boss invisible or below ground: "+description);
    }
    static void PlacePlayerForBoss(Enemy boss)
    {
        var cam=Camera.main;var dir=cam.transform.forward;dir.y=0;if(dir.sqrMagnitude<.01f)dir=Vector3.forward;dir.Normalize();
        float distance=Mathf.Max(22,boss.GetHeight()*1.6f);
        var p=boss.transform.position-dir*distance;
        if(Physics.Raycast(p+Vector3.up*100,Vector3.down,out var hit,220,GameManager.Instance.whatIsGroundAndObjects,QueryTriggerInteraction.Ignore))p=MyPlayer.Instance.playerMovement.FeetPositionToRb(hit.point);
        else p.y=MyPlayer.Instance.transform.position.y;
        MyPlayer.Instance.TeleportPlayerImmediate(p,dir,dir,15);
    }
    static void Validate(string label)
    {
        var body=BodyAssets();var enemy=EnemyAssets();var effects=SpellVfxDiagnostics.SnapshotJson;var weapons=NativeWeaponDiagnostics.SnapshotJson;var appearance=(string)Call("AzerothMegabonk.MageVisuals.MageVisualBridge","WeaponDiagnostics");
        Plugin.Note("VISUAL "+label+" body="+body);Plugin.Note("VISUAL "+label+" enemies="+enemy);Plugin.Note("VISUAL "+label+" spellEffects="+effects);
        Plugin.Note("VISUAL "+label+" nativeWeaponPolicy="+weapons);Plugin.Note("VISUAL "+label+" sourceWeapon="+appearance);
        using var policy=JsonDocument.Parse(weapons);using var a=JsonDocument.Parse(appearance);
        if(!policy.RootElement.GetProperty("NativeAttacksSuppressed").GetBoolean() || policy.RootElement.GetProperty("EnginePlaceholderCount").GetInt32()!=1 || policy.RootElement.GetProperty("TicksBlocked").GetInt32()==0 || policy.RootElement.GetProperty("VisibleNativeWeaponCards").GetInt32()!=0)throw new InvalidOperationException("Native weapon is available to the Mage: "+weapons);
        if(a.RootElement.GetProperty("sourceItemId").GetInt32()!=35 || a.RootElement.GetProperty("sourceDisplayId").GetInt32()!=472 || !a.RootElement.GetProperty("attached").GetBoolean() || !a.RootElement.GetProperty("active").GetBoolean() || !a.RootElement.GetProperty("sourceAssetsValid").GetBoolean() || a.RootElement.GetProperty("visibleNativeWeaponRenderers").GetInt32()!=0)throw new InvalidOperationException("Mage weapon appearance is incorrect: "+appearance);
        using var b=JsonDocument.Parse(body);using var e=JsonDocument.Parse(enemy);using var v=JsonDocument.Parse(effects);
        if(!b.RootElement.GetProperty("allValid").GetBoolean())throw new InvalidOperationException("Mage cache lost original model/texture/frames");
        if(!e.RootElement.GetProperty("allValid").GetBoolean())throw new InvalidOperationException("Enemy cache lost original model/texture/frames");
        foreach(var n in new[]{"InvalidMeshes","InvalidTextures","InvalidMaterials","FailedEffects"})if(v.RootElement.GetProperty(n).GetInt32()!=0)throw new InvalidOperationException("Source VFX lost native resources: "+n);
        if(v.RootElement.GetProperty("PinnedMeshes").GetInt32()!=v.RootElement.GetProperty("CachedMeshes").GetInt32() || v.RootElement.GetProperty("PinnedTextures").GetInt32()!=v.RootElement.GetProperty("CachedTextures").GetInt32())throw new InvalidOperationException("Source VFX caches are not protected from native cleanup");
    }
    static string Safe(string value)=>value.Replace(':','-').Replace('/','-');
    static void Capture(string name)
    {
        MyPlayer.Instance.minimapCamera?.Render();
        if(!(bool)Call("AzerothMegabonk.TestBattle.CaptureBridge","Capture",name))throw new InvalidOperationException("Native camera capture failed");
    }
}
