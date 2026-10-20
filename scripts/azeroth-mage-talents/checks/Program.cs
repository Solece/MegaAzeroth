using MageTalents;
using System.Text.Json;
var catalog=TalentCatalog.Load(args[0]);var checks=0;
void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);checks++;}
Check(catalog.Talents.Count==39 && catalog.Talents.GroupBy(t=>t.Tree).All(g=>g.Count()==13),"original39 nodes,13 per tree");
Check(catalog.PointsForXp(0)==0 && catalog.PointsForXp(79)==0 && catalog.PointsForXp(80)==1,"first source point costs80XP");
Check(catalog.PointsForXp(174)==1 && catalog.PointsForXp(175)==2,"second source point costs95XP");
Check(catalog.XpToPoint(51)==23205 && catalog.PointsForXp(23204)==50 && catalog.PointsForXp(23205)==51 && catalog.PointsForXp(long.MaxValue)==51,"native51 point cap and exact23205XP threshold");
for(var n=0;n<=51;n++)Check(catalog.PointsForXp(catalog.XpToPoint(n))==n,$"native threshold roundtrip {n}");
Check(TalentCatalog.BaselineRunXp(60,100,0,false)==22,"source baseline60sec+100kills yields22XP");
Check(TalentCatalog.BaselineRunXp(600,1000,2,true)==500,"source baseline10min+1000kills+2boss+win yields500XP");
var s=new TalentState();Check(!s.Spend(catalog,"arcane_focus"),"zero earned points cannot allocate");s.TalentXp=23205;
Check(!s.Spend(catalog,"arcane_concentration"),"tier3 requires10 tree points");
for(var n=0;n<5;n++)Check(s.Spend(catalog,"arcane_focus"),"allocate sourceArcaneFocus "+(n+1));
Check(!s.Spend(catalog,"arcane_focus"),"cannot exceed source max rank5");
for(var n=0;n<5;n++)s.Spend(catalog,"arcane_subtlety");
Check(s.Spend(catalog,"arcane_concentration"),"10 point milestone opens");
Check(!s.Refund(catalog,"arcane_focus"),"cannot refund prerequisite row under a learned milestone");
Check(MathF.Abs(s.SpellModifier(catalog,"arcane_missiles","Arcane",0)-.25f)<1e-6,"ArcaneFocus5 gives source25%school damage");
Check(s.SpellModifier(catalog,"fireball","Fire",0)==0,"ArcaneFocus does not buff Fire");
Check(MathF.Abs(s.Stat(catalog,12)-.4f)<1e-6,"ArcaneSubtlety5 gives source40%luck");
Check(catalog.Find("presence_of_mind").Prerequisite=="arcane_concentration" && catalog.Find("arcane_power").Prerequisite=="presence_of_mind","sourceArcane prerequisite chain");
foreach(var tree in new[]{"Arcane","Fire","Frost"})
{
 var all=new TalentState{TalentXp=23205};foreach(var t in catalog.Talents.Where(t=>t.Tree==tree).OrderBy(t=>t.Tier))for(var n=0;n<t.MaxRank;n++)Check(all.Spend(catalog,t.Id),"source tree allocation "+t.Id+"/"+(n+1));
 Check(all.Valid(catalog),"complete "+tree+" tree valid within source51point budget");
}
Check(catalog.Find("ignite").Effects[0].Value==.8f && catalog.Find("ignite").Effects[0].Ticks==2,"sourceIgnite80%over4sec/2ticks");
Check(catalog.Find("water_elemental").Effects.Single(e=>e.Kind=="periodicCast").Interval==8,"sourceWaterElemental freeNova8sec");
TalentState.TemporaryPointBudget=999;
var debug=new TalentState();Check(debug.Earned(catalog)==999 && debug.Available(catalog)==999,"explicit debug budget999 with zero original experience");
foreach(var t in catalog.Talents.OrderBy(t=>t.Tier))for(var n=0;n<t.MaxRank;n++)if(!debug.Spend(catalog,t.Id))throw new Exception("Debug allocation failed:"+t.Id);
Check(debug.Valid(catalog) && debug.Spent==121 && debug.Available(catalog)==878,"debug999 permits all39 original nodes at maximum ranks");
Check(!debug.Spend(catalog,"arcane_focus"),"debug budget retains original node rank caps");
Check(!debug.Refund(catalog,"arcane_concentration"),"debug budget retains original prerequisite validation");
debug.Ranks.Clear();Check(debug.Available(catalog)==999,"debug free respec returns all999 points");
TalentState.TemporaryPointBudget=0;Check(debug.Earned(catalog)==0 && debug.Available(catalog)==0,"normal progression restored to original experience after debug override removed");
Console.WriteLine($"{checks} source Mage talent/progression checks passed.");
