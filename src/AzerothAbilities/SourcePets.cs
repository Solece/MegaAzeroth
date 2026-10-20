using System.Reflection;
using Assets.Scripts.Menu.Shop;
using System.Text.Json;
using Assets.Scripts.Actors.Enemies;
using Assets.Scripts.Actors.Player;
using Assets.Scripts.Inventory.Stats;
using UnityEngine;
using Object=UnityEngine.Object;

namespace AzerothAbilities;

internal static class SourcePets
{
    sealed class Descriptor
    {public string PetName{get;set;}public string PetIcon{get;set;}public int PetDisplayId{get;set;}public float PetScale{get;set;}public float PetBaseDamage{get;set;}public float PetCooldown{get;set;}public int PetSpellId{get;set;}public int PetSchool{get;set;}public string PetSourceOffset{get;set;}}
    sealed class Pet
    {internal string Id;internal Descriptor Data;internal GameObject Visual;internal LearnedSpell Spell;internal Vector3 Position;internal float Until,Next,Factor,Interval,Height,NextVisualRetry;internal int Shots;}
    static readonly List<Pet> pets=new();static Dictionary<int,Descriptor> descriptors;
    static Type bridge;static MethodInfo create,position,facing,clip,destroy,height;
    static int serial;
    static void Bind()
    {
        if(descriptors==null)
        {
            string path=Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location),"catalogs","pets.json");
            descriptors=JsonSerializer.Deserialize<Dictionary<int,Descriptor>>(File.ReadAllText(path),ClassCatalogs.Options);
        }
        if(bridge!=null)return;
        bridge=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("AzerothMegabonk.MageVisuals.CreatureVisualBridge",false)).FirstOrDefault(t=>t!=null);
        if(bridge==null)return;
        create=bridge.GetMethod("CreateByDisplayId");position=bridge.GetMethod("SetPosition");facing=bridge.GetMethod("SetFacing");clip=bridge.GetMethod("SetClip");destroy=bridge.GetMethod("Destroy");height=bridge.GetMethod("SourceHeight");
    }
    internal static void Spawn(int displayId,float duration,float damageFactor,float interval,int maxAdditional,string id)
    {
        if(!ClassCombat.GenericActive || string.IsNullOrEmpty(id))return;Bind();
        if(!descriptors.TryGetValue(displayId,out var descriptor))throw new InvalidDataException("Missing source pet descriptor display "+displayId);
        if(maxAdditional==0 && pets.Any(p=>p.Id==id))return;
        if(maxAdditional>0 && pets.Count(p=>p.Id==id||p.Id.StartsWith(id+"#",StringComparison.Ordinal))>=maxAdditional)return;
        var player=MyPlayer.Instance;if(player?.inventory==null||!player.hasStarted)return;
        string key=maxAdditional>0?id+"#"+(++serial):id;
        var original=ClassCatalogs.AllSpells().FirstOrDefault(s=>s.SourceSpellId==descriptor.PetSpellId);
        string school=new[]{"Physical","Holy","Fire","Nature","Frost","Shadow","Arcane"}[Math.Clamp(descriptor.PetSchool,0,6)];
        var definition=new SpellDefinition{Id="pet_"+descriptor.PetName.ToLowerInvariant().Replace(' ','_'),Name=descriptor.PetName,Icon=descriptor.PetIcon??original?.Icon??AbilityRuntime.Catalog.SourceClass.Icon,SourceKind=2,SourceSpellId=descriptor.PetSpellId,SourceOffset=descriptor.PetSourceOffset,Kind=descriptor.PetSpellId==0?"melee":"projectile",School=school,CastKindId=descriptor.PetSpellId==0?5:0,BaseDamage=descriptor.PetBaseDamage,BaseCooldown=descriptor.PetCooldown,CastDamageScale=1,Range=descriptor.PetSpellId==0?3.5f:original?.Range??28,Targets=1,ProjectileSpeed=original?.ProjectileSpeed??0};
        if(displayId==110){definition.Range=28;definition.ProjectileSpeed=28;definition.StatusKind=1;definition.StatusValue=.3f;definition.StatusDuration=2;}
        var at=player.feet!=null?player.feet.position:player.transform.position;
        var pet=new Pet{Id=key,Data=descriptor,Spell=new LearnedSpell(definition),Position=at,Until=duration>0?CombatSimulation.Time+duration:float.PositiveInfinity,Factor=Mathf.Max(0,damageFactor),Interval=interval>0?interval:descriptor.PetCooldown,Next=CombatSimulation.Time};
        EnsureVisual(pet,player);
        pets.Add(pet);DamageSourceRegistry.Register("Azeroth:"+definition.Id,pet.Spell);Plugin.Current?.Log.LogInfo($"Source companion: display={displayId};id={key};baseDamage={descriptor.PetBaseDamage};school={school};source={descriptor.PetSourceOffset};visible={pet.Visual!=null}");
    }
    static float HostFit(MyPlayer player)
    {
        string path=Path.Combine(BepInEx.Paths.GameRootPath,"AzerothHeroAssets",ClassCombat.ClassId,"body","model.json");
        if(!File.Exists(path))path=Path.Combine(BepInEx.Paths.GameRootPath,"AzerothMageAssets","human-male","model.json");
        using var doc=JsonDocument.Parse(File.ReadAllText(path));
        float source=doc.RootElement.GetProperty("boundsMax")[1].GetSingle()-doc.RootElement.GetProperty("boundsMin")[1].GetSingle();
        var capsule=player.playerMovement?.GetComponent<CapsuleCollider>();
        float host=capsule==null?player.height:(capsule.direction==1?capsule.height:capsule.radius*2)*Mathf.Abs(capsule.transform.lossyScale.y);
        return host/Mathf.Max(.01f,source);
    }
    static void EnsureVisual(Pet pet,MyPlayer player)
    {
        if(pet.Visual!=null || !AbilityBehaviour.RenderVisuals || Time.unscaledTime<pet.NextVisualRetry)return;
        Bind();if(create==null)return;
        try
        {
            pet.Height=(height!=null?(float)height.Invoke(null,new object[]{pet.Data.PetDisplayId}):1.6f)*pet.Data.PetScale*HostFit(player);
            pet.Visual=(GameObject)create.Invoke(null,new object[]{pet.Data.PetDisplayId,pet.Position,pet.Height});
        }
        catch(Exception error){pet.NextVisualRetry=Time.unscaledTime+2;Plugin.Current?.Log.LogWarning("Source companion visual will retry: "+pet.Id+"; "+error.GetBaseException().Message);}
    }
    static Vector3 Floor(Vector3 point)
    {
        var game=GameManager.Instance;
        if(game!=null && Physics.Raycast(point+Vector3.up*12,Vector3.down,out var hit,40,game.whatIsGround.value,QueryTriggerInteraction.Ignore))point.y=hit.point.y;
        return point;
    }
    internal static void Despawn(string id)
    {
        foreach(var pet in pets.Where(p=>p.Id==id||p.Id.StartsWith(id+"#",StringComparison.Ordinal)).ToArray()){Remove(pet);pets.Remove(pet);}
    }
    internal static bool Has(string id)=>pets.Any(p=>p.Id==id||p.Id.StartsWith(id+"#",StringComparison.Ordinal));
    internal static int Count(string id)=>pets.Count(p=>p.Id==id||p.Id.StartsWith(id+"#",StringComparison.Ordinal));
    static void Remove(Pet pet){if(pet.Visual!=null){if(destroy!=null)destroy.Invoke(null,new object[]{pet.Visual});else Object.Destroy(pet.Visual);}pet.Visual=null;}
    internal static void Clear(){foreach(var pet in pets)Remove(pet);pets.Clear();}
    internal static void Tick()
    {
        var player=MyPlayer.Instance;if(player?.inventory==null||!CombatSimulation.CanSimulate)return;
        float dt=CombatSimulation.DeltaTime;var feet=player.feet!=null?player.feet.position:player.transform.position;
        foreach(var pet in pets.ToArray())
        {
            if(CombatSimulation.Time>=pet.Until){Remove(pet);pets.Remove(pet);continue;}
            EnsureVisual(pet,player);
            bool melee=pet.Data.PetSpellId==0;
            var enemies=AbilityRuntime.Targets(pet.Position,melee?28:pet.Spell.Definition.Range,false);var target=enemies.FirstOrDefault();
            var desired=melee&&target!=null?target.transform.position:feet-player.transform.forward*2.8f;
            if((pet.Position-feet).sqrMagnitude>40*40)pet.Position=feet;
            var previous=pet.Position;
            float stop=melee&&target!=null?pet.Spell.Definition.Range*.75f:3.5f;
            if((pet.Position-desired).sqrMagnitude>stop*stop)
                pet.Position=Vector3.MoveTowards(pet.Position,desired,1.15f*Mathf.Max(7,7*Mathf.Max(0,AbilityRuntime.Stat(EStat.MoveSpeedMultiplier)))*dt);
            pet.Position=Floor(pet.Position);
            if(pet.Visual!=null)
            {
                position?.Invoke(null,new object[]{pet.Visual,pet.Position});
                if((pet.Position-previous).sqrMagnitude>.0001f){facing?.Invoke(null,new object[]{pet.Visual,pet.Position-previous});clip?.Invoke(null,new object[]{pet.Visual,"run"});}
                else clip?.Invoke(null,new object[]{pet.Visual,"idle"});
            }
            if(target==null || CombatSimulation.Time<pet.Next || melee&&(target.transform.position-pet.Position).sqrMagnitude>pet.Spell.Definition.Range*pet.Spell.Definition.Range)continue;
            int level=Math.Max(0,player.inventory.playerXp.level);
            float damage=pet.Data.PetBaseDamage*(1+.12f*level)*pet.Factor*Mathf.Max(0,AbilityRuntime.Stat(EStat.DamageMultiplier));
            // ClassCombat.Hit adds sourceStat17 once for this SourceKind2.
            if(melee || pet.Spell.Definition.ProjectileSpeed<=0)AbilityRuntime.Hit(pet.Spell,target,damage,pet.Position);
            else AbilityRuntime.AddProjectile(pet.Spell,target,pet.Position+Vector3.up*pet.Height*.55f,damage,0);
            pet.Next=CombatSimulation.Time+pet.Interval/Mathf.Max(.1f,AbilityRuntime.Stat(EStat.AttackSpeed));pet.Shots++;
            if(pet.Visual!=null){facing?.Invoke(null,new object[]{pet.Visual,target.transform.position-pet.Position});clip?.Invoke(null,new object[]{pet.Visual,melee?"attack":"cast"});}
        }
    }
    internal static object Snapshot()=>pets.Select(p=>new{p.Id,DisplayId=p.Data.PetDisplayId,p.Data.PetName,Position=new[]{p.Position.x,p.Position.y,p.Position.z},Remaining=p.Until==float.PositiveInfinity?-1:p.Until-CombatSimulation.Time,NextAttackIn=Mathf.Max(0,p.Next-CombatSimulation.Time),p.Shots,HasSourceVisual=p.Visual!=null,Range=p.Spell.Definition.Range,p.Height,SourceScale=p.Data.PetScale,SourceSpellId=p.Data.PetSpellId}).ToArray();
}
