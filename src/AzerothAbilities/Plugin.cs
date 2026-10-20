using System.Reflection;
using Assets.Scripts.Actors;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Game.Combat;
using Assets.Scripts.Inventory__Items__Pickups;
using Assets.Scripts.Inventory.Stats;
using Assets.Scripts.Inventory__Items__Pickups.Weapons;
using Assets.Scripts.Managers;
using Assets.Scripts.Menu.Shop;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UObject=UnityEngine.Object;

namespace AzerothAbilities;

[BepInPlugin(Id,"Azeroth Mage Abilities","0.3.0")]
public sealed class Plugin : BasePlugin
{
    public const string Id="local.azerothmegabonk.abilities";
    internal static Plugin Current;
    internal SpellCatalog Catalog;
    internal ConfigEntry<string> SelectedClass=null!;
    internal ConfigEntry<bool> SpellVisuals=null!;
    private Harmony harmony;
    public override void Load()
    {
        Current=this;
        SelectedClass=Config.Bind("Abilities","SelectedClass","none","Explicit Azeroth class selection: mage or none. Only mage replaces native weapon casting.");
        SpellVisuals=Config.Bind("Abilities","SpellVisuals",true,"Original local Mage spell assets rendered with the Unity adapter.");
        try
        {
            var path=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,"mage.json");
            Catalog=SpellCatalog.Load(path);
            Log.LogInfo($"Loaded source-backed {Catalog.ClassId} rank-one catalog ({Catalog.Spells.Count} spells), source SHA256 {Catalog.SourceSha256}. Original upgrade modifiers and extended Mage behaviors loaded.");
            AbilityRuntime.Initialize(Catalog);
            SpellVfx.Initialize();MageSpellBehaviors.VisualEvent=SpellVfx.Play;
            harmony=new Harmony(Id);harmony.PatchAll(typeof(Plugin).Assembly);
            AddComponent<AbilityBehaviour>();
            AddComponent<MageSelectionBehaviour>();
        }
        catch(Exception ex){Log.LogError("Azeroth abilities disabled: "+ex);}
    }
    public override bool Unload(){harmony?.UnpatchSelf();AbilityRuntime.Clear();return true;}
}

public static class HudBridge
{
    public static bool Active=>AbilityRuntime.Active;
    public static string ClassId=>Active?"mage":"";
    public static string[] SpellIds=>AbilityRuntime.Spells.Select(s=>s.Definition.Id).ToArray();
    public static int[] SpellRanks=>AbilityRuntime.Spells.Select(s=>s.Rank).ToArray();
    public static float[] CooldownsRemaining=>AbilityRuntime.Spells.Select(s=>Mathf.Max(0,s.NextCast-Time.time)).ToArray();
    public static float[] CooldownsTotal=>AbilityRuntime.Spells.Select(AbilityRuntime.Cooldown).ToArray();
        public static float[] SourceDamages=>AbilityRuntime.Spells.Select(s=>s.Definition.BaseDamage*s.Definition.CastDamageScale*AbilityRuntime.Catalog.DamageScale*s.DamageFactor).ToArray();
    public static float[] SourceCooldowns=>AbilityRuntime.Spells.Select(s=>s.Definition.BaseCooldown*AbilityRuntime.Catalog.CooldownScale*s.CooldownFactor).ToArray();
    // Call from the Unity game thread. Rarity is the original Azeroth index0..4.
    public static bool ApplyUpgradeForProbe(string spellId,int kind,int rarity)
    {
        if(!Active || rarity<0 || rarity>4)return false;
        var spell=AbilityRuntime.Spells.FirstOrDefault(s=>s.Definition.Id==spellId);
        if(spell==null || !spell.Definition.AllowedUpgrades.Contains(kind))return false;
        var rules=AbilityRuntime.Catalog.UpgradeRules;
        var delta=rules.Delta(spell,kind,rarity);if(delta<=0)return false;
        AbilityRuntime.Apply(new AbilityChoice(spell.Definition,new UpgradeDefinition{Id=kind.ToString(),Value=delta,SourceEvidence=rules.SourceEvidence,Description=$"source modifier{kind} rarity{rarity}"}));
        return true;
    }
    public static int CastCount=>AbilityRuntime.CastCount;
    public static int HitCount=>AbilityRuntime.HitCount;
    public static float DamageDealt=>AbilityRuntime.DamageDealt;
    public static float BarrierRemaining=>MageSpellBehaviors.BarrierRemaining;
    public static string LastCastSpell=>AbilityRuntime.LastCastSpell;
    public static Vector3 LastCastDirection=>AbilityRuntime.LastCastDirection;
    public static int SourceVfxSpawned=>SpellVfx.Spawned;
    public static int SourceVfxLive=>SpellVfx.Live;
    public static bool LearnSpellForProbe(string id)
    {
        if(!Application.isBatchMode || !Active)return false;
        var definition=AbilityRuntime.Catalog.Spells.FirstOrDefault(s=>s.Id==id);if(definition==null)return false;
        if(!AbilityRuntime.Spells.Any(s=>s.Definition.Id==id))AbilityRuntime.Spells.Add(new LearnedSpell(definition));return true;
    }
    public static string[] AvailableChoices=>AbilityRuntime.Choices().Select(c=>c.Label).ToArray();
    public static bool SelectClass(string classId)
    {
        if(classId is not ("mage" or "none") || Plugin.Current?.Catalog==null)return false;
        Plugin.Current.SelectedClass.Value=classId;
        AbilityRuntime.ResetRun();return true;
    }
}

