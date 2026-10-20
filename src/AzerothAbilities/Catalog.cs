using System.Text.Json;

namespace AzerothAbilities;

public sealed class SpellCatalog
{
    public string SourceSha256 { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string StartingSpell { get; set; } = "";
    public float DamageScale { get; set; }
    public float CooldownScale { get; set; }
    public float CooldownFloor { get; set; }
    public string Evidence { get; set; } = "";
    public OriginalUpgradeRules UpgradeRules {get;set;}=new();
    public List<SpellDefinition> Spells { get; set; } = new();
    public List<string> Unsupported { get; set; } = new();

    public static SpellCatalog Load(string path)
    {
        var catalog = JsonSerializer.Deserialize<SpellCatalog>(File.ReadAllText(path),new JsonSerializerOptions {PropertyNameCaseInsensitive=true})
            ?? throw new InvalidDataException("Empty spell catalog");
        if(catalog.SourceSha256.Length != 64 || catalog.ClassId != "mage" || catalog.DamageScale<=0 || catalog.CooldownScale<=0 || catalog.CooldownFloor<=0)
            throw new InvalidDataException("Missing original source hash, class or verified balance scales");
        if(catalog.Spells.Select(s=>s.Id).Distinct().Count()!=catalog.Spells.Count)
            throw new InvalidDataException("Duplicate spell IDs");
        foreach(var spell in catalog.Spells)
        {
            if(spell.Id.Length==0 || spell.BaseDamage<=0 || spell.BaseCooldown<=0 || spell.SourceOffset.Length==0)
                throw new InvalidDataException($"Missing original values for {spell.Id}");
            if(spell.Kind is not ("projectile" or "instant" or "selfAoe" or "missiles"))
                throw new InvalidDataException($"Unsupported spell runtime kind: {spell.Id}/{spell.Kind}");
            if(spell.Kind is "projectile" or "missiles" && spell.ProjectileSpeed<=0)
                throw new InvalidDataException($"Missing projectile speed for {spell.Id}");
            if(spell.Kind=="selfAoe" ? spell.Radius<=0 : spell.Range<=0)
                throw new InvalidDataException($"Missing original range/radius for {spell.Id}");
            foreach(var upgrade in spell.Upgrades)
                if(upgrade.SourceEvidence.Length==0 || upgrade.Modifier is not ("damage" or "cooldown" or "targets" or "radius"))
                    throw new InvalidDataException($"Unsupported/unproven upgrade for {spell.Id}");
        }
        if(catalog.UpgradeRules.SourceEvidence.Length==0 || catalog.UpgradeRules.DamageRarity.Length!=5 || catalog.UpgradeRules.CooldownRarity.Length!=5 || catalog.UpgradeRules.TargetsRarity.Length!=5 || catalog.UpgradeRules.RadiusRarity.Length!=5)throw new InvalidDataException("Missing source-backed upgrade tables");
        if(!catalog.Spells.Any(s=>s.Id==catalog.StartingSpell))throw new InvalidDataException("Starting spell missing");
        return catalog;
    }
}

public sealed class SpellDefinition
{
    public string Id { get; set; }="";
    public string Name { get; set; }="";
    public string Icon { get; set; }="";
    public string SourceOffset { get; set; }="";
    public string Kind { get; set; }="";
    public string School { get; set; }="";
    public string Targeting { get; set; }="bossEliteNearest";
    public float CastDamageScale {get;set;}=1;
    public int CastKindId {get;set;}
    public float BaseDamage { get; set; }
    public float BaseCooldown { get; set; }
    public float Range { get; set; }
    public float Radius { get; set; }
    public int Targets { get; set; }
    public float ProjectileSpeed { get; set; }
    public int[] AllowedUpgrades {get;set;}=Array.Empty<int>();
    public List<UpgradeDefinition> Upgrades { get; set; }=new();
}
public sealed class UpgradeDefinition
{
    public string Id { get; set; }="";
    public string Modifier { get; set; }="";
    public float Value { get; set; }
    public string SourceEvidence { get; set; }="";
    public string Description { get; set; }="";
}

public sealed class LearnedSpell
{
    public SpellDefinition Definition { get; }
    public int Rank { get; set; }=1;
    public float DamageFactor { get; set; }=1;
    public float CooldownFactor { get; set; }=1;
    public float RadiusFactor { get; set; }=1;
    public int[] UpgradePicks {get;set;}=new int[5];
    public int ExtraTargets { get; set; }
    public float NextCast { get; set; }
    public LearnedSpell(SpellDefinition definition) {Definition=definition;}
}


public sealed class OriginalUpgradeRules
{
    public string SourceEvidence {get;set;}="";
    public float[] DamageRarity {get;set;}=Array.Empty<float>();
    public float[] CooldownRarity {get;set;}=Array.Empty<float>();
    public int[] TargetsRarity {get;set;}=Array.Empty<int>();
    public float[] RadiusRarity {get;set;}=Array.Empty<float>();
    public float DamageRepeatFalloff {get;set;}
    public float CooldownFactorFloor {get;set;}
    public int ExtraTargetsCap {get;set;}
    public float RadiusFactorCap {get;set;}
    public float RankDamageBonus {get;set;}
    public float RankDamageFalloff {get;set;}

    public float Delta(LearnedSpell spell,int kind,int rarity)
    {
        rarity=Math.Clamp(rarity,0,4);
        return kind switch
        {
            0=>DamageRarity[rarity]/(1+spell.UpgradePicks[0]*DamageRepeatFalloff),
            1=>CooldownRarity[rarity]*Math.Max(0,(spell.CooldownFactor-CooldownFactorFloor)/(1-CooldownFactorFloor)),
            2=>Math.Min(TargetsRarity[rarity],Math.Max(0,ExtraTargetsCap-spell.ExtraTargets)),
            3=>RadiusRarity[rarity]*Math.Max(0,(RadiusFactorCap-spell.RadiusFactor)/(RadiusFactorCap-1)),
            _=>0
        };
    }
    public void Apply(LearnedSpell spell,int kind,float value)
    {
        spell.Rank++;
        switch(kind)
        {
            case 0:spell.DamageFactor*=1+value;break;
            case 1:spell.CooldownFactor-=value;break;
            case 2:spell.ExtraTargets+=(int)value;break;
            case 3:spell.RadiusFactor+=value;break;
            default:throw new InvalidDataException("Unimplemented original upgrade kind");
        }
        spell.UpgradePicks[kind]++;
        spell.DamageFactor*=1+RankDamageBonus/(1+RankDamageFalloff*Math.Max(spell.Rank-2,0));
    }
}

