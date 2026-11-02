using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
namespace MacacaGames.ViewSystem;

/// <summary>Permanent scene lifetime. Pool recovery does not cancel this token.</summary>
public sealed class ViewElementLifetime:IDisposable
{
    private readonly CancellationTokenSource cancellation=new();
    private readonly List<Action> cleanup=new();
    private readonly CancellationToken token;
    public CancellationToken Token=>token;
    public bool IsAlive {get;private set;}=true;
    public ViewElementLifetime()=>token=cancellation.Token;
    public ViewRequestedPool CreatePool(ViewHost host,string elementId,ViewElementChildRecoveryMode mode=ViewElementChildRecoveryMode.UseChildPolicy)
    {if(!IsAlive)throw new ObjectDisposedException(nameof(ViewElementLifetime));return host.CreateRequestedPool(elementId,this,mode);}
    public void OnDispose(Action action)
    {if(!IsAlive)throw new ObjectDisposedException(nameof(ViewElementLifetime));cleanup.Add(action);}
    public void Subscribe<T>(Action<T> subscribe,Action<T> unsubscribe,T handler)
    {if(!IsAlive)throw new ObjectDisposedException(nameof(ViewElementLifetime));subscribe(handler);cleanup.Add(()=>unsubscribe(handler));}
    public void Dispose()
    {
        if(!IsAlive)return;IsAlive=false;var errors=new List<Exception>();
        try{cancellation.Cancel();}catch(Exception error){errors.Add(error);}
        for(int i=cleanup.Count-1;i>=0;i--)try{cleanup[i]();}catch(Exception error){errors.Add(error);}
        cleanup.Clear();cancellation.Dispose();
        if(errors.Count>0)throw new AggregateException("Element lifetime cleanup failed.",errors);
    }
}