public static class AbilityRuntime
{
    public static Func<string,string> StarterOverride;
    public static Action<LearnedSpell,Enemy,DamageContainer> BeforeHitEvent;
    public static Action<LearnedSpell,Enemy,DamageContainer,float> HitEvent;
    public static Action<LearnedSpell,Vector3> CastEvent;
    private static Vector3? castOrigin;
    private static float castDamageScale=1,castRange;
    public static readonly List<LearnedSpell> Spells=new();
    internal static readonly List<SpellProjectile> Projectiles=new();
    internal static SpellCatalog Catalog;
    private static IntPtr lastInventory;
    private static float nextSummary;
    private static bool reported;
    public static int CastCount {get;private set;}
    public static int HitCount {get;private set;}
    public static float DamageDealt {get;private set;}
    public static string LastCastSpell {get;private set;}="";
    public static Vector3 LastCastDirection {get;private set;}=Vector3.forward;
    public static bool Active=>Catalog!=null && Plugin.Current?.SelectedClass.Value==Catalog.ClassId;
    internal static void Initialize(SpellCatalog catalog){Catalog=catalog;}
    internal static void Clear()
    {
        foreach(var p in Projectiles)p.Dispose();Projectiles.Clear();Spells.Clear();lastInventory=IntPtr.Zero;
        MageSpellBehaviors.Clear();SpellVfx.Clear();
    }
    public static void ResetRun()
    {
        Clear();CastCount=HitCount=0;DamageDealt=0;reported=false;
        var starter=StarterOverride?.Invoke(Catalog?.StartingSpell??"fireball")??Catalog?.StartingSpell;
        if(Active && Catalog!=null)Spells.Add(new LearnedSpell(Catalog.Spells.First(s=>s.Id==starter)));
        lastInventory=MyPlayer.Instance?.inventory?.Pointer??IntPtr.Zero;
        Plugin.Current?.Log.LogInfo($"Azeroth run reset: selected={Plugin.Current.SelectedClass.Value}; source-backed rank-one spells={Spells.Count}");
    }
    // A native stage change keeps the same run and its learned source upgrades.
    // Only scene-owned projectiles/statuses are discarded; ResetRun is reserved for a new run.
    public static void RebindNativeStage()
    {
        foreach(var projectile in Projectiles)projectile.Dispose();Projectiles.Clear();
        MageSpellBehaviors.Clear();SpellVfx.Clear();
        foreach(var spell in Spells)spell.NextCast=Time.time;
        lastInventory=MyPlayer.Instance?.inventory?.Pointer??IntPtr.Zero;
        Plugin.Current?.Log.LogInfo($"Azeroth stage rebound: stage={MapController.GetStageIndex()},final={MapController.IsFinalBossStage()}; retained="+string.Join(",",Spells.Select(s=>$"{s.Definition.Id}:{s.Rank}")));
    }
    public static void StartNativePlayer()
    {
        if(Active && Spells.Count>0 && (MapController.GetStageIndex()>0 || MapController.IsFinalBossStage()))RebindNativeStage();
        else ResetRun();
    }
    internal static float Stat(EStat stat)
    {
        try{return PlayerStats.GetStat(stat);}catch{return stat==EStat.Projectiles?0:1;}
    }
    public static float Cooldown(LearnedSpell s)=>Mathf.Max(Catalog?.CooldownFloor??0.25f,s.Definition.BaseCooldown*(Catalog?.CooldownScale??1)*s.CooldownFactor/Mathf.Max(0.1f,Stat(EStat.AttackSpeed)));
    public static float Damage(LearnedSpell s)=>s.Definition.BaseDamage*s.Definition.CastDamageScale*(Catalog?.DamageScale??1)*s.DamageFactor*Mathf.Max(0,Stat(EStat.DamageMultiplier));
    internal static void Tick()
    {
        var p=MyPlayer.Instance;
        if(!Active || p==null || !p.hasStarted || p.inventory==null)return;
        if(lastInventory!=p.inventory.Pointer){StartNativePlayer();lastInventory=p.inventory.Pointer;}
        var gm=GameManager.Instance;
        if(gm==null || gm.isGameOver || Time.timeScale<=0 || p.inventory.pause || LevelupScreen.isLevelingUp)return;
        MageSpellBehaviors.Tick();
        if(!reported){reported=true;Plugin.Current?.Log.LogInfo($"Azeroth Mage runtime active; starting {Spells[0].Definition.Id}, source damage={Spells[0].Definition.BaseDamage*Catalog!.DamageScale:F3}, source cooldown={Spells[0].Definition.BaseCooldown*Catalog.CooldownScale:F3}. Megabonk tome/item multipliers applied separately. Native WeaponBase.Use suppressed.");}
        for(int i=Projectiles.Count-1;i>=0;i--)if(!Projectiles[i].Tick(Time.deltaTime)){Projectiles[i].Dispose();Projectiles.RemoveAt(i);}
        foreach(var spell in Spells)
        {
            if(spell.Definition.Kind=="passive")continue;
            if(Time.time<spell.NextCast)continue;
            if(Cast(spell,p))spell.NextCast=Time.time+Cooldown(spell);
        }
        if(Time.unscaledTime>=nextSummary)
        {
            nextSummary=Time.unscaledTime+15;
            Plugin.Current?.Log.LogInfo($"Azeroth ability telemetry: spells={Spells.Count},casts={CastCount},hits={HitCount},damage={DamageDealt:F2},projectiles={Projectiles.Count}");
        }
    }
    public static List<Enemy> Targets(Vector3 origin,float range,bool priority)
    {
        var result=new List<Enemy>();var em=EnemyManager.Instance;
        if(em==null)return result;
        // Snapshot wrappers before damage may remove dictionary entries.
        foreach(var pair in em.enemies)
        {
            var e=pair.Value;
            if(e!=null && !e.IsDeadOrDyingNextFrame() && (e.transform.position-origin).sqrMagnitude<=range*range)result.Add(e);
        }
        result.Sort((a,b)=>
        {
            if(priority)
            {
                var ap=a.IsBoss()||a.IsElite();var bp=b.IsBoss()||b.IsElite();
                if(ap!=bp)return ap?-1:1;
            }
            return (a.transform.position-origin).sqrMagnitude.CompareTo((b.transform.position-origin).sqrMagnitude);
        });
        return result;
    }
    private static bool Cast(LearnedSpell spell,MyPlayer player)
    {
        if(MageSpellBehaviors.TryCast(spell,player,out var specialCasted))
        {if(specialCasted)RecordCast(spell,player);return specialCasted;}
        var d=spell.Definition;var origin=castOrigin??(player.transform.position+Vector3.up*0.8f);
        var targets=Targets(castOrigin??player.transform.position,d.Kind=="selfAoe"?d.Radius*spell.RadiusFactor*MageSpellBehaviors.RadiusScale(spell)*Mathf.Max(0.1f,Stat(EStat.SizeMultiplier)):(castRange>0?castRange:d.Range),true);
        if(targets.Count==0)return false;
        var damage=Damage(spell)*castDamageScale;
        if(d.Kind=="selfAoe")
        {
            foreach(var target in targets)Hit(spell,target,damage,origin);
            SpellVfx.Play(d.Id,"cast",player.transform.position,spell.RadiusFactor*MageSpellBehaviors.RadiusScale(spell),.7f);
        }
        else
        {
            var count=Mathf.Max(1,d.Targets+spell.ExtraTargets+Mathf.RoundToInt(Mathf.Max(0,Stat(EStat.Projectiles))));
            for(int i=0;i<count;i++)
            {
                var target=d.Targeting=="random"?targets[UnityEngine.Random.Range(0,targets.Count)]:targets[i%targets.Count];
                if(d.Kind=="instant")Hit(spell,target,damage,origin);
                else Projectiles.Add(new SpellProjectile(spell,target,origin,damage));
            }
        }
        RecordCast(spell,player,targets[0].transform.position);
        return true;
    }
    private static void RecordCast(LearnedSpell spell,MyPlayer player,Vector3? target=null)
    {
        if(castOrigin.HasValue){SpellVfx.Play(spell.Definition.Id,"cast",castOrigin.Value,1,.25f);return;}
        CastCount++;LastCastSpell=spell.Definition.Id;
        LastCastDirection=target.HasValue?(target.Value-player.transform.position).normalized:player.transform.forward;
        if(spell.Definition.Kind is "projectile" or "missiles" or "instant")
        {
            var hands=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothMegabonk.MageVisuals.MageVisualBridge",false)).FirstOrDefault(t=>t!=null)?.GetProperty("HandWorldPositions")?.GetValue(null) as Vector3[];
            if(hands!=null && hands.Length==2){SpellVfx.Play(spell.Definition.Id,"cast",hands[0],1,.25f);SpellVfx.Play(spell.Definition.Id,"cast",hands[1],1,.25f);}
            else SpellVfx.Play(spell.Definition.Id,"cast",player.transform.position+Vector3.up,1,.25f);
        }
        CastEvent?.Invoke(spell,player.transform.position);
    }
    public static bool CastFreeSpell(string id)
    {
        var player=MyPlayer.Instance;if(!Active || player?.inventory==null || !player.hasStarted)return false;
        var spell=Spells.FirstOrDefault(s=>s.Definition.Id==id);
        var definition=Catalog.Spells.FirstOrDefault(s=>s.Id==id);if(definition==null)return false;
        return Cast(spell??new LearnedSpell(definition),player);
    }
    public static bool LearnStartingSpell(string id)
    {
        if(!Active)return false;var definition=Catalog.Spells.FirstOrDefault(s=>s.Id==id);if(definition==null)return false;
        if(!Spells.Any(s=>s.Definition.Id==id))Spells.Add(new LearnedSpell(definition));return true;
    }
    public static bool LaunchPetProjectile(Enemy target,Vector3 origin,float damage)
    {
        if(!Active || target==null || target.IsDeadOrDyingNextFrame())return false;
        var definition=new SpellDefinition{Id="water_elemental",Name="Water Elemental",School="Frost",Kind="projectile",BaseDamage=50,ProjectileSpeed=28,Range=28,Targets=1,StatusKind=1,StatusValue=.3f,StatusDuration=2};
        Projectiles.Add(new SpellProjectile(new LearnedSpell(definition),target,origin,damage,28,28));return true;
    }
    public static bool CastFreeSpellAt(string id,Vector3 origin,float damageMultiplier=1,float rangeOverride=0)
    {
        var oldOrigin=castOrigin;var oldScale=castDamageScale;var oldRange=castRange;
        try{castOrigin=origin;castDamageScale=Mathf.Max(0,damageMultiplier);castRange=rangeOverride;return CastFreeSpell(id);}
        finally{castOrigin=oldOrigin;castDamageScale=oldScale;castRange=oldRange;}
    }
    public static void Hit(LearnedSpell spell,Enemy target,float damage,Vector3 from)
    {
        if(target==null || target.IsDeadOrDyingNextFrame())return;
        MageSpellBehaviors.BeforeHit(spell,target);if(target.IsDeadOrDyingNextFrame())return;
        var dc=new DamageContainer(0,"Azeroth:"+spell.Definition.Id)
        {
            damage=damage,direction=(target.transform.position-from).normalized,knockback=0,
            element=spell.Definition.School=="Fire"?EElement.Fire:spell.Definition.School=="Frost"?EElement.Ice:EElement.Neutral,
            crit=false,isExecute=false,enemy=target
        };
        var hp=target.hp;
        BeforeHitEvent?.Invoke(spell,target,dc);
        target.DamageFromPlayerOther(dc);
        MageSpellBehaviors.AfterHit(spell,target,dc);HitEvent?.Invoke(spell,target,dc,hp);
        SpellVfx.Play(spell.Definition.Id,"impact",target.transform.position+Vector3.up*.8f,1,.2f);
        HitCount++;DamageDealt+=Mathf.Max(0,hp-target.hp);
        if(HitCount<=8)Plugin.Current?.Log.LogInfo($"Azeroth hit {spell.Definition.Id}: enemy={target.id},requested={damage:F3},hp={hp:F3}->{target.hp:F3}");
    }
    internal static List<AbilityChoice> Choices(int rarity=0)
    {
        var result=new List<AbilityChoice>();
        if(!Active || Catalog==null)return result;
        foreach(var definition in Catalog.Spells)
        {
            var learned=Spells.FirstOrDefault(s=>s.Definition.Id==definition.Id);
            if(learned==null)result.Add(new AbilityChoice(definition,null));
                        else foreach(var kind in definition.AllowedUpgrades)
            {
                var delta=Catalog.UpgradeRules.Delta(learned,kind,rarity);
                if(delta<=0.00001f)continue;
                var modifier=kind switch{0=>"damage",1=>"cooldown",2=>"targets",3=>"radius",4=>"duration",_=>"unsupported"};
                var description=kind switch{0=>$"+{delta*100:0.##}% damage",1=>$"-{delta*100:0.##}% base cooldown",2=>$"+{delta:0} target(s)",3=>$"+{delta*100:0.##}% base radius",4=>$"+{delta*100:0.##}% base duration",_=>""};
                result.Add(new AbilityChoice(definition,new UpgradeDefinition{Id=kind.ToString(),Modifier=modifier,Value=delta,SourceEvidence=Catalog.UpgradeRules.SourceEvidence,Description=description}));
            }
        }
        return result;
    }
    internal static void Apply(AbilityChoice choice)
    {
        var learned=Spells.FirstOrDefault(s=>s.Definition.Id==choice.Spell.Id);
        if(learned==null)Spells.Add(new LearnedSpell(choice.Spell));
        else if(choice.Upgrade!=null)
        {
            Catalog.UpgradeRules.Apply(learned,int.Parse(choice.Upgrade.Id),choice.Upgrade.Value);
        }
        Plugin.Current?.Log.LogInfo($"Azeroth choice applied: {choice.Label}; source={choice.Upgrade?.SourceEvidence??choice.Spell.SourceOffset}");
    }
}

