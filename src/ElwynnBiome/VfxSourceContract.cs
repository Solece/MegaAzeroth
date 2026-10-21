using System.Text.Json;

namespace AzerothMegabonk.ElwynnBiome;

// Test-only expectations must be supplied from an independently audited source
// route. Deriving required stages from the running adapter would reproduce its bugs.
internal sealed class VfxSourceContract
{
    // Independent audited source case: emitter flags32777, step rate200@67ms,
    // native latch436, lifetime1.2 and shape2 vertical0. Not a generic VFX rule.
    internal static bool ConsecrationOnceBurst(int earlyPeak,long totalSpawned,int bursts,long clipped,int latePeak)=>earlyPeak==200&&totalSpawned==200&&bursts==1&&clipped==0&&latePeak==0;
    internal static bool ConsecrationHorizontal(float maximumCenterYDrift)=>float.IsFinite(maximumCenterYDrift)&&maximumCenterYDrift<.2f;
    internal sealed class Specification
    {
        public string Spell {get;set;}
        public string Scope {get;set;}
        public string SourceEvidence {get;set;}
        public string[] UnverifiedRequirements {get;set;}=Array.Empty<string>();
        public Requirement[] Requirements {get;set;}=Array.Empty<Requirement>();
        public float[] CaptureTimes {get;set;}=Array.Empty<float>();
    }
    internal sealed class Requirement
    {
        public string Label {get;set;}
        public string Stage {get;set;}
        public string Key {get;set;}
        public string Source {get;set;}
        public float FromSeconds {get;set;}
        public float ToSeconds {get;set;}
        public bool RequireLiveParticles {get;set;}
        public bool RequireVisibleGeometry {get;set;}
        public bool RequireStagePixels {get;set;}=true;
        public bool AllowFallback {get;set;}
        public float ExpectedOriginalLifeSeconds {get;set;}
        public bool RequireOriginalOneShotDuration {get;set;}
    }
    internal sealed class Observation
    {
        internal bool ModelSeen,ParticlesSeen,GeometrySeen,SimultaneousDrawable;
        internal bool CorrectOriginalLife,StoppedTooEarly;
        internal float FirstSeen=-1,LastSeen=-1;
        internal int MaximumParticles,MaximumVertices;
        internal readonly List<float> PixelTimes=new();
    }
    internal sealed class Check
    {
        public bool Pass {get;set;}
        public string Label {get;set;}
        public object Evidence {get;set;}
    }
    internal sealed class Report
    {
        public bool Pass {get;set;}
        public string Spell {get;set;}
        public string Scope {get;set;}
        public string SourceEvidence {get;set;}
        public bool AppearanceVerified {get;set;}=false;
        public bool PixelsEvaluated {get;set;}
        public List<Check> Checks {get;set;}=new();
    }
    readonly Specification spec;readonly Observation[] observations;readonly JsonElement baseline;
    internal IEnumerable<string> Stages=>spec.Requirements.Select(r=>r.Stage).Distinct();
    internal float[] CaptureTimes=>spec.CaptureTimes;
    internal static VfxSourceContract Load(string path,string spell,string baselineJson)
    {
        var specification=JsonSerializer.Deserialize<Specification>(File.ReadAllText(path),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidDataException("Missing VFX source contract");
        if(specification.Spell!=spell)throw new InvalidDataException("Source contract belongs to another spell: "+specification.Spell);
        return new VfxSourceContract(specification,Parse(baselineJson));
    }
    internal VfxSourceContract(Specification specification,JsonElement before)
    {
        spec=specification;baseline=before.Clone();
        if(string.IsNullOrWhiteSpace(spec.SourceEvidence)||spec.Requirements.Length==0)throw new InvalidDataException("Contract requires independent source evidence and critical stages/models");
        foreach(var r in spec.Requirements)if(string.IsNullOrWhiteSpace(r.Stage)||string.IsNullOrWhiteSpace(r.Key)||string.IsNullOrWhiteSpace(r.Source)||r.FromSeconds<0||r.ToSeconds<=r.FromSeconds||(!r.RequireLiveParticles&&!r.RequireVisibleGeometry))throw new InvalidDataException("Invalid source VFX requirement: "+r.Label);
        if(spec.CaptureTimes.Any(t=>!float.IsFinite(t)||t<=0)||!spec.CaptureTimes.SequenceEqual(spec.CaptureTimes.OrderBy(t=>t).Distinct()))throw new InvalidDataException("Source capture times must be finite, positive and strictly increasing");
        observations=spec.Requirements.Select(_=>new Observation()).ToArray();
    }
    internal void Observe(JsonElement snapshot,float seconds)
    {
        if(!Try(snapshot,"Effects",out var effects)||effects.ValueKind!=JsonValueKind.Array)return;
        for(int i=0;i<spec.Requirements.Length;i++)
        {
            var r=spec.Requirements[i];if(seconds<r.FromSeconds||seconds>r.ToSeconds)continue;var o=observations[i];
            foreach(var fx in effects.EnumerateArray())
            {
                if(Text(fx,"Id")!=spec.Spell||Text(fx,"Stage")!=r.Stage||!string.Equals(Text(fx,"Key"),r.Key,StringComparison.OrdinalIgnoreCase)||!string.Equals(Normalize(Text(fx,"Source")),Normalize(r.Source),StringComparison.OrdinalIgnoreCase))continue;
                o.ModelSeen=true;if(o.FirstSeen<0)o.FirstSeen=seconds;o.LastSeen=seconds;
                if(r.ExpectedOriginalLifeSeconds>0)
                {
                    o.CorrectOriginalLife|=Math.Abs(Number(fx,"LifeSeconds")-r.ExpectedOriginalLifeSeconds)<.002f&&Math.Abs(Number(fx,"SourceDurationSeconds")-r.ExpectedOriginalLifeSeconds)<.002f&&(!r.RequireOriginalOneShotDuration||Boolean(fx,"OriginalOneShotDuration"));
                    o.StoppedTooEarly|=Boolean(fx,"Stopped")&&Number(fx,"Age")<r.ExpectedOriginalLifeSeconds-.02f;
                }
                int particles=Items(fx,"ParticleLayers").Sum(p=>Integer(p,"LiveParticles"));
                int vertices=Items(fx,"Renderers").Where(p=>Boolean(p,"Enabled")&&Boolean(p,"Active")&&Number(p,"Alpha")>0&&Integer(p,"IndexCount")>0&&!string.IsNullOrWhiteSpace(Text(p,"Shader"))).Sum(p=>Integer(p,"VertexCount"));
                o.MaximumParticles=Math.Max(o.MaximumParticles,particles);o.MaximumVertices=Math.Max(o.MaximumVertices,vertices);
                o.ParticlesSeen|=particles>0;o.GeometrySeen|=vertices>0;
                o.SimultaneousDrawable|=vertices>0&&(!r.RequireLiveParticles||particles>0)&&(!r.RequireVisibleGeometry||vertices>0);
            }
        }
    }
    internal void ObservePixels(string stage,float seconds,long changedPixels)
    {
        if(changedPixels<=0)return;
        for(int i=0;i<spec.Requirements.Length;i++){var r=spec.Requirements[i];if(r.Stage==stage&&seconds>=r.FromSeconds&&seconds<=r.ToSeconds)observations[i].PixelTimes.Add(seconds);}
    }
    internal Report Evaluate(JsonElement current,bool requirePixels=true)
    {
        var report=new Report{Spell=spec.Spell,Scope=spec.Scope,SourceEvidence=spec.SourceEvidence,PixelsEvaluated=requirePixels};
        void Add(bool pass,string label,object evidence)=>report.Checks.Add(new Check{Pass=pass,Label=label,Evidence=evidence});
        Add(spec.UnverifiedRequirements.Length==0,"independent source contract complete",spec.UnverifiedRequirements);
        for(int i=0;i<spec.Requirements.Length;i++)
        {
            var r=spec.Requirements[i];var o=observations[i];
            Add(o.ModelSeen,r.Label+": exact source model dispatched in required window",new{r.Stage,r.Key,r.Source,r.FromSeconds,r.ToSeconds,o.FirstSeen,o.LastSeen});
            Add(o.SimultaneousDrawable,r.Label+": required particles/geometry alive together",new{r.RequireLiveParticles,r.RequireVisibleGeometry,o.MaximumParticles,o.MaximumVertices});
            if(r.ExpectedOriginalLifeSeconds>0)Add(o.CorrectOriginalLife&&!o.StoppedTooEarly,r.Label+": original one-shot emission lifetime, no premature stop",new{r.ExpectedOriginalLifeSeconds,r.RequireOriginalOneShotDuration,o.CorrectOriginalLife,o.StoppedTooEarly});
            if(requirePixels)Add(!r.RequireStagePixels||o.PixelTimes.Count>0,r.Label+": independently isolated stage contributes pixels",new{o.PixelTimes});
            string stage=spec.Spell+"/"+r.Stage;
            var drops=DeltaEntries(current,baseline,"Dropped",key=>key.EndsWith(":"+stage,StringComparison.Ordinal)||key==stage);
            var fallbacks=DeltaEntries(current,baseline,"Fallbacks",key=>key==stage);
            Add(drops.Count==0,r.Label+": no critical-stage budget/asset drops",drops);
            Add(r.AllowFallback||fallbacks.Count==0,r.Label+": exact stage, no fallback substitution",fallbacks);
            Add(Counter(current,"Requested",stage)>Counter(baseline,"Requested",stage)&&Counter(current,"SpawnedStages",stage)>Counter(baseline,"SpawnedStages",stage),r.Label+": actual requested and admitted stage counters advanced",new{requested=Counter(current,"Requested",stage)-Counter(baseline,"Requested",stage),spawned=Counter(current,"SpawnedStages",stage)-Counter(baseline,"SpawnedStages",stage)});
        }
        report.Pass=report.Checks.All(c=>c.Pass);return report;
    }
    static Dictionary<string,int> DeltaEntries(JsonElement after,JsonElement before,string group,Func<string,bool> matches)
    {var result=new Dictionary<string,int>();if(Try(after,group,out var entries)&&entries.ValueKind==JsonValueKind.Object)foreach(var p in entries.EnumerateObject()){int delta=p.Value.GetInt32()-Counter(before,group,p.Name);if(delta>0&&matches(p.Name))result[p.Name]=delta;}return result;}
    static int Counter(JsonElement value,string group,string key)=>Try(value,group,out var g)&&Try(g,key,out var e)&&e.TryGetInt32(out int result)?result:0;
    internal static JsonElement Parse(string value){using var d=JsonDocument.Parse(value);return d.RootElement.Clone();}
    static IEnumerable<JsonElement> Items(JsonElement value,string name)=>Try(value,name,out var e)&&e.ValueKind==JsonValueKind.Array?e.EnumerateArray():Enumerable.Empty<JsonElement>();
    static bool Try(JsonElement value,string name,out JsonElement found){if(value.ValueKind==JsonValueKind.Object)foreach(var p in value.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)){found=p.Value;return true;}found=default;return false;}
    static string Text(JsonElement v,string key)=>Try(v,key,out var e)&&e.ValueKind==JsonValueKind.String?e.GetString():null;
    static int Integer(JsonElement v,string key)=>Try(v,key,out var e)&&e.ValueKind==JsonValueKind.Number&&e.TryGetInt32(out int n)?n:0;
    static float Number(JsonElement v,string key)=>Try(v,key,out var e)&&e.ValueKind==JsonValueKind.Number&&e.TryGetSingle(out float n)?n:0;
    static bool Boolean(JsonElement v,string key)=>Try(v,key,out var e)&&e.ValueKind==JsonValueKind.True;
    static string Normalize(string value)=>value?.Replace('\\','/');
}
