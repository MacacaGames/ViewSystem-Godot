using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

public interface IViewPresenter<in TModel>{void Bind(Control view,TModel model);}
public interface IViewTransitions
{
    double EnterDuration(Control view);
    Task RetirePageAsync(Control view,CancellationToken token);
    Task RetireOverlayAsync(Control view,CancellationToken token);
}
public sealed record ViewActivation(Control View,Task Ready);

/// <summary>Catalog, page/state/overlay lifetime and transition ownership.
/// Native Controls retain layout, focus, input and clipping responsibility.</summary>
[GlobalClass]
public partial class ViewController:Node
{
    [Export] public ViewCatalog Catalog {get;set;}=null!;
    [Export] public NodePath PagesPath {get;set;}="../Pages";
    [Export] public NodePath OverlaysPath {get;set;}="../Overlay";
    public Control? Page {get;private set;}
    public string CurrentPage {get;private set;}="";
    public string CurrentState {get;private set;}="";
    public bool IsTransitioning {get;private set;}
    public Task PageReady {get;private set;}=Task.CompletedTask;
    public event Action<Exception>? TransitionFailed;
    public ViewContext ModelContext { get; set; } = ViewContext.Empty;
    private readonly Dictionary<Control, ViewInstance> compositions = new();
    private Control pages=null!,overlays=null!;
    private IReadOnlyDictionary<string,ViewEntry> entries=null!;
    private IViewTransitions transitions=null!;
    private readonly Dictionary<string,Control> states=new();
    private readonly CancellationTokenSource lifetime=new();
    private int generation;
    public override void _Ready()
    {
        if(Catalog==null)throw new InvalidOperationException("Assign a native ViewCatalog Resource.");
        entries=Catalog.Validate();pages=GetNode<Control>(PagesPath);overlays=GetNode<Control>(OverlaysPath);
    }
    public void SetTransitions(IViewTransitions owner)=>transitions=owner??throw new ArgumentNullException(nameof(owner));
    private ViewEntry Entry(string id)=>entries.TryGetValue(id,out var entry)?entry:throw new InvalidOperationException("No native view registered: "+id);
    public Control Instantiate(string id)
    {
        var composition=ViewComposer.Create(Entry(id),ModelContext);
        var view=composition.Root;compositions.Add(view,composition);
        view.TreeExiting+=()=>compositions.Remove(view);
        return view;
    }
    public ViewActivation ShowPage(string id,Action<Control> prepare,double maximumWaitSeconds)
    {
        if(transitions==null)throw new InvalidOperationException("Assign the project's transition adapter before showing pages.");
        var entry=Entry(id);if(entry.Kind!=ViewKind.Page)throw new InvalidOperationException("Expected page: "+id);
        if(!double.IsFinite(maximumWaitSeconds)||maximumWaitSeconds<0)throw new ArgumentOutOfRangeException(nameof(maximumWaitSeconds));
        if(Page!=null)_=Observe(transitions.RetirePageAsync(Page,lifetime.Token));
        Page=Instantiate(id);CurrentPage=id;CurrentState=entry.State;pages.AddChild(Page);prepare(Page);
        double duration=transitions.EnterDuration(Page);
        if(!double.IsFinite(duration)||duration<0)throw new InvalidOperationException("Invalid authored transition duration.");
        IsTransitioning=duration>0;int version=++generation;
        PageReady=FinishEntry(Page,Math.Min(duration,maximumWaitSeconds),version);
        _=Observe(PageReady);return new(Page,PageReady);
    }
    public async Task<Control> ChangePageAsync(string id,Action<Control> prepare,double maximumWaitSeconds,CancellationToken token=default)
    {var activation=ShowPage(id,prepare,maximumWaitSeconds);await activation.Ready.WaitAsync(token);return activation.View;}
    private async Task FinishEntry(Control page,double duration,int version)
    {
        if(duration>0)await TweenDelay(page,duration,lifetime.Token);
        if(version==generation)IsTransitioning=false;
    }
    public Control ShowState(string id)
    {
        if(Entry(id).Kind!=ViewKind.State)throw new InvalidOperationException("Expected state: "+id);
        if(states.TryGetValue(id,out var existing))return existing;
        var view=Instantiate(id);states.Add(id,view);pages.AddChild(view);return view;
    }
    public void BringStateForward(string id)
    {
        var view=states[id];pages.MoveChild(view,pages.GetChildCount()-1);
    }
    public void RemoveState(string id)
    {
        if(states.Remove(id,out var view)){view.GetParent()?.RemoveChild(view);view.QueueFree();}
    }
    public Control ShowOverlay(string id)
    {
        if(Entry(id).Kind!=ViewKind.Overlay)throw new InvalidOperationException("Expected overlay: "+id);
        var view=Instantiate(id);overlays.AddChild(view);return view;
    }
    public Task DismissOverlayAsync(Control view)=>transitions.RetireOverlayAsync(view,lifetime.Token);
    private async Task Observe(Task operation)
    {
        try{await operation;}
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){ }
        catch(Exception error){if(TransitionFailed!=null)TransitionFailed(error);else GD.PushError(error.ToString());}
    }
    public static async Task TweenDelay(Control owner,double seconds,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();var tween=owner.CreateTween();tween.TweenInterval(seconds);
        async Task AwaitTween(){await owner.ToSignal(tween,Tween.SignalName.Finished);}
        try{await AwaitTween().WaitAsync(token);}finally{if(tween.IsValid())tween.Kill();}
    }
    public override void _ExitTree(){lifetime.Cancel();lifetime.Dispose();}
}
