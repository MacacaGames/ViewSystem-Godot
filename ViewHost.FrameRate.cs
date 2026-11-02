using Godot;
using System;
using System.Linq;
namespace MacacaGames.ViewSystem;
public partial class ViewHost
{
    [Export] public bool IsPrimaryHost {get;set;}
    private static WeakReference<ViewHost>? frameRateOwner;
    private int? previousFrameRate;
    private void UpdateFrameRate()
    {
        if(!IsPrimaryHost){ReleaseFrameRate();return;}
        if(frameRateOwner?.TryGetTarget(out var other)==true&&GodotObject.IsInstanceValid(other)&&other.IsInsideTree()&&other!=this)
            throw new InvalidOperationException("Only one ViewHost may control the primary page frame rate.");
        previousFrameRate??=Engine.MaxFps;frameRateOwner=new(this);
        var page=CurrentPage?.Definition;
        int requested=page?.TargetFrameRate??-1;
        if(requested<0&&page?.State.Length>0)requested=Catalog.Entries.FirstOrDefault(e=>e.Kind==ViewKind.State&&e.State==page.State&&e.TargetFrameRate>=0)?.TargetFrameRate??-1;
        Engine.MaxFps=requested>=0?requested:previousFrameRate.Value;
    }
    private void ReleaseFrameRate()
    {
        if(previousFrameRate is {} previous&&frameRateOwner?.TryGetTarget(out var owner)==true&&owner==this)
        {Engine.MaxFps=previous;frameRateOwner=null;}
        previousFrameRate=null;
    }
}
