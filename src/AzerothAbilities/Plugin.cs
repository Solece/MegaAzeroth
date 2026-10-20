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

[BepInPlugin(Id,"Azeroth Mage Abilities","0.1.1")]
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
        SpellVisuals=Config.Bind("Abilities","SpellVisuals",true,"Procedural spell visuals; these are not original Azeroth spell art.");
        try
        {
            var path=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,"mage.json");
            Catalog=SpellCatalog.Load(path);
            Log.LogInfo($"Loaded source-backed {Catalog.ClassId} rank-one catalog ({Catalog.Spells.Count} spells), source SHA256 {Catalog.SourceSha256}. Original upgrade modifier math loaded; talents/racials/original VFX unsupported.");
            AbilityRuntime.Initialize(Catalog);
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
    public static readonly List<LearnedSpell> Spells=new();
    internal static readonly List<SpellProjectile> Projectiles=new();
    internal static SpellCatalog Catalog;
    private static IntPtr lastInventory;
    private static float nextSummary;
    private static bool reported;
    public static int CastCount {get;private set;}
    public static int HitCount {get;private set;}
    public static float DamageDealt {get;private set;}
    public static bool Active=>Catalog!=null && Plugin.Current?.SelectedClass.Value==Catalog.ClassId;
    internal static void Initialize(SpellCatalog catalog){Catalog=catalog;}
    internal static void Clear()
    {
        foreach(var p in Projectiles)p.Dispose();Projectiles.Clear();Spells.Clear();lastInventory=IntPtr.Zero;
    }
    public static void ResetRun()
    {
        Clear();CastCount=HitCount=0;DamageDealt=0;reported=false;
        if(Active && Catalog!=null)Spells.Add(new LearnedSpell(Catalog.Spells.First(s=>s.Id==Catalog.StartingSpell)));
        Plugin.Current?.Log.LogInfo($"Azeroth run reset: selected={Plugin.Current.SelectedClass.Value}; source-backed rank-one spells={Spells.Count}");
    }
    internal static float Stat(EStat stat)
    {
        try{return PlayerStats.GetStat(stat);}catch{return stat==EStat.Projectiles?0:1;}
    }
    public static float Cooldown(LearnedSpell s)=>Mathf.Max(Catalog?.CooldownFloor??0.25f,s.Definition.BaseCooldown*(Catalog?.CooldownScale??1)*s.CooldownFactor/Mathf.Max(0.1f,Stat(EStat.AttackSpeed)));
    internal static float Damage(LearnedSpell s)=>s.Definition.BaseDamage*s.Definition.CastDamageScale*(Catalog?.DamageScale??1)*s.DamageFactor*Mathf.Max(0,Stat(EStat.DamageMultiplier));
    internal static void Tick()
    {
        var p=MyPlayer.Instance;
        if(!Active || p==null || !p.hasStarted || p.inventory==null)return;
        if(lastInventory!=p.inventory.Pointer){ResetRun();lastInventory=p.inventory.Pointer;}
        var gm=GameManager.Instance;
        if(gm==null || gm.isGameOver || Time.timeScale<=0 || p.inventory.pause || LevelupScreen.isLevelingUp)return;
        if(!reported){reported=true;Plugin.Current?.Log.LogInfo($"Azeroth Mage runtime active; starting {Spells[0].Definition.Id}, source damage={Spells[0].Definition.BaseDamage*Catalog!.DamageScale:F3}, source cooldown={Spells[0].Definition.BaseCooldown*Catalog.CooldownScale:F3}. Megabonk tome/item multipliers applied separately. Native WeaponBase.Use suppressed.");}
        for(int i=Projectiles.Count-1;i>=0;i--)if(!Projectiles[i].Tick(Time.deltaTime)){Projectiles[i].Dispose();Projectiles.RemoveAt(i);}
        foreach(var spell in Spells)
        {
            if(Time.time<spell.NextCast)continue;
            if(Cast(spell,p))spell.NextCast=Time.time+Cooldown(spell);
        }
        if(Time.unscaledTime>=nextSummary)
        {
            nextSummary=Time.unscaledTime+15;
            Plugin.Current?.Log.LogInfo($"Azeroth ability telemetry: spells={Spells.Count},casts={CastCount},hits={HitCount},damage={DamageDealt:F2},projectiles={Projectiles.Count}");
        }
    }
    private static List<Enemy> Targets(Vector3 origin,float range,bool priority)
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
        var d=spell.Definition;var origin=player.transform.position+Vector3.up*0.8f;
        var targets=Targets(player.transform.position,d.Kind=="selfAoe"?d.Radius*spell.RadiusFactor*Mathf.Max(0.1f,Stat(EStat.SizeMultiplier)):d.Range,true);
        if(targets.Count==0)return false;
        var damage=Damage(spell);
        if(d.Kind=="selfAoe")
        {
            foreach(var target in targets)Hit(spell,target,damage,origin);
            if(Plugin.Current!.SpellVisuals.Value && AbilityBehaviour.RenderVisuals)AbilityBehaviour.Pulse(origin,d.Radius*spell.RadiusFactor,d.School);
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
        CastCount++;
        return true;
    }
    internal static void Hit(LearnedSpell spell,Enemy target,float damage,Vector3 from)
    {
        if(target==null || target.IsDeadOrDyingNextFrame())return;
        var dc=new DamageContainer(0,"Azeroth:"+spell.Definition.Id)
        {
            damage=damage,direction=(target.transform.position-from).normalized,knockback=0,
            element=spell.Definition.School=="Fire"?EElement.Fire:EElement.Neutral,
            crit=false,isExecute=false,enemy=target
        };
        var hp=target.hp;
        target.DamageFromPlayerOther(dc);
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
                var modifier=kind switch{0=>"damage",1=>"cooldown",2=>"targets",3=>"radius",_=>"unsupported"};
                var description=kind switch{0=>$"+{delta*100:0.##}% damage",1=>$"-{delta*100:0.##}% base cooldown",2=>$"+{delta:0} target(s)",3=>$"+{delta*100:0.##}% base radius",_=>""};
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
    internal string Description=>Upgrade?.Description ?? $"Rank 1 · {Spell.BaseDamage*Spell.CastDamageScale*AbilityRuntime.Catalog!.DamageScale:0.##} {Spell.School} damage · {Spell.BaseCooldown*AbilityRuntime.Catalog.CooldownScale:0.##} sec cooldown. ";
}

