using MageTalents;
using AzerothAbilities;
using System.Text.Json;

try
{
if(args.Contains("--verify-handled-failure"))throw new InvalidOperationException("Controlled model-check failure");
int catalogArg=Array.IndexOf(args,"--catalogs"),outputArg=Array.IndexOf(args,"--output");
if(catalogArg<0||catalogArg+1>=args.Length)throw new ArgumentException("Use --catalogs <source-talent-catalog-directory> [--output <report-directory>].");
var catalogs=Path.GetFullPath(args[catalogArg+1]);
var root=outputArg>=0&&outputArg+1<args.Length?Path.GetFullPath(args[outputArg+1]):Path.GetFullPath("reports/source-model-checks");
Directory.CreateDirectory(root);var checks=0;var rows=new List<object>();
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;}
var all=Directory.GetFiles(catalogs,"*.json").Select(TalentCatalog.Load).ToArray();
Check(all.Length==9&&all.Sum(c=>c.Talents.Count)==354,"All original class rows");
foreach(var c in all)
{
 Check(c.Trees.Count==3&&c.Talents.All(t=>c.Trees.Any(tree=>tree.Name==t.Tree)),c.ClassId+" original tree association");
 TalentState.TemporaryPointBudget=0;var baseline=new TalentState();
 Check(baseline.Earned(c)==0&&baseline.Available(c)==0,c.ClassId+" no free normal points");
 Check(c.XpToPoint(51)==23205&&c.PointsForXp(23205)==51&&c.PointsForXp(long.MaxValue)==51,"Source normal progression cap");
 var normal=new TalentState{TalentXp=23205};var progress=true;
 while(progress){progress=false;foreach(var t in c.Talents)if(normal.Spend(c,t.Id))progress=true;}
 Check(normal.Valid(c)&&normal.Spent==51&&normal.Available(c)==0,c.ClassId+" source 51-point allocation");
 var initial=JsonSerializer.Serialize(normal);
 var branch=c.Talents.First(t=>t.Tier==1&&t.Prerequisite.Length==0);var gated=new TalentState{TalentXp=23205};
 for(var iteration=0;iteration<10&&gated.Spent<5;iteration++)foreach(var t in c.Talents.Where(t=>t.Tree==branch.Tree&&t.Tier==0))if(gated.Spent<5)gated.Spend(c,t.Id);
 Check(gated.Spent==5&&gated.Spend(c,branch.Id),c.ClassId+" exact lower-tier requirement setup");
 var firstLower=c.Talents.First(t=>t.Tree==branch.Tree&&t.Tier==0&&gated.Rank(t.Id)>0);
 Check(!gated.Clone().Refund(c,firstLower.Id),c.ClassId+" refund cannot invalidate higher tier");
 Check(JsonSerializer.Serialize(normal)==initial,"Read-only CanRefund clone does not mutate allocation");
 TalentState.TemporaryPointBudget=999;var test=new TalentState();progress=true;
 while(progress){progress=false;foreach(var t in c.Talents)if(test.Spend(c,t.Id))progress=true;}
 Check(test.Valid(c)&&test.Spent==c.Talents.Sum(t=>t.MaxRank),c.ClassId+" authorized999 full source max ranks");
 Check(test.Available(c)==999-test.Spent,"Debug budget remaining");
 foreach(var t in c.Talents)Check(test.Rank(t.Id)==t.MaxRank,"Each source talent rank");
 foreach(var item in test.Selected(c).Where(x=>x.Effect.Tag==31))
 {
  var e=item.Effect;var spells=e.Spells.Length>0?e.Spells:new[]{"test"};
  var spell=new LearnedSpell(new SpellDefinition{Id=spells[0],School=e.School is "all" or "sourceCastKind" or "sourcePetOnly"?"Holy":e.School,CastKindId=e.FilterCastKind,SourceKind=e.FilterDiscriminant==6?2:0});
  Check(e.Matches(spell),c.ClassId+" positive original spell filter");
  spell.Definition.SourceKind=2;
  if(e.FilterDiscriminant is 1 or 2 or 3 or 4)Check(!e.Matches(spell),"Player spell filters exclude source pet damage");
 }
 var parsed=JsonSerializer.Deserialize<TalentCatalog>(JsonSerializer.Serialize(c,TalentCatalog.Json),TalentCatalog.Json);
 Check(parsed.ClassId==c.ClassId&&parsed.Trees.Select(t=>t.Id).SequenceEqual(c.Trees.Select(t=>t.Id)),"Bridge class/tree JSON preserves source IDs");
 rows.Add(new{c.ClassId,Nodes=c.Talents.Count,MaxedPoints=test.Spent,NormalPoints=normal.Spent});
}
var hunter=all.Single(c=>c.ClassId=="hunter").Find("rapid_fire").Effects.Single(e=>e.Tag==1);
Check(hunter.Count==6&&hunter.MaxPerSecond==0&&hunter.TriggerEvent==7,"Rapid Fire cast counter independent of rate cap");
var hot=all.Single(c=>c.ClassId=="mage").Find("hot_streak").Effects.Single(e=>e.Tag==1);
Check(hot.Count==4&&hot.MaxPerSecond==2&&hot.TriggerEvent==2,"Hot Streak critical counter and cap");
var overload=all.Single(c=>c.ClassId=="shaman").Find("lightning_overload").Effects.Single(e=>e.Tag==0);
Check(overload.Spell==""&&overload.RepeatTriggeringSpell&&overload.TriggerEvent==7,"Optional source spell means repeat triggering spell");
var druid=all.Single(c=>c.ClassId=="druid");
Check(druid.Find("leader_of_pack").Excludes.Contains("cat_form")&&druid.Find("cat_form").Excludes.Contains("leader_of_pack"),"Original Bear/Cat mutual exclusion metadata");
Check(all.SelectMany(c=>c.Talents).SelectMany(t=>t.Effects).Where(e=>e.Tag==26&&e.Interval==0).All(e=>e.PeriodicBaseCooldown>0),"Periodic zero interval resolves exact source auxiliary cooldown");
var effects=all.SelectMany(c=>c.Talents).SelectMany(t=>t.Effects).ToArray();
Check(effects.Where(e=>e.Tag==9).Count()==7&&effects.Where(e=>e.Tag==9).All(e=>e.Ticks==Math.Max(1,(int)MathF.Round(e.Duration,MidpointRounding.AwayFromZero))&&e.Duration/e.Ticks==1),"Seven original talent DoTs use duration-derived one-second ticks");
Check(all.Single(c=>c.ClassId=="mage").Find("ignite").Effects.Single(e=>e.Tag==9).TriggerEvent==2,"Ignite applies on critical hit");
Check(all.Single(c=>c.ClassId=="rogue").Find("deadly_poison").Effects.Single(e=>e.Tag==9).TriggerEvent==0,"Deadly Poison applies on ordinary source hit");
Check(effects.Where(e=>e.Tag==6).All(e=>e.Radius==10),"Source cleave radius10 independent of spell radius");
Check(effects.Where(e=>e.Tag==8).Any(e=>e.Status==-1&&e.StatusValue>0),"Signed source vulnerability sentinel deserializes");
TalentState.TemporaryPointBudget=0;
var feral=new TalentState{TalentXp=23205};
foreach(var talent in druid.Talents.Where(t=>t.TreeId=="DruidFeralCombat"&&t.Tier<6))feral.Ranks[talent.Id]=talent.MaxRank;
Check(feral.Valid(druid)&&feral.Spent==39,"Normal source Feral lower-tree setup");
var bear=feral.Clone();Check(bear.Spend(druid,"leader_of_pack")&&!bear.Spend(druid,"cat_form"),"Native tier6 same-tree exclusion Bear then Cat");
var cat=feral.Clone();Check(cat.Spend(druid,"cat_form")&&!cat.Spend(druid,"leader_of_pack"),"Native tier6 same-tree exclusion Cat then Bear");
var both=feral.Clone();both.Ranks["leader_of_pack"]=1;both.Ranks["cat_form"]=1;
Check(!both.Valid(druid),"Normal source allocation rejects both Feral capstones");
TalentState.TemporaryPointBudget=999;Check(both.Valid(druid),"Explicit999 test exception accepts both original capstones");
TalentState.TemporaryPointBudget=0;
Check(all.All(c=>c.Talents.All(t=>t.Prerequisite.Length==0||c.Find(t.Prerequisite).MaxRank==1)),"All source prerequisites have one rank, matching native full-rank prerequisite predicate");
var result=new{Passed=true,Checks=checks,Classes=rows,NativeGameplayVerification=false};
File.WriteAllText(Path.Combine(root,"model-checks.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception ex){Console.Error.WriteLine(ex.ToString());Environment.ExitCode=1;}

namespace AzerothAbilities
{
 public sealed class SpellDefinition {public string Id{get;set;}="";public string School{get;set;}="";public int CastKindId{get;set;}public int SourceKind{get;set;}}
 public sealed class LearnedSpell {public SpellDefinition Definition{get;}public LearnedSpell(SpellDefinition definition){Definition=definition;}}
}