internal sealed class AbilityChoice
{
    internal SpellDefinition Spell; internal UpgradeDefinition Upgrade; internal AbilityChoice(SpellDefinition spell,UpgradeDefinition upgrade){Spell=spell;Upgrade=upgrade;}
    internal string Label=>Upgrade==null?"Learn "+Spell.Name:Spell.Name+": "+Upgrade.Description;
    internal string Description=>Upgrade?.Description ?? (Spell.Kind switch
    {
        "barrier"=>$"Absorbs {Spell.StatusValue*AbilityRuntime.Catalog.DamageScale*100:0.#}% max HP for {Spell.Duration*AbilityRuntime.Catalog.DurationScale:0.#} sec; refreshes when broken.",
        "passive"=>$"12% spell-hit chance to polymorph for 10 sec; up to 3 sheep. Next hit explodes for 15% max HP, capped at {Spell.BaseDamage*AbilityRuntime.Catalog.DamageScale:0.#}; radius {Spell.Radius:0.#}.",
        "groundAoe"=>$"{Spell.BaseDamage*AbilityRuntime.Catalog.DamageScale/Math.Max(1,Spell.Ticks):0.##} {Spell.School} damage per tick for {Spell.Duration*AbilityRuntime.Catalog.DurationScale:0.#} sec; radius {Spell.Radius:0.#}.",
        _=>$"Rank 1 · {Spell.BaseDamage*Spell.CastDamageScale*AbilityRuntime.Catalog.DamageScale:0.##} {Spell.School} damage · {Spell.BaseCooldown*AbilityRuntime.Catalog.CooldownScale:0.##} sec cooldown."
    });
}