internal sealed class SpellProjectile
{
    private readonly LearnedSpell spell;private readonly Enemy target;private readonly uint enemyId;
    private readonly float damage;private readonly float speed;private readonly float range;
    private Vector3 pos;private float distance;private GameObject visual;
    internal SpellProjectile(LearnedSpell spell,Enemy target,Vector3 position,float damage)
    {
        this.spell=spell;this.target=target;enemyId=target.id;pos=position;this.damage=damage;
        speed=spell.Definition.ProjectileSpeed*Mathf.Max(0.1f,AbilityRuntime.Stat(EStat.ProjectileSpeedMultiplier));range=spell.Definition.Range;
        if(Plugin.Current!.SpellVisuals.Value && AbilityBehaviour.RenderVisuals)visual=AbilityBehaviour.Orb(pos,spell.Definition.School);
    }
    internal bool Tick(float dt)
    {
        if(target==null || target.id!=enemyId || target.IsDeadOrDyingNextFrame() || distance>range)return false;
        var endpoint=target.collider!=null?target.collider.bounds.center:target.transform.position+Vector3.up;
        var step=speed*dt;
        if((endpoint-pos).sqrMagnitude<=Mathf.Max(0.2f,step)*Mathf.Max(0.2f,step))
        {AbilityRuntime.Hit(spell,target,damage,pos);return false;}
        pos=Vector3.MoveTowards(pos,endpoint,step);distance+=step;if(visual!=null)visual.transform.position=pos;return true;
    }
    internal void Dispose(){if(visual!=null)UObject.Destroy(visual);visual=null;}
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
    private static void Postfix(){AbilityRuntime.ResetRun();}
}






