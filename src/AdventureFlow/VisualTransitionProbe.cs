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
    internal static bool Enabled=>Application.isBatchMode && Environment.GetEnvironmentVariable("AZEROTH_LAB_HEADLESS_TEST")=="1" && Environment.GetCommandLineArgs().Contains("-azeroth-visual-smoke");
    internal static SpellCatalog ActiveCatalog=>JsonSerializer.Deserialize<SpellCatalog>(ClassAbilityDiagnostics.ActiveSourceCatalogJson,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Public active source catalog is absent");
    static string[] Ids=>ActiveCatalog.Spells.Select(s=>s.Id).ToArray();
    static readonly HashSet<string> stages=new(),bosses=new();
    static string seededClass;
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
            if(!EquipmentReady() || !PetsReady(false))return false;
            if(seededClass==null)
            {
                if(!ClassAbilityDiagnostics.LearnAllForProbe())throw new InvalidOperationException("Cannot exercise original source class catalog: "+HudBridge.ClassId);
                seededClass=HudBridge.ClassId;
            }
            else if(seededClass!=HudBridge.ClassId)throw new InvalidOperationException("Original source class changed during one adventure run");
            foreach(var id in Ids)if(!HudBridge.SpellIds.Contains(id))throw new InvalidOperationException("Original class spell not learned: "+id);
            // Reserve the effect budget during this batch-only cleanup probe.
            // Existing projectiles and their lifetimes continue ticking normally.
            if(!TomeCompatibilityDiagnostics.SetAutomaticCastingForProbe(false))throw new InvalidOperationException("Cannot reserve source visual probe budget");
            if(!SpellVfxDiagnostics.PrimeAllAssetsForProbe() || !(bool)Call("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge","PrimeAllModelsForProbe"))throw new InvalidOperationException("Cannot prime original model caches");
            if(HudBridge.ClassId=="mage")
            {
                var sheep=(GameObject)Call("AzerothMegabonk.MageVisuals.CreatureVisualBridge","Create","sheep",player.transform.position+Vector3.right*8,.8f);
                Call("AzerothMegabonk.MageVisuals.CreatureVisualBridge","Destroy",sheep);
            }
            if(!NativeWeaponDiagnostics.AttemptBlockedGrantForProbe())throw new InvalidOperationException("Native weapon grant bypassed source-class policy");
            Validate("before explicit cleanup");wait=Time.realtimeSinceStartup+1;phase=1;return false;
        }
        if(Time.realtimeSinceStartup<wait)return false;
        if(phase==1){unloading=Resources.UnloadUnusedAssets();phase=2;Plugin.Note("VISUAL forced native UnloadUnusedAssets queued: "+key);return false;}
        if(phase==2)
        {
            if(!unloading.isDone)return false;
            Validate("immediately after cleanup");
            int spawned=HudBridge.SourceVfxSpawned;
            int demonstrated=0;
            foreach(var id in Ids)
            {
                bool played=false;
                foreach(string stage in new[]{"cast","impact","missile","areaKit","area","aura"})
                    if(SpellVfxDiagnostics.PlayForProbe(id,stage,player.transform.position+Vector3.up*1.5f,.6f)){played=true;break;}
                if(played){Plugin.Note("VISUAL source class effect after cleanup: "+HudBridge.ClassId+"/"+id);if(++demonstrated>=4)break;}
            }
            if(HudBridge.SourceVfxSpawned<=spawned)throw new InvalidOperationException("No new original spell visuals after cleanup");
            wait=Time.realtimeSinceStartup+.2f;phase=3;return false;
        }
        Validate("rendered after cleanup");
        Capture("scene-visual-"+Safe(key)+".ppm");
        if(!TomeCompatibilityDiagnostics.SetAutomaticCastingForProbe(true))throw new InvalidOperationException("Cannot restore automatic class casting after visual probe");
        stages.Add(key);Plugin.Note("PASS VISUAL STAGE "+key+"; class="+HudBridge.ClassId+"; original catalogSpells="+Ids.Length+"; learned="+HudBridge.SpellIds.Length+"; body/gear/permanentPets/cache/VFX survived native cleanup");return true;
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
            // The native emergence sequence intentionally starts below ground; verify the settled body after teleporting completes.
            if(boss.IsTeleporting())return false;
            Validate("live boss after transition");
            CaptureBossEvidence(boss,"boss-visual-"+Safe(key)+".ppm","live native boss");
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
            if(scorpion!=null && scorpion.IsTeleporting())return false;
            CaptureBossEvidence(scorpion,"boss-scorpion-ground.ppm","user-reported spider boss");
            scorpion.MakeInvulnerable(false);scorpion.Kill("AzerothVisualRegressionProbe");
        }
        boss.MakeInvulnerable(false);bosses.Add(current);Plugin.Note("PASS VISUAL BOSS "+current);return true;
    }
    static void CaptureBossEvidence(Enemy enemy,string file,string label)
    {
        var camera=Camera.main;
        if(camera==null)throw new InvalidOperationException("Native camera missing for boss evidence");
        var rotation=camera.transform.rotation;
        try
        {
            var description=(string)Call("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge","Describe",enemy);
            using(var data=JsonDocument.Parse(description))
            {
                var j=data.RootElement;
                if(!j.GetProperty("canVisible").GetBoolean())
                {
                    // Native random terrain can leave the test player's view pointed over a
                    // cliff after its fixture teleport. Aim the real camera at the live body
                    // for this screenshot, with its native projection and world position intact.
                    var v=j.GetProperty("cameraViewport");
                    var target=camera.ViewportToWorldPoint(new Vector3(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle()));
                    camera.transform.LookAt(target);
                    Plugin.Note("VISUAL test camera aimed at live boss for capture; native projection, enemy position, AI and colliders unchanged");
                    description=(string)Call("AzerothMegabonk.EnemyVisuals.EnemyVisualBridge","Describe",enemy);
                }
            }
            AssertBoss(description);Plugin.Note("VISUAL "+label+" "+description);Capture(file);
        }
        finally{camera.transform.rotation=rotation;}
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
        if(!policy.RootElement.GetProperty("NativeAttacksSuppressed").GetBoolean() || policy.RootElement.GetProperty("EnginePlaceholderCount").GetInt32()!=1 || policy.RootElement.GetProperty("TicksBlocked").GetInt32()==0 || policy.RootElement.GetProperty("VisibleNativeWeaponCards").GetInt32()!=0)throw new InvalidOperationException("Native weapon is available to source class: "+weapons);
        if(!EquipmentReady())throw new InvalidOperationException("Original class equipment/body is not ready: "+appearance);
        if(HudBridge.ClassId=="mage" && (a.RootElement.GetProperty("sourceItemId").GetInt32()!=35 || a.RootElement.GetProperty("sourceDisplayId").GetInt32()!=472))throw new InvalidOperationException("Original Mage staff identity changed: "+appearance);
        if(!PetsReady(true))throw new InvalidOperationException("Original permanent class pets lost visual/runtime ownership after stage cleanup");
        using var b=JsonDocument.Parse(body);using var e=JsonDocument.Parse(enemy);using var v=JsonDocument.Parse(effects);
        if(!b.RootElement.GetProperty("allValid").GetBoolean())throw new InvalidOperationException("Original class cache lost model/texture/frames");
        if(!e.RootElement.GetProperty("allValid").GetBoolean())throw new InvalidOperationException("Enemy cache lost original model/texture/frames");
        foreach(var n in new[]{"InvalidMeshes","InvalidTextures","InvalidMaterials","FailedEffects"})if(v.RootElement.GetProperty(n).GetInt32()!=0)throw new InvalidOperationException("Source VFX lost native resources: "+n);
        if(v.RootElement.GetProperty("PinnedMeshes").GetInt32()!=v.RootElement.GetProperty("CachedMeshes").GetInt32() || v.RootElement.GetProperty("PinnedTextures").GetInt32()!=v.RootElement.GetProperty("CachedTextures").GetInt32())throw new InvalidOperationException("Source VFX caches are not protected from native cleanup");
    }
    static bool EquipmentReady()
    {
        using var document=JsonDocument.Parse((string)Call("AzerothHeroes.HeroVisualBridge","AppearanceDiagnostics"));var root=document.RootElement;
        int form=root.GetProperty("activeFormSpellId").GetInt32();
        if(root.GetProperty("activeClassId").GetString()!=HudBridge.ClassId || !root.GetProperty("attached").GetBoolean() || root.GetProperty("sourceVertexCount").GetInt32()<=0)return false;
        if(form is not (9634 or 768) && !root.GetProperty("sourceHeadClosed").GetBoolean())return false;
        var equipment=root.GetProperty("equipment");
        if(!equipment.GetProperty("sourceAssetsValid").GetBoolean() || equipment.GetProperty("visibleNativeWeaponRenderers").GetInt32()!=0)return false;
        int count=equipment.GetProperty("equipment").GetArrayLength();
        if(form is 9634 or 768)return count==0;
        return count==0 || equipment.GetProperty("attached").GetBoolean() && equipment.GetProperty("active").GetBoolean();
    }
    static Dictionary<int,int> ExpectedPermanentPets()
    {
        var expected=new Dictionary<int,int>();if(!ClassAbilityDiagnostics.UsesGenericRuntime)return expected;
        var type=TypeNamed("MageTalents.TalentBridge");
        using var catalog=JsonDocument.Parse((string)type.GetProperty("CatalogJson").GetValue(null));
        using var state=JsonDocument.Parse((string)type.GetProperty("StateJson").GetValue(null));var ranks=state.RootElement.GetProperty("Ranks");
        foreach(var node in catalog.RootElement.GetProperty("Talents").EnumerateArray())
        {
            if(!ranks.TryGetProperty(node.GetProperty("Id").GetString(),out var rank)||rank.GetInt32()<1)continue;
            foreach(var effect in node.GetProperty("Effects").EnumerateArray())
                if(effect.GetProperty("Tag").GetInt32()==29)
                {int id=effect.GetProperty("PetDisplayId").GetInt32();expected.TryGetValue(id,out var count);expected[id]=count+1;}
        }
        return expected;
    }
    static bool PetsReady(bool log)
    {
        var expected=ExpectedPermanentPets();if(expected.Count==0)return true;
        using var source=JsonDocument.Parse(ClassAbilityDiagnostics.SnapshotJson);var pets=source.RootElement.GetProperty("Pets");
        using var catalog=JsonDocument.Parse((string)Call("AzerothMegabonk.MageVisuals.CreatureVisualBridge","CatalogDiagnostics"));
        var names=new Dictionary<int,string>();
        foreach(var display in catalog.RootElement.GetProperty("sourceDisplays").EnumerateArray())names[display.GetProperty("displayId").GetInt32()]="AzerothCompanion_"+display.GetProperty("key").GetString();
        var renderers=new Dictionary<string,HashSet<int>>();
        foreach(var renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            if(renderer==null||!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
            var parent=renderer.transform;
            while(parent!=null&&!parent.name.StartsWith("AzerothCompanion_",StringComparison.Ordinal))parent=parent.parent;
            if(parent==null)continue;
            var mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if(mesh==null||mesh.vertexCount<=0||renderer.sharedMaterial==null||renderer.sharedMaterial.mainTexture==null)continue;
            if(!renderers.TryGetValue(parent.name,out var roots))renderers[parent.name]=roots=new();roots.Add(parent.gameObject.GetInstanceID());
        }
        bool ready=true;
        foreach(var row in expected)
        {
            int native=0;foreach(var pet in pets.EnumerateArray())
                if(pet.GetProperty("DisplayId").GetInt32()==row.Key&&pet.GetProperty("Remaining").GetSingle()<0&&pet.GetProperty("HasSourceVisual").GetBoolean())native++;
            int visible=names.TryGetValue(row.Key,out var name)&&renderers.TryGetValue(name,out var roots)?roots.Count:0;
            if(native<row.Value||visible<row.Value)ready=false;
            if(log)Plugin.Note("VISUAL permanent pet display="+row.Key+"; expected="+row.Value+"; runtimeAlive="+native+"; realVisibleBodies="+visible);
        }
        if(log)
        {
            Plugin.Note("VISUAL source pets="+pets.GetRawText());
            Plugin.Note("VISUAL companion animation="+(string)Call("AzerothHeroes.HeroVisualBridge","AnimationDiagnostics"));
            var talent=TypeNamed("MageTalents.TalentRuntime");Plugin.Note("VISUAL permanent-pet rebind counters="+(string)talent.GetProperty("DiagnosticsJson").GetValue(null));
        }
        return ready;
    }
    static string Safe(string value)=>value.Replace(':','-').Replace('/','-');
    static void Capture(string name)
    {
        MyPlayer.Instance.minimapCamera?.Render();
        if(!(bool)Call("AzerothMegabonk.TestBattle.CaptureBridge","Capture",name))throw new InvalidOperationException("Native camera capture failed");
    }
}