internal sealed class SpellProjectile
{
    private readonly LearnedSpell spell;private readonly Enemy target;private readonly uint enemyId;
    private readonly float damage;private readonly float speed;private readonly float range;
    private Vector3 pos;private float distance;private GameObject visual;
    internal SpellProjectile(LearnedSpell spell,Enemy target,Vector3 position,float damage,float speedOverride=0,float rangeOverride=0)
    {
        this.spell=spell;this.target=target;enemyId=target.id;pos=position;this.damage=damage;
        speed=speedOverride>0?speedOverride:spell.Definition.ProjectileSpeed*Mathf.Max(0.1f,AbilityRuntime.Stat(EStat.ProjectileSpeedMultiplier));range=rangeOverride>0?rangeOverride:spell.Definition.Range;
        if(Plugin.Current!.SpellVisuals.Value && AbilityBehaviour.RenderVisuals)visual=SpellVfx.Projectile(spell.Definition.Id,pos);
    }
    internal bool Tick(float dt)
    {
        if(target==null || target.id!=enemyId || target.IsDeadOrDyingNextFrame() || distance>range)return false;
        var endpoint=target.collider!=null?target.collider.bounds.center:target.transform.position+Vector3.up;
        var step=speed*dt;
        if((endpoint-pos).sqrMagnitude<=Mathf.Max(0.2f,step)*Mathf.Max(0.2f,step))
        {AbilityRuntime.Hit(spell,target,damage,pos);return false;}
        pos=Vector3.MoveTowards(pos,endpoint,step);distance+=step;if(visual!=null){visual.transform.position=pos;var direction=endpoint-pos;if(direction.sqrMagnitude>.001f)visual.transform.rotation=Quaternion.LookRotation(direction);}return true;
    }
    internal void Dispose(){SpellVfx.Release(visual);visual=null;}
}

