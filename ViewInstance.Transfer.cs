using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;
public sealed partial class ViewInstance
{
    private readonly Dictionary<Control,(Vector2 Position,Vector2 Scale)> transfers=new();
    private readonly List<Task> recoveryTransfers=new();
    internal Task RecoveryReady=>Task.WhenAll(recoveryTransfers);
    internal void RecordTransfer(Control node,Transform2D transform)=>transfers[node]=(transform.Origin,transform.Scale);
    internal async Task PlayTransferAsync(Control node,ViewElementPlacement placement,CancellationToken token)
    {
        if(!transfers.Remove(node,out var old)||placement.InstantPosition||node.GetParent() is Container||!node.IsInsideTree())return;
        if(!double.IsFinite(placement.TransferDuration)||placement.TransferDuration<0)throw new InvalidOperationException("TransferDuration must be finite and nonnegative.");
        var flags=placement.ResolveLayout(Context)?.Flags??ViewLayoutFlags.All;
        var finalPosition=node.GlobalPosition;var finalScale=node.Scale;
        var parentScale=node.GetParentControl()?.GetGlobalTransform().Scale??Vector2.One;
        bool position=(flags&ViewLayoutFlags.AnchoredPosition)!=0,scale=(flags&ViewLayoutFlags.LocalScale)!=0&&parentScale.X!=0&&parentScale.Y!=0;
        if(!position&&!scale)return;
        var tween=node.CreateTween().SetParallel().SetTrans(placement.TransferTransition).SetEase(placement.TransferEase).SetIgnoreTimeScale(IgnoreTimeScale??true);
        if(IgnoreTimeScale??true)tween.SetPauseMode(Tween.TweenPauseMode.Process);
        try
        {
            if(position){node.GlobalPosition=old.Position;tween.TweenProperty(node,"global_position",finalPosition,placement.TransferDuration);}
            if(scale){node.Scale=old.Scale/parentScale;tween.TweenProperty(node,"scale",finalScale,placement.TransferDuration);}
            await node.ToSignal(tween,Tween.SignalName.Finished).AsTask().WaitAsync(token);
        }
        finally{if(tween.IsValid())tween.Kill();}
    }
    internal void QueueReturnTransfer(ViewInstance previous,Control node,ViewElementPlacement placement)
    {
        async Task Play()
        {
            try{await previous.AnimateOwnedAsync(node,previous.LifetimeToken,t=>previous.PlayTransferAsync(node,placement,t));}
            catch(OperationCanceledException)when(previous.LifetimeToken.IsCancellationRequested){}
        }
        recoveryTransfers.Add(Play());
    }
}
