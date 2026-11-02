using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

public interface IViewSceneProvider
{
    Task<PackedScene> LoadAsync(string path,CancellationToken token);
}
/// <summary>Native cached/threaded resource loading. Cancellation stops waiting, not engine IO.</summary>
public sealed class GodotViewSceneProvider(Node clock):IViewSceneProvider
{
    public async Task<PackedScene> LoadAsync(string path,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!ResourceLoader.Exists(path,"PackedScene"))throw new InvalidOperationException("Scene does not exist: "+path);
        var error=ResourceLoader.LoadThreadedRequest(path,"PackedScene");
        if(error!=Error.Ok&&error!=Error.Busy)throw new InvalidOperationException("Scene load request failed: "+path+" ("+error+")");
        while(ResourceLoader.LoadThreadedGetStatus(path)==ResourceLoader.ThreadLoadStatus.InProgress)
            await clock.ToSignal(clock.GetTree(),SceneTree.SignalName.ProcessFrame).AsTask().WaitAsync(token);
        token.ThrowIfCancellationRequested();
        if(ResourceLoader.LoadThreadedGetStatus(path)!=ResourceLoader.ThreadLoadStatus.Loaded)throw new InvalidOperationException("Scene load failed: "+path);
        return ResourceLoader.LoadThreadedGet(path) as PackedScene??throw new InvalidOperationException("Resource is not a PackedScene: "+path);
    }
}
