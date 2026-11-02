using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace MacacaGames.ViewSystem;

public sealed record ViewInstanceSnapshot(string Id,string Kind,string Instance,bool Leaving,int Nodes,int Controls,bool Dormant=false);
public sealed record ViewRequestedPoolSnapshot(string Template,string Mode,int Active,int Queued);
public sealed record ViewObjectGraphSnapshot(int SchemaVersion,string Tag,ulong MonotonicMilliseconds,string Host,
    ViewInstanceSnapshot[] Views,ViewPoolSnapshot[] Pools,ViewRequestedPoolSnapshot[] RequestedPools);
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,WriteIndented=true)]
[JsonSerializable(typeof(ViewObjectGraphSnapshot))]
[JsonSerializable(typeof(ViewPolicyAdvice[]))]
public partial class ViewDiagnosticsJsonContext:JsonSerializerContext{}

public sealed record ViewPolicyAdvice(string Scene,int AuthoredUses,int SerializedNodes,int QueuedPeak,string TargetPolicy,int TargetKeepCount,
    string MigrationStatus,string[] SafetyBlockers,string[] StaticHints,string NextAction);
public static class ViewPoolPolicyAdvisor
{
    public static ViewPolicyAdvice[] Analyze(ViewCatalog catalog,IReadOnlyList<ViewObjectGraphSnapshot>? runtime=null)
    {
        var uses=catalog.Entries.SelectMany(e=>e.Elements.Select(p=>(p.Scene,p.ScenePath,p.PoolPolicy)))
            .Concat(catalog.Entries.Where(e=>e.Kind==ViewKind.Element).Select(e=>(e.Scene,e.ScenePath,e.PoolPolicy)))
            .GroupBy(x=>x.Scene!=null?x.Scene.ResourcePath.Length>0?x.Scene.ResourcePath:"instance:"+x.Scene.GetInstanceId():x.ScenePath);
        return uses.Select(group=>
        {
            var first=group.First();int nodes=0;var hints=new HashSet<string>(StringComparer.Ordinal);var visited=new HashSet<ulong>();
            void Scan(PackedScene scene)
            {
                if(!visited.Add(scene.GetInstanceId()))return;var state=scene.GetState();nodes+=state.GetNodeCount();
                for(int n=0;n<state.GetNodeCount();n++)
                {
                    var nested=state.GetNodeInstance(n);if(nested!=null)Scan(nested);
                    for(int p=0;p<state.GetNodePropertyCount(n);p++)
                        if(state.GetNodePropertyName(n,p)=="script"&&state.GetNodePropertyValue(n,p).AsGodotObject() is Script script)
                        {
                            string source=script.SourceCode;
                            if(source.Length==0)hints.Add("Script source unavailable: "+script.ResourcePath);
                            if(source.Contains("async ",StringComparison.Ordinal))hints.Add("Review async cancellation: "+script.ResourcePath);
                            if(source.Contains("+=",StringComparison.Ordinal))hints.Add("Review subscription cleanup: "+script.ResourcePath);
                            if(source.Contains("static ",StringComparison.Ordinal))hints.Add("Review static ownership: "+script.ResourcePath);
                            if(source.Contains("CreatePool",StringComparison.Ordinal)||source.Contains("CreateRequestedPool",StringComparison.Ordinal))hints.Add("Review requested child ownership: "+script.ResourcePath);
                        }
                }
            }
            if(first.Scene!=null)Scan(first.Scene);
            var samples=runtime?.Where(r=>r.SchemaVersion==1).DistinctBy(r=>(r.Host,r.MonotonicMilliseconds,r.Tag)).ToArray()??Array.Empty<ViewObjectGraphSnapshot>();
            int queued=samples.SelectMany(r=>r.Pools).Where(p=>p.Scene==group.Key).Select(p=>p.Queued).DefaultIfEmpty().Max();
            bool cycle=samples.GroupBy(r=>r.Host).Any(host=>
            {
                var sequence=new[]{"baseline","open","returned","open","returned"};int index=0;
                foreach(var sample in host.OrderBy(r=>r.MonotonicMilliseconds))
                    if(sample.Tag==sequence[index]&&(index==0||sample.Pools.Any(p=>p.Scene==group.Key))){if(++index==sequence.Length)return true;}
                return false;
            });
            var policy=first.PoolPolicy??new();string target=policy.Recovery.ToString();int keep=policy.KeepCount;
            bool intentional=policy.Recovery!=ViewElementRecoveryPolicy.KeepForever;
            if(!intentional&&!policy.Unique&&cycle&&queued>1){target=ViewElementRecoveryPolicy.KeepN.ToString();keep=1;}
            var blockers=new List<string>();
            if(!cycle)blockers.Add("Need baseline → open → returned → open → returned runtime snapshots.");
            if(hints.Count>0)blockers.Add("Static hints require lifetime/ownership review; they are not proof of a leak.");
            if(policy.Unique)blockers.Add("Unique borrowing is not eligible for ordinary hierarchy destruction.");
            if(group.Any(x=>(x.PoolPolicy?.Unique??false)!=policy.Unique||(x.PoolPolicy?.Recovery??ViewElementRecoveryPolicy.KeepForever)!=policy.Recovery||(x.PoolPolicy?.KeepCount??1)!=policy.KeepCount))blockers.Add("Conflicting policy resources for one scene.");
            return new ViewPolicyAdvice(group.Key,group.Count(),nodes,queued,target,keep,
                blockers.Count==0?"Runtime validation required":intentional?"Intentional policy; review pending":"Evidence required",blockers.ToArray(),hints.OrderBy(s=>s,StringComparer.Ordinal).ToArray(),
                blockers.Count>0?"Collect missing evidence and review lifetime cleanup; do not change policy automatically.":"Apply one reviewed policy, then repeat the same open/return cycle.");
        }).ToArray();
    }
}
public partial class ViewHost
{
    public ViewObjectGraphSnapshot CaptureObjectGraph(string tag="")
        =>new(1,tag,Time.GetTicksMsec(),IsInsideTree()?GetPath().ToString():Name.ToString(),
            owned.Values.Concat(requestedViews).Distinct().Where(v=>GodotObject.IsInstanceValid(v.Root)).Select(v=>
            {var nodes=ViewComposer.Walk(v.Root).ToArray();return new ViewInstanceSnapshot(v.Definition.Id,v.Definition.Kind.ToString(),v.Root.GetInstanceId().ToString(),leaving.Contains(v),nodes.Length,nodes.OfType<Control>().Count(),v.Dormant);}).ToArray(),
            elementPool?.Snapshot().ToArray()??Array.Empty<ViewPoolSnapshot>(),requestedPools.Select(p=>new ViewRequestedPoolSnapshot(p.TemplateId,p.Mode.ToString(),p.ActiveCount,p.QueuedCount)).ToArray());
    public string CaptureGraphJson(string tag="")=>JsonSerializer.Serialize(CaptureObjectGraph(tag),ViewDiagnosticsJsonContext.Default.ViewObjectGraphSnapshot);
    public void DumpObjectGraph(string path,string tag="")=>System.IO.File.WriteAllText(path,CaptureGraphJson(tag));
}
