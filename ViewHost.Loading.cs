using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;
public partial class ViewHost
{
    public IViewSceneProvider? SceneProvider {get;set;}
    private async Task<IReadOnlyDictionary<string,PackedScene>> PrepareScenesAsync(ViewEntry entry,CancellationToken token)
    {
        var paths=new HashSet<string>(StringComparer.Ordinal);
        foreach(var definition in StateDefinitions(entry).Where(s=>StateNeedsCreation(s.Id)).Append(entry))
        {
            if(definition.Scene==null)paths.Add(definition.ScenePath);
            foreach(var item in definition.Elements.Where(p=>p!=null&&p.Scene==null&&!p.ExcludeFeatures.Any(OS.HasFeature)))paths.Add(item.ScenePath);
        }
        var result=new Dictionary<string,PackedScene>(StringComparer.Ordinal);
        var provider=SceneProvider??new GodotViewSceneProvider(this);
        foreach(var path in paths){token.ThrowIfCancellationRequested();result.Add(path,await provider.LoadAsync(path,token));}
        return result;
    }
}
