using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
namespace MacacaGames.ViewSystem;
public partial class ViewHost
{
    private readonly HashSet<ViewInstance> requestedViews=new();
    private readonly HashSet<ViewRequestedPool> requestedPools=new();
    internal CancellationToken HostToken=>lifetime.Token;
    public ViewRequestedPool CreateRequestedPool(string elementId,ViewElementLifetime? owner=null,ViewElementChildRecoveryMode mode=ViewElementChildRecoveryMode.UseChildPolicy)
    {
        if(owner?.IsAlive==false)throw new ObjectDisposedException(nameof(owner));
        if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
        lifetime.Token.ThrowIfCancellationRequested();
        Initialize();Entry(elementId,ViewKind.Element);
        var pool=new ViewRequestedPool(this,elementId,owner,mode);requestedPools.Add(pool);return pool;
    }
    /// <summary>Borrow an existing singleton unit independently, including inline nested units.</summary>
    public ViewRequestedPool CreateNestedSingletonPool(Control singleton,ViewElementLifetime? owner=null)
    {
        if(owner?.IsAlive==false)throw new ObjectDisposedException(nameof(owner));lifetime.Token.ThrowIfCancellationRequested();
        var (scene,policy)=ElementPool.SingletonSource(singleton);
        var template=new ViewEntry{Id="@singleton:"+singleton.GetInstanceId(),Kind=ViewKind.Element,Scene=scene,PoolPolicy=policy};
        var requested=new ViewRequestedPool(this,template.Id,owner,ViewElementChildRecoveryMode.ReturnToGlobalPool,template);requestedPools.Add(requested);return requested;
    }
    internal ViewInstance PrepareRequested(string id,ViewModels? model,bool bind=true,ViewEntry? template=null)
    {
        var entry=template??Entry(id,ViewKind.Element);
        var scene=entry.Scene??(ResourceLoader.Exists(entry.ScenePath,"PackedScene")?GD.Load<PackedScene>(entry.ScenePath):null)??throw new InvalidOperationException("Requested element scene is missing.");
        var view=ViewInstance.CreatePooledElement(entry,Context(entry,model),ElementPool,scene,bind);
        requestedViews.Add(view);
        return view;
    }
    internal void RebindRequested(ViewInstance view,ViewModels? model){view.Rebind(Context(view.Definition,model));view.ConnectEvents();Prepared?.Invoke(view);}
    internal void ReleaseRequested(ViewInstance view)
    {if(view.IsDisposed)return;requestedViews.Remove(view);view.Dispose();}
    internal void PrewarmRequested(string id,int count,CancellationToken token,ViewEntry? template=null)
    {
        token.ThrowIfCancellationRequested();var entry=template??Entry(id,ViewKind.Element);
        var scene=entry.Scene??(ResourceLoader.Exists(entry.ScenePath,"PackedScene")?GD.Load<PackedScene>(entry.ScenePath):null)??throw new InvalidOperationException("Requested scene is missing.");
        ElementPool.Prewarm(scene,entry.PoolPolicy,count);
        foreach(var child in entry.Elements.Where(p=>!p.ExcludeFeatures.Any(OS.HasFeature))){var childScene=child.Scene??GD.Load<PackedScene>(child.ScenePath);ElementPool.Prewarm(childScene,child.PoolPolicy,count);}
    }
    internal void ForgetRequestedPool(ViewRequestedPool pool)=>requestedPools.Remove(pool);
}
