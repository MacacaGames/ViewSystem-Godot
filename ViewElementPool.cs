using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;

public sealed record ViewPoolSnapshot(string Scene,bool Unique,int Active,int Queued,string[] Borrowers,int Pending=0);

/// <summary>One host's scene pool. Unique leases are invalidated independently of physical parents.</summary>
public sealed partial class ViewElementPool:IDisposable
{
    private sealed record Lease(ViewInstance Owner,ViewElementPlacement Placement,Node Parent);
    private sealed class Bucket
    {
        public readonly PackedScene Scene;
        public readonly Queue<Control> Queue=new();
        public readonly Dictionary<Control,List<Lease>> Active=new();
        public readonly ViewElementPolicy Policy;
        public bool Unique,Singleton;
        public StringName DefaultName=new("");
        public Bucket(PackedScene scene,ViewElementPolicy policy){Scene=scene;Policy=policy;Unique=policy.Unique;}
    }
    private readonly Dictionary<string,Bucket> buckets=new(StringComparer.Ordinal);
    private readonly Control parking=new(){Name="ElementPool",Visible=false,MouseFilter=Control.MouseFilterEnum.Ignore,ProcessMode=Node.ProcessModeEnum.Disabled};
    private readonly Dictionary<Control,ViewElementLifetime> lifetimes=new();
    private readonly HashSet<Control> leasedVisible=new();
    private readonly HashSet<ViewInstance> pendingOwners=new();
    internal void MarkPending(ViewInstance owner,bool value){if(value)pendingOwners.Add(owner);else pendingOwners.Remove(owner);}
    private bool disposed;
    private readonly HashSet<Node> singletons=new();
    private readonly List<WeakReference<ViewModels>> singletonTargets=new();
    private void TrackModels(ViewModels target){singletonTargets.RemoveAll(w=>!w.TryGetTarget(out _));if(!singletonTargets.Any(w=>w.TryGetTarget(out var value)&&ReferenceEquals(value,target)))singletonTargets.Add(new(target));}
    internal void RegisterSingleton(Node singleton,ViewModels target){singletons.Add(singleton);TrackModels(target);target.AddObject(singleton);}
    internal void InjectSingletons(ViewModels target){if(singletons.Count==0)return;TrackModels(target);foreach(var node in singletons)target.AddObject(node);}
    public event Action<Control>? Destroying;
    internal bool HasPooledAncestor(Control node)=>lifetimes.Keys.Any(parent=>GodotObject.IsInstanceValid(parent)&&parent.IsAncestorOf(node));
    public ViewElementPool(Node owner)=>owner.AddChild(parking);
    private static string Key(PackedScene scene)=>scene.HasMeta("_viewsystem_pool_key")?scene.GetMeta("_viewsystem_pool_key").AsString():scene.ResourcePath.Length>0?scene.ResourcePath:"instance:"+scene.GetInstanceId();
    private Bucket Get(PackedScene scene,ViewElementPolicy? policy)
    {
        if(disposed)throw new ObjectDisposedException(nameof(ViewElementPool));
        string key=Key(scene);policy??=new();
        if(policy.KeepCount<0)throw new InvalidOperationException("Pool KeepCount cannot be negative.");
        if(!buckets.TryGetValue(key,out var bucket)){bucket=new(scene,policy);buckets.Add(key,bucket);}
        else if(bucket.Policy.Unique!=policy.Unique&&!bucket.Singleton||bucket.Policy.Recovery!=policy.Recovery||bucket.Policy.KeepCount!=policy.KeepCount)
            throw new InvalidOperationException("A scene must use one consistent pool policy in a host: "+key);
        return bucket;
    }
    internal Control Acquire(ViewInstance owner,ViewElementPlacement placement,Node parent,PackedScene scene)
    {
        var bucket=Get(scene,placement.PoolPolicy);Control element;ViewInstance? lender=null;
        if(bucket.Unique&&bucket.Active.Count>0)
        {
            element=bucket.Active.Keys.Single();var previous=bucket.Active[element].Last();
            if(previous.Owner==owner)throw new InvalidOperationException("A Unique scene cannot be placed twice in one view.");
            if(element.IsInsideTree()&&element.IsVisibleInTree())owner.RecordTransfer(element,element.GetGlobalTransform());
            lender=previous.Owner;
        }
        else
        {
            element=bucket.Queue.Count>0?bucket.Queue.Dequeue():Instantiate(bucket);
            bucket.Active.Add(element,new());
        }
        ViewInstance.AssignElementOwner(element,owner);
        bucket.Active[element].Add(new(owner,placement,parent));
        try
        {
        // Publish the new lease before cancelling its lender. Cancellation can
        // synchronously resume a RequestAsync catch that releases the old lease.
        lender?.ParkComposedChildren(element);lender?.RestoreElement(element);lender?.SuspendElement(element);
        element.GetParent()?.RemoveChild(element);
        element.Name=placement.NodeName.Length>0?placement.NodeName:bucket.DefaultName;
        parent.AddChild(element);element.Visible=leasedVisible.Contains(element);
        foreach(var singleton in ViewComposer.Walk(element).Where(n=>n is IViewElementSingleton))owner.RegisterSingleton(singleton);
        return element;
        }
        catch(Exception failure)
        {try{Release(owner,element);}catch(Exception cleanup){throw new AggregateException("Element acquisition and recovery failed.",failure,cleanup);}throw;}
    }
    private Control Instantiate(Bucket bucket)
    {
        var node=bucket.Scene.Instantiate();
        if(node is not Control element){node.Free();throw new InvalidOperationException("Pooled scene root must be Control.");}
        PrepareNested(element,bucket.Scene);
        if(ViewComposer.Walk(element).Any(n=>n is IViewElementSingleton)){bucket.Singleton=true;bucket.Unique=true;}
        bucket.DefaultName=element.Name.ToString().Length>0?element.Name:new StringName(element.GetClass());
        if(element.Visible)leasedVisible.Add(element);
        lifetimes.Add(element,new());return element;
    }
    internal bool IsUnique(Control element)=>buckets.Values.Any(b=>b.Unique&&(b.Active.ContainsKey(element)||b.Queue.Contains(element)));
    internal void UpdateParent(ViewInstance owner,Control element,Node parent)
    {foreach(var bucket in buckets.Values)if(bucket.Active.TryGetValue(element,out var leases)){int index=leases.FindIndex(l=>l.Owner==owner);if(index>=0)leases[index]=leases[index] with {Parent=parent};return;}}
    public T GetSingleton<T>() where T:Node,IViewElementSingleton
        =>singletons.OfType<T>().SingleOrDefault()??throw new InvalidOperationException("Singleton not prepared; instantiate its view or explicitly prewarm its scene first: "+typeof(T).Name);
    public ViewElementLifetime Lifetime(Control element)=>lifetimes.TryGetValue(element,out var scope)?scope:throw new InvalidOperationException("Element is not owned by this pool.");
    internal bool IsOwner(Control element,ViewInstance owner)
    {
        foreach(var bucket in buckets.Values)
            if(bucket.Active.TryGetValue(element,out var leases))return leases.Count>0&&leases[^1].Owner==owner;
        return false;
    }
    internal void Release(ViewInstance owner,Control element,bool forceKeep=false,bool forceDestroy=false)
    {
        var bucket=buckets.Values.FirstOrDefault(b=>b.Active.ContainsKey(element));if(bucket==null)return;
        var leases=bucket.Active[element];int index=leases.FindIndex(l=>l.Owner==owner);if(index<0)return;
        bool top=index==leases.Count-1;
        bool visible=GodotObject.IsInstanceValid(element)&&element.IsInsideTree()&&owner.HasEntered&&(element.Visible||owner.IsPooledRoot);var transform=visible?element.GetGlobalTransform():Transform2D.Identity;
        leases.RemoveAt(index);
        if(!top)return;
        owner.SuspendElement(element);
        while(leases.Count>0&&(leases[^1].Owner.IsDisposed||!GodotObject.IsInstanceValid(leases[^1].Parent)))leases.RemoveAt(leases.Count-1);
        Exception? failure=null;
        if(leases.Count==0&&!owner.Dormant)try{owner.BeforeElementReturn(element);}catch(Exception error){failure=error;}
        owner.RestoreElement(element);
        ViewInstance.AssignElementOwner(element,null);
        element.GetParent()?.RemoveChild(element);
        while(leases.Count>0&&(leases[^1].Owner.IsDisposed||!GodotObject.IsInstanceValid(leases[^1].Parent)))leases.RemoveAt(leases.Count-1);
        if(leases.Count>0)
        {
            var previous=leases[^1];ViewInstance.AssignElementOwner(element,previous.Owner);
            element.Name=previous.Placement.NodeName.Length>0?previous.Placement.NodeName:bucket.DefaultName;
            if(previous.Owner.CanResumeUnder(previous.Parent)||element==previous.Owner.Root){previous.Parent.AddChild(element);element.Visible=leasedVisible.Contains(element);previous.Owner.ResumeElement(previous.Placement);}
            else {parking.AddChild(element);element.Hide();}
            if(visible){previous.Owner.RecordTransfer(element,transform);owner.QueueReturnTransfer(previous.Owner,element,previous.Placement);}return;
        }
        bucket.Active.Remove(element);
        Exception? recoveryFailure=null;
        try
        {
        if(forceDestroy&&!bucket.Unique)Destroy(element);
        else if(forceKeep||bucket.Unique||bucket.Policy.Recovery==ViewElementRecoveryPolicy.KeepForever||bucket.Policy.Recovery==ViewElementRecoveryPolicy.KeepN&&bucket.Queue.Count<bucket.Policy.KeepCount)
        {parking.AddChild(element);element.Hide();bucket.Queue.Enqueue(element);}
        else Destroy(element);
        }catch(Exception error){recoveryFailure=error;}
        if(failure!=null&&recoveryFailure!=null)throw new AggregateException("Pool return callback and recovery failed.",failure,recoveryFailure);
        if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        if(recoveryFailure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(recoveryFailure).Throw();
    }
    private void Destroy(Control element)
    {
        leasedVisible.Remove(element);
        var errors=new List<Exception>();
        try{if(lifetimes.Remove(element,out var lifetime))lifetime.Dispose();}catch(Exception error){errors.Add(error);}
        try{Destroying?.Invoke(element);}catch(Exception error){errors.Add(error);}
        finally{if(GodotObject.IsInstanceValid(element)){element.GetParent()?.RemoveChild(element);element.QueueFree();}}
        if(errors.Count>0)throw new AggregateException("Permanent element cleanup failed.",errors);
    }
    public IReadOnlyList<ViewPoolSnapshot> Snapshot()=>buckets.Select(pair=>new ViewPoolSnapshot(pair.Key,pair.Value.Unique,pair.Value.Active.Values.Count(v=>v.Count>0&&!v[^1].Owner.Dormant&&!pendingOwners.Contains(v[^1].Owner)),pair.Value.Queue.Count,
        pair.Value.Active.Values.SelectMany(v=>v.Select(l=>l.Owner.Definition.Id+"/"+l.Placement.Id)).ToArray(),pair.Value.Active.Values.Count(v=>v.Count>0&&pendingOwners.Contains(v[^1].Owner)))).ToArray();
    public void Prewarm(PackedScene scene,ViewElementPolicy? policy,int count)
    {
        if(count<0)throw new ArgumentOutOfRangeException(nameof(count));var bucket=Get(scene,policy);
        int wanted=bucket.Unique?Math.Min(1,count):count;
        if(bucket.Policy.Recovery==ViewElementRecoveryPolicy.DestroyOnRecovery&&!bucket.Unique)wanted=0;
        if(bucket.Policy.Recovery==ViewElementRecoveryPolicy.KeepN&&!bucket.Unique)wanted=Math.Min(wanted,bucket.Policy.KeepCount);
        while(bucket.Queue.Count<wanted&&!(bucket.Unique&&bucket.Active.Count>0)){var element=Instantiate(bucket);foreach(var node in ViewComposer.Walk(element).Where(n=>n is IViewElementSingleton))singletons.Add(node);element.Hide();bucket.Queue.Enqueue(element);if(bucket.Unique)break;}
        if(nestedTemplates.TryGetValue(Key(scene),out var nested))foreach(var item in nested)Prewarm(item.Scene,new(){Unique=true},Math.Min(1,count));
    }
    public void Trim()
    {
        foreach(var bucket in buckets.Values)
        {
            int keep=bucket.Unique?1:bucket.Policy.Recovery==ViewElementRecoveryPolicy.KeepForever?bucket.Queue.Count:bucket.Policy.Recovery==ViewElementRecoveryPolicy.KeepN?bucket.Policy.KeepCount:0;
            while(bucket.Queue.Count>keep)Destroy(bucket.Queue.Dequeue());
        }
    }
    internal void CancelLifetimes()
    {
        var errors=new List<Exception>();foreach(var scope in lifetimes.Values)try{scope.Dispose();}catch(Exception error){errors.Add(error);}
        if(errors.Count>0)throw new AggregateException("Pool lifetime cleanup failed.",errors);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;var errors=new List<Exception>();
        foreach(var element in lifetimes.Keys.ToArray())try{Destroy(element);}catch(Exception error){errors.Add(error);}
        foreach(var weak in singletonTargets)if(weak.TryGetTarget(out var target))foreach(var node in singletons)target.RemoveObject(node);
        singletons.Clear();singletonTargets.Clear();
        nestedTemplates.Clear();buckets.Clear();lifetimes.Clear();pendingOwners.Clear();leasedVisible.Clear();parking.QueueFree();
        if(errors.Count>0)throw new AggregateException("Pool disposal failed.",errors);
    }
}