public sealed class AbilityBehaviour : MonoBehaviour
{
    public AbilityBehaviour(IntPtr pointer):base(pointer){}
    internal static bool RenderVisuals=>!Application.isBatchMode || Environment.GetEnvironmentVariable("AZEROTH_LAB_RENDER_PROBE")=="1";
    private static Material fire,arcane;
    private bool failed;
    private void Update()
    {
        try{AbilityRuntime.Tick();}catch(Exception ex){if(!failed){failed=true;Plugin.Current?.Log.LogError("Ability tick failed; returning to native weapons: "+ex);Plugin.Current.SelectedClass.Value="none";AbilityRuntime.Clear();}}
    }
    internal static GameObject Orb(Vector3 position,string school)
    {
        var orb=GameObject.CreatePrimitive(PrimitiveType.Sphere);orb.name="AzerothSpellVisual";orb.transform.position=position;orb.transform.localScale=Vector3.one*0.28f;
        var collider=orb.GetComponent<Collider>();if(collider!=null)UObject.Destroy(collider);
        var renderer=orb.GetComponent<Renderer>();var material=school=="Fire"?fire:arcane;
        if(material==null)
        {
            var shader=Shader.Find("Unlit/Color")??Shader.Find("Sprites/Default");
            material=new Material(shader);material.color=school=="Fire"?new Color(1,0.28f,0.04f):new Color(0.65f,0.25f,1);
            if(school=="Fire")fire=material;else arcane=material;
        }
        renderer.sharedMaterial=material;return orb;
    }
    internal static void Pulse(Vector3 position,float radius,string school)
    {
        var orb=Orb(position,school);orb.transform.localScale=new Vector3(radius*2,0.12f,radius*2);UObject.Destroy(orb,0.12f);
    }
}

[HarmonyPatch(typeof(WeaponBase),nameof(WeaponBase.Use))]
internal static class NativeWeaponsPatch
{
    private static bool Prefix()=>!AbilityRuntime.Active;
}
[HarmonyPatch(typeof(MyPlayer),nameof(MyPlayer.StartPlayer))]
internal static class StartRunPatch
{
    private static void Postfix(){AbilityRuntime.StartNativePlayer();}
}






