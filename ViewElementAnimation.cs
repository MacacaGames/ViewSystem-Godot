using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

public enum ViewTransitionType { ActiveSwitch,Fade,Tween,Custom }
public interface IViewTransitionParticipant
{
    Task PlayViewTransitionAsync(bool show,CancellationToken token);
}
[Tool,GlobalClass]
public partial class ViewElementAnimation:Resource
{
    [Export] public ViewTransitionType Type {get;set;}=ViewTransitionType.Tween;
    [Export] public double Duration {get;set;}=.4;
    [Export] public double Delay {get;set;}
    [Export] public bool IgnoreTimeScale {get;set;}
    [Export] public Tween.TransitionType Transition {get;set;}=Tween.TransitionType.Quad;
    [Export] public Tween.EaseType Ease {get;set;}=Tween.EaseType.Out;
    [Export] public bool Move {get;set;}
    [Export] public Vector2 FromPosition {get;set;}
    [Export] public Vector2 ToPosition {get;set;}
    [Export] public bool Rotate {get;set;}
    [Export] public float FromRotation {get;set;}
    [Export] public float ToRotation {get;set;}
    [Export] public bool Scale {get;set;}
    [Export] public Vector2 FromScale {get;set;}=Vector2.One;
    [Export] public Vector2 ToScale {get;set;}=Vector2.One;
    [Export] public bool Fade {get;set;}
    [Export] public float FromAlpha {get;set;}
    [Export] public float ToAlpha {get;set;}=1;
    [Export] public bool UseCurrentFrom {get;set;}
    [Export] public bool UseCurrentTo {get;set;}
    public void Validate()
    {
        if(!double.IsFinite(Duration)||!double.IsFinite(Delay)||Duration<0||Delay<0)throw new InvalidOperationException("Animation duration/delay must be finite and nonnegative.");
        if(!float.IsFinite(FromPosition.X)||!float.IsFinite(FromPosition.Y)||!float.IsFinite(ToPosition.X)||!float.IsFinite(ToPosition.Y)||!float.IsFinite(FromRotation)||!float.IsFinite(ToRotation)||!float.IsFinite(FromScale.X)||!float.IsFinite(FromScale.Y)||!float.IsFinite(ToScale.X)||!float.IsFinite(ToScale.Y)||!float.IsFinite(FromAlpha)||!float.IsFinite(ToAlpha))throw new InvalidOperationException("Animation values must be finite.");
    }
    public async Task PlayAsync(Control node,bool show,CancellationToken token,bool? ignoreTimeScale=null)
    {
        Validate();token.ThrowIfCancellationRequested();
        if(Type==ViewTransitionType.ActiveSwitch)return;
        if(Type==ViewTransitionType.Custom)
        {
            if(node is not IViewTransitionParticipant custom)throw new InvalidOperationException("Custom transition root must implement IViewTransitionParticipant (for example an AnimationTree adapter).");
            await custom.PlayViewTransitionAsync(show,token);return;
        }
        if(node.GetParent() is Container&&(Move||Scale||Rotate))throw new InvalidOperationException("Animate an inner Control when its parent is a Container.");
        var tween=node.CreateTween().SetIgnoreTimeScale(ignoreTimeScale??IgnoreTimeScale).SetTrans(Transition).SetEase(Ease);
        if(ignoreTimeScale??IgnoreTimeScale)tween.SetPauseMode(Tween.TweenPauseMode.Process);
        bool animated=false;
        void Track(string property,Variant from,Variant to)
        {
            var current=node.GetIndexed(new NodePath(property));
            if(UseCurrentFrom)from=current;if(UseCurrentTo)to=current;
            tween.Parallel().TweenProperty(node,property,to,Duration).From(from).SetDelay(Delay);animated=true;
        }
        try
        {
            if(Type==ViewTransitionType.Tween)
            {if(Move)Track("position",FromPosition,ToPosition);if(Rotate)Track("rotation",FromRotation,ToRotation);if(Scale)Track("scale",FromScale,ToScale);}
            if(Fade||Type==ViewTransitionType.Fade)Track("modulate:a",FromAlpha,ToAlpha);
            if(!animated)tween.TweenInterval(Delay+Duration);
            await node.ToSignal(tween,Tween.SignalName.Finished).AsTask().WaitAsync(token);
        }
        finally {if(tween.IsValid())tween.Kill();}
    }
}
internal static class ViewSignalAwaiter
{
    internal static async Task AsTask(this SignalAwaiter awaiter)=>await awaiter;
}
