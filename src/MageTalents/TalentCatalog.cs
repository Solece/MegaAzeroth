using System.Text.Json;

namespace MageTalents;

public sealed class TalentCatalog
{
    public string SourceSha256 {get;set;}="";
    public int MaxPoints {get;set;}
    public int PointXpFirst {get;set;}
    public int PointXpStep {get;set;}
    public List<TalentDefinition> Talents {get;set;}=new();
    public static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true,WriteIndented=true};
    public static TalentCatalog Load(string path)
    {
        var c=JsonSerializer.Deserialize<TalentCatalog>(File.ReadAllText(path),Json)??throw new InvalidDataException("Missing Mage talent catalog");
        if(c.SourceSha256!="2c2f3926e1f8f8092809f5de0624ac8f2197bb341a2870d408ce0250f13f094a" || c.Talents.Count!=39 || c.Talents.Select(t=>t.Id).Distinct().Count()!=39 || c.MaxPoints!=51 || c.PointXpFirst!=80 || c.PointXpStep!=15)throw new InvalidDataException("Unverified Mage talent catalog");
        foreach(var t in c.Talents)
            if(t.MaxRank<1 || t.MaxRank>5 || t.Tier<0 || t.Tier>6 || t.RequiredTreePoints!=t.Tier*5 || t.Effects.Count==0 || t.SourceOffset.Length==0)throw new InvalidDataException("Invalid source talent row: "+t.Id);
        return c;
    }
    public TalentDefinition Find(string id)=>Talents.FirstOrDefault(t=>t.Id==id);
    public int XpForNext(int points)=>points>=MaxPoints?0:PointXpFirst+PointXpStep*Math.Max(0,points);
    public int PointsForXp(long xp)
    {
        var n=0;while(n<MaxPoints && xp>=XpForNext(n)){xp-=XpForNext(n);n++;}return n;
    }
    public long XpToPoint(int points)
    {
        points=Math.Clamp(points,0,MaxPoints);return (long)PointXpFirst*points+(long)PointXpStep*points*(points-1)/2;
    }
    // Native14587D0F0..D482. The laboratory uses the verified first-zone baseline;
    // there is no invented mapping from a Megabonk map to Azeroth's twelve zones.
    public static long BaselineRunXp(float seconds,int kills,int bosses,bool won)
        =>Math.Max(0,(long)MathF.Round(MathF.Max(0,seconds)/60f*12f+Math.Max(0,kills)*.1f+Math.Max(0,bosses)*40f+(won?200f:0f),MidpointRounding.AwayFromZero));
}
public sealed class TalentDefinition
{
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public string Icon {get;set;}="";
    public string Description {get;set;}="";
    public string Prerequisite {get;set;}="";
    public string Tree {get;set;}="";
    public int Tier {get;set;}
    public int Column {get;set;}
    public int MaxRank {get;set;}
    public int RequiredTreePoints {get;set;}
    public string SourceOffset {get;set;}="";
    public List<TalentEffect> Effects {get;set;}=new();
    public string DisplayDescription(int rank)
    {
        var e=Effects.FirstOrDefault();var value=e==null?0:e.Kind switch{"stat" or "spellModifier" or "critDot" or "impactSplash" or "killExplosion"=>e.Value*100,"hitStatus" or "castProc"=>e.Chance*100,_=>e.Value};
        if(e?.Kind=="spellModifier" && e.Modifier==2)value=e.Value;
        return Description.Replace("{v}",(value*Math.Max(1,rank)).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture));
    }
}
public sealed class TalentEffect
{
    public string SourceOffset {get;set;}="";
    public string Kind {get;set;}="";
    public int Tag {get;set;}
    public int Stat {get;set;}
    public float Value {get;set;}
    public float Chance {get;set;}
    public int Modifier {get;set;}
    public string[] Spells {get;set;}=Array.Empty<string>();
    public string School {get;set;}="all";
    public string Spell {get;set;}="";
    public int Count {get;set;}
    public int MaxPerSecond {get;set;}
    public float Radius {get;set;}
    public int Targets {get;set;}
    public float DamageFactor {get;set;}
    public int Status {get;set;}
    public float Duration {get;set;}
    public int Ticks {get;set;}
    public int MaxAdditional {get;set;}
    public float Interval {get;set;}
    public bool Matches(string id,string school)=>Spells.Length>0?Spells.Contains(id):School=="all"||School==school;
}
public sealed class TalentState
{
    // Explicit test-launch budget. Zero retains the recovered original progression cap.
    public static int TemporaryPointBudget {get;set;}
    public long TalentXp {get;set;}
    public Dictionary<string,int> Ranks {get;set;}=new();
    public int Spent=>Ranks.Values.Sum();
    public int Rank(string id)=>Ranks.TryGetValue(id,out var n)?n:0;
    public int TreeSpent(TalentCatalog c,string tree)=>c.Talents.Where(t=>t.Tree==tree).Sum(t=>Rank(t.Id));
    public int Earned(TalentCatalog c)=>TemporaryPointBudget>0?TemporaryPointBudget:c.PointsForXp(TalentXp);
    public int Available(TalentCatalog c)=>Math.Max(0,Earned(c)-Spent);
    public TalentState Clone()=>new(){TalentXp=TalentXp,Ranks=new(Ranks)};
    public bool Valid(TalentCatalog c)
    {
        if(TalentXp<0 || Spent>Earned(c))return false;
        foreach(var p in Ranks)
        {
            var d=c.Find(p.Key);if(d==null || p.Value<0 || p.Value>d.MaxRank)return false;
            if(p.Value==0)continue;
            var lower=c.Talents.Where(t=>t.Tree==d.Tree && t.Tier<d.Tier).Sum(t=>Rank(t.Id));
            if(lower<d.RequiredTreePoints || d.Prerequisite.Length>0 && Rank(d.Prerequisite)<1)return false;
        }
        return true;
    }
    public bool Spend(TalentCatalog c,string id)
    {
        var d=c.Find(id);if(d==null || Rank(id)>=d.MaxRank || Available(c)<1)return false;
        var candidate=Clone();candidate.Ranks[id]=candidate.Rank(id)+1;if(!candidate.Valid(c))return false;
        Ranks=candidate.Ranks;return true;
    }
    public bool Refund(TalentCatalog c,string id)
    {
        if(Rank(id)<1)return false;
        var candidate=Clone();candidate.Ranks[id]--;if(!candidate.Valid(c))return false;
        Ranks=candidate.Ranks;return true;
    }
    public IEnumerable<(TalentDefinition Talent,TalentEffect Effect,int Rank)> Selected(TalentCatalog c)
    {
        foreach(var t in c.Talents){var rank=Rank(t.Id);if(rank<1)continue;foreach(var e in t.Effects)yield return(t,e,rank);}
    }
    public float Stat(TalentCatalog c,int id)=>Selected(c).Where(t=>t.Effect.Kind=="stat"&&t.Effect.Stat==id).Sum(t=>t.Effect.Value*t.Rank);
    public float SpellModifier(TalentCatalog c,string id,string school,int kind)
        =>Selected(c).Where(t=>t.Effect.Kind=="spellModifier"&&t.Effect.Modifier==kind&&t.Effect.Matches(id,school)).Sum(t=>t.Effect.Value*t.Rank);
}
