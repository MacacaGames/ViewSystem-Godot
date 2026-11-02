using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

public enum ViewElementChildRecoveryMode { ReturnToGlobalPool,DestroyWithOwner,UseChildPolicy }
public sealed class ViewRequestedPool:IDisposable
{
    private readonly ViewHost host;
    private readonly string id;
    private readonly ViewEntry? template;
    private readonly ViewElementChildRecoveryMode mode;
    private readonly List<ViewInstance> active=new();
    private readonly Queue<ViewInstance> local=new();
    private readonly HashSet<ViewInstance> returning=new();
    private readonly Control parking=new(){Name="RequestedPool",Visible=false,MouseFilter=Control.MouseFilterEnum.Ignore,ProcessMode=Node.ProcessModeEnum.Disabled};
    private bool disposed;
    internal ViewRequestedPool(ViewHost host,string id,ViewElementLifetime? owner,ViewElementChildRecoveryMode mode,ViewEntry? template=null)
    {this.template=template;this.host=host;this.id=id;this.mode=mode;host.AddChild(parking);owner?.OnDispose(Dispose);}
    public string TemplateId=>id;
    public ViewElementChildRecoveryMode Mode=>mode;
    public int ActiveCount=>active.Count;
    public int QueuedCount=>local.Count;
    public ViewActivation Request(Control parent,ViewModels? model=null)
    {var view=RequestView(parent,model);return new(view.Root,view.Ready);}
    private ViewInstance RequestView(Control parent,ViewModels? model)
    {
        if(disposed)throw new ObjectDisposedException(nameof(ViewRequestedPool));
        host.HostToken.ThrowIfCancellationRequested();
        if(!parent.IsInsideTree())throw new InvalidOperationException("Requested elements require a mounted parent.");
        var view=local.Count>0?local.Dequeue():host.PrepareRequested(id,model,template:template);
        try
        {
            if(mode==ViewElementChildRecoveryMode.DestroyWithOwner&&view.HasUniqueUnits)throw new InvalidOperationException("DestroyWithOwner cannot own a Unique/singleton scene, including nested singleton nodes.");
            host.RebindRequested(view,model);
            if(disposed)throw new ObjectDisposedException(nameof(ViewRequestedPool));
            host.HostToken.ThrowIfCancellationRequested();view.ResetEntry();active.Add(view);
            view.HasEntered=true;view.Lifecycle(p=>p.OnChangePage(true));view.Lifecycle(p=>p.OnBeforeShow());
            view.Root.GetParent()?.RemoveChild(view.Root);parent.AddChild(view.Root);view.MountLayout();view.Root.Show();view.Lifecycle(p=>p.OnStartShow());
            view.Ready=Enter(view);return view;
        }
        catch(Exception failure){try{ReleaseFailedRequest(view);}catch(Exception cleanup){throw new AggregateException("Requested preparation and cleanup failed.",failure,cleanup);}throw;}
    }
    private void ReleaseFailedRequest(ViewInstance view)
    {
        active.Remove(view);view.ForceDestroyRecovery=mode==ViewElementChildRecoveryMode.DestroyWithOwner;
        view.ForceKeepRecovery=mode==ViewElementChildRecoveryMode.ReturnToGlobalPool;
        _=view.Ready.ContinueWith(task=>{_ = task.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        host.ReleaseRequested(view);
    }
    private async Task Enter(ViewInstance view)
    {
        using var token=CancellationTokenSource.CreateLinkedTokenSource(view.EntryToken,host.HostToken);
        await view.AnimateAsync(true,token.Token);token.Token.ThrowIfCancellationRequested();view.Lifecycle(p=>{p.OnShown();p.OnChangedPage();});
    }
    public async Task<Control> RequestAsync(Control parent,ViewModels? model=null,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();var view=RequestView(parent,model);
        try{await view.Ready.WaitAsync(token);return view.Root;}
        catch(Exception failure)
        {
            if(active.Contains(view))try{ReleaseFailedRequest(view);}catch(Exception cleanup){throw new AggregateException("Requested entry and cleanup failed.",failure,cleanup);}throw;
        }
    }
    public ViewElementLifetime Lifetime(Control element)=>host.ElementPool.Lifetime(element);
    public async Task ReturnAsync(Control element,CancellationToken token=default)
    {
        if(disposed)throw new ObjectDisposedException(nameof(ViewRequestedPool));
        var view=active.LastOrDefault(v=>v.Root==element)??throw new InvalidOperationException("Element is not borrowed from this requested pool.");
        if(!returning.Add(view))throw new InvalidOperationException("Element is already returning.");
        using var recovery=CancellationTokenSource.CreateLinkedTokenSource(token,host.HostToken,view.LifetimeToken);
        var errors=new List<Exception>();host.ElementPool.MarkPending(view,true);
        try
        {
            try{view.CancelEntry();}catch(Exception error){errors.Add(error);}
            try{await view.Ready;}catch(OperationCanceledException){}catch(Exception error){errors.Add(error);}
            foreach(var phase in new Action<IViewElementLifeCycle>[] {p=>p.OnChangePage(false),p=>p.OnBeforeLeave(),p=>p.OnStartLeave()})
                try{view.CleanupLifecycle(phase);}catch(Exception error){errors.Add(error);}
            if(view.IsCurrentOwner)
                try{await view.AnimateAsync(false,recovery.Token);}catch(Exception error){errors.Add(error);}
        }
        finally
        {
            returning.Remove(view);active.Remove(view);
            try
            {
                if(!view.IsDisposed)
                {
                    if(view.IsCurrentOwner){view.Root.Hide();view.CleanupLifecycle(p=>{p.OnHidden();p.OnChangedPage();});}
                    if(mode==ViewElementChildRecoveryMode.DestroyWithOwner&&!disposed)
                    {try{view.BeforeElementReturn(view.Root);}catch(Exception error){errors.Add(error);}view.RestoreRecoveredElements();host.ElementPool.MarkPending(view,false);view.Dormant=true;view.Root.GetParent()?.RemoveChild(view.Root);parking.AddChild(view.Root);local.Enqueue(view);}
                    else{view.ForceKeepRecovery=mode==ViewElementChildRecoveryMode.ReturnToGlobalPool;host.ReleaseRequested(view);await view.RecoveryReady;}
                }
            }
            catch(Exception error){errors.Add(error);try{host.ReleaseRequested(view);}catch(Exception cleanup){errors.Add(cleanup);}}
        }
        if(errors.Count>0)throw new AggregateException("Requested element recovery failed.",errors);
    }
    public Task PrewarmAsync(int count,CancellationToken token=default)
    {
        if(disposed)throw new ObjectDisposedException(nameof(ViewRequestedPool));
        if(count<0)throw new ArgumentOutOfRangeException(nameof(count));
        if(mode!=ViewElementChildRecoveryMode.DestroyWithOwner){host.PrewarmRequested(id,count,token,template);return Task.CompletedTask;}
        while(local.Count<count)
        {
            token.ThrowIfCancellationRequested();var view=host.PrepareRequested(id,null,false,template);
            if(view.HasUniqueUnits){host.ReleaseRequested(view);throw new InvalidOperationException("DestroyWithOwner cannot prewarm Unique/singleton scenes.");}
            view.Dormant=true;local.Enqueue(view);
        }
        return Task.CompletedTask;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;var errors=new List<Exception>();
        void Attempt(Action action){try{action();}catch(Exception error){errors.Add(error);}}
        foreach(var view in active.Concat(local).Distinct().ToArray())
        {
            Attempt(view.CancelEntry);
            if(view.IsCurrentOwner&&!view.Dormant)
            {
                if(!returning.Contains(view)){Attempt(()=>view.CleanupLifecycle(p=>p.OnBeforeLeave()));Attempt(()=>view.CleanupLifecycle(p=>p.OnStartLeave()));}
                Attempt(()=>view.CleanupLifecycle(p=>p.OnHidden()));
            }
            view.ForceDestroyRecovery=mode==ViewElementChildRecoveryMode.DestroyWithOwner;
            view.ForceKeepRecovery=mode==ViewElementChildRecoveryMode.ReturnToGlobalPool;Attempt(()=>host.ReleaseRequested(view));
        }
        active.Clear();local.Clear();host.ForgetRequestedPool(this);parking.QueueFree();
        if(errors.Count>0)throw new AggregateException("Requested pool owner cleanup failed.",errors);
    }
}
