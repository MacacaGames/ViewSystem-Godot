using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

public partial class ViewHost
{
    private Rect2 GetNormalizedSafeArea()
    {
        if(UseSimulatedSafeArea)return SimulatedSafeArea;
        if(!IsInsideTree()||Size.X<=0||Size.Y<=0||DisplayMetricsProvider==null&&DisplayServer.GetName()=="headless")return new(0,0,1,1);
        return ViewDisplayMetrics.Normalize((DisplayMetricsProvider??ViewDisplayMetrics.Capture)(this));
    }
    private bool StateGroupRequired(string group,ViewEntry? nextPage=null)
        => group.Length>0&&((nextPage??CurrentPage?.Definition)?.State==group||overlays.Any(v=>v.Definition.State==group));
    private ViewContext ContextForState(ViewInstance state)
        => overlays.LastOrDefault(v=>v.Definition.State==state.Definition.State)?.Context??CurrentPage?.Context??state.Context;
    private async Task ReconcileStatesAsync()
    {
        var errors=new System.Collections.Generic.List<Exception>();
        foreach(var pair in states.ToArray())
        {
            if(!StateGroupRequired(pair.Value.Definition.State))
            {states.Remove(pair.Key);try{await RetireDelivered(pair.Value,false);}catch(Exception error){errors.Add(error);}}
            else try{pair.Value.Rebind(ContextForState(pair.Value));pair.Value.Lifecycle(p=>p.RefreshView());}catch(Exception error){errors.Add(error);}
        }
        if(errors.Count>0)throw new AggregateException("State retirement/refresh failed after overlay removal.",errors);
    }
    private readonly System.Collections.Generic.Dictionary<ViewInstance,WeakReference<Control>> previousFocus=new();
    private readonly System.Collections.Generic.Dictionary<Control,Control.FocusModeEnum> suspendedFocus=new();
    private readonly System.Collections.Generic.Dictionary<ViewInstance,Control> modalBarriers=new();
    private void CommitPresentation(ViewInstance view,Control? priorFocus=null)
    {
        if(priorFocus!=null)previousFocus[view]=new(priorFocus);
        if(view.Definition.Kind==ViewKind.Overlay&&view.Definition.Modal)
        {
            var barrier=new Control {Name="ModalBarrier",MouseFilter=MouseFilterEnum.Stop};view.Root.GetParent().AddChild(barrier);barrier.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);modalBarriers[view]=barrier;
        }
        SortPresentation();UpdateNavigation();UpdateFrameRate();FocusPresentation(overlays.LastOrDefault()??view);
    }
    private void SortPresentation()
    {
        foreach(var layer in owned.Values.Select(v=>v.Root.GetParent()).OfType<Control>().Distinct())
        {
            var views=owned.Values.Where(v=>GodotObject.IsInstanceValid(v.Root)&&v.Root.GetParent()==layer).OrderBy(v=>v.Order).ToArray();
            foreach(var view in views)
            {if(modalBarriers.TryGetValue(view,out var barrier))layer.MoveChild(barrier,layer.GetChildCount()-1);layer.MoveChild(view.Root,layer.GetChildCount()-1);}
        }
    }
    private void UpdateNavigation()
    {
        foreach(var pair in suspendedFocus)if(GodotObject.IsInstanceValid(pair.Key))pair.Key.FocusMode=pair.Value;suspendedFocus.Clear();
        foreach(var state in states.Values)
        {
            state.ResetFocusLinks();
            var owner=overlays.LastOrDefault(v=>v.Definition.State==state.Definition.State)??CurrentPage;
            if(owner!=null)foreach(var data in owner.Definition.StateNavigation.Where(n=>n.StateId==state.Definition.Id))state.ApplyFocusLinks(data.Links);
        }
        var top=overlays.LastOrDefault();if(top==null||!top.Definition.NavigationEnabled&&!top.Definition.Modal)return;
        foreach(var view in owned.Values.Where(v=>v!=top&&!top.Root.IsAncestorOf(v.Root)&&!(v.Definition.Kind==ViewKind.State&&v.Definition.State.Length>0&&v.Definition.State==top.Definition.State)))
            foreach(var node in ViewComposer.Walk(view.Root).OfType<Control>().Where(n=>n.FocusMode!=Control.FocusModeEnum.None))
            {suspendedFocus.TryAdd(node,node.FocusMode);node.FocusMode=Control.FocusModeEnum.None;}
    }
    private void RestoreFocus(ViewInstance? dismissed=null)
    {
        UpdateNavigation();SortPresentation();
        if(dismissed!=null&&previousFocus.Remove(dismissed,out var weak)&&weak.TryGetTarget(out var previous)&&GodotObject.IsInstanceValid(previous)&&previous.IsInsideTree()&&previous.IsVisibleInTree()&&previous.FocusMode!=Control.FocusModeEnum.None)
        {previous.GrabFocus();return;}
        if((overlays.LastOrDefault()??CurrentPage) is {} active)FocusPresentation(active);
    }
    private void FocusPresentation(ViewInstance view)
    {
        if(!view.Definition.NavigationEnabled)return;
        if(view.Definition.FirstSelectedState.Length==0)view.Focus();
        else if(states.TryGetValue(view.Definition.FirstSelectedState,out var state))state.FocusControl(view.Definition.FirstSelected);
        else throw new InvalidOperationException("FirstSelected State is not active: "+view.Definition.FirstSelectedState);
    }
    private void ReleasePresentation(ViewInstance view)
    {if(modalBarriers.Remove(view,out var barrier)&&GodotObject.IsInstanceValid(barrier)){barrier.GetParent()?.RemoveChild(barrier);barrier.QueueFree();}}
    public bool IsFullPageLive(string id)=>CurrentPage?.Definition.Id==id;
    public bool IsOverlayLive(string id)=>overlays.Any(v=>v.Definition.Id==id);
    public bool IsStateLive(string id)=>states.ContainsKey(id);
    public bool HasView(string id)=>Catalog.Entries.Any(e=>e.Id==id);
    public Control GetCurrentPageElement(string id)=>CurrentPage?.Elements.TryGetValue(id,out var node)==true?node:throw new InvalidOperationException("No current page element: "+id);
    public void RefreshAll()
    {
        UpdateFrameRate();
        foreach(var view in owned.Values.Concat(requestedViews).ToArray())
        {view.Rebind(Context(view.Definition,view.Context.Page));view.Lifecycle(p=>p.RefreshView());}
    }
    public void RefreshFullPage(){if(CurrentPage!=null){CurrentPage.Rebind(Context(CurrentPage.Definition,CurrentPage.Context.Page));CurrentPage.Lifecycle(p=>p.RefreshView());}}
    public void RefreshOverlayPage(){foreach(var view in overlays){view.Rebind(Context(view.Definition,view.Context.Page));view.Lifecycle(p=>p.RefreshView());}}
    public void SetSharedModels(params object[] values){SharedModels=ViewModels.FromObjects(values);RefreshAll();}
    public T GetSingletonViewElement<T>() where T:Node,IViewElementSingleton=>ElementPool.GetSingleton<T>();
    public Control GetPageElement(string page,string element)=>owned.Values.FirstOrDefault(v=>v.Definition.Id==page&&v.Definition.Kind is ViewKind.Page or ViewKind.Overlay)?.Elements.GetValueOrDefault(element)??throw new InvalidOperationException("No live page element: "+page+"/"+element);
    public Control GetStateElement(string state,string element)=>states.TryGetValue(state,out var view)&&view.Elements.TryGetValue(element,out var node)?node:throw new InvalidOperationException("No live State element: "+state+"/"+element);
    public void SetBreakPoint(string name)
    {
        if(string.IsNullOrWhiteSpace(name))throw new ArgumentException("Breakpoint name is empty.",nameof(name));
        ActiveBreakpoints.Add(name);RefreshAll();
    }
    public void RemoveBreakPoint(string name){ActiveBreakpoints.Remove(name);RefreshAll();}
    public void ClearBreakPoint(){ActiveBreakpoints.Clear();RefreshAll();}
    public string[] GetActiveBreakPoints()=>ActiveBreakpoints.OrderBy(n=>n,StringComparer.Ordinal).ToArray();
    public async Task LeaveAllOverlaysAsync(string[]? ignore=null,CancellationToken token=default)
    {
        foreach(var view in overlays.AsEnumerable().Reverse().ToArray())
            if(ignore?.Contains(view.Definition.Id)!=true)await DismissOverlayAsync(view,token);
    }
    public ViewPageChanger FullPageChanger()=>new(this,false);
    public ViewPageChanger OverlayPageChanger()=>new(this,true);
}

/// <summary>Awaitable fluent navigation; no process-global controller singleton.</summary>
public sealed class ViewPageChanger
{
    private readonly ViewHost host;
    private readonly bool overlay;
    private string id="";
    private ViewModels? model;
    private Action? completed;
    private ViewNavigationOptions options=new();
    internal ViewPageChanger(ViewHost host,bool overlay){this.host=host;this.overlay=overlay;}
    public ViewPageChanger SetPage(string value){id=value;return this;}
    public ViewPageChanger SetPageModel(params object[] values){model=ViewModels.FromObjects(values);return this;}
    public ViewPageChanger SetPageModel(ViewModels value){model=value;return this;}
    public ViewPageChanger OnStart(Action callback){options=options with {OnStart=callback};return this;}
    public ViewPageChanger OnChanged(Action callback){options=options with {OnChanged=_=>callback()};return this;}
    public ViewPageChanger SetNavigationMode(ViewNavigationMode value){options=options with {Mode=value};return this;}
    public ViewPageChanger SetWaitPreviousPageFinish(bool value){options=options with {WaitPreviousPageFinish=value};return this;}
    public ViewPageChanger SetIgnoreClickProtection(bool value){options=options with {IgnoreClickProtection=value};return this;}
    public ViewPageChanger SetIgnoreTransition(bool value){options=options with {IgnoreTransition=value};return this;}
    public ViewPageChanger SetIgnoreTimeScale(bool value){options=options with {IgnoreTimeScale=value};return this;}
    public ViewPageChanger SetReplayWhileSamePage(bool value){options=options with {ReplayWhileSamePage=value};return this;}
    public ViewPageChanger SetOrder(int value){options=options with {SortOrder=value};return this;}
    public ViewPageChanger SetCustomRoot(Control value){options=options with {CustomRoot=value};return this;}
    public ViewPageChanger OnComplete(Action callback){completed=callback;return this;}
    public async Task<ViewInstance> Show(CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var result=overlay?await host.ShowOverlayAsync(id,model,token,options):await host.ShowPageAsync(id,model,token,options);
        completed?.Invoke();return result;
    }
    public async Task Leave(CancellationToken token=default)
    {
        if(!overlay)throw new InvalidOperationException("Leave requires an overlay changer.");
        var view=host.Overlays.LastOrDefault(v=>v.Definition.Id==id)??throw new InvalidOperationException("Overlay is not live: "+id);
        options.OnStart?.Invoke();await host.DismissOverlayAsync(view,token);completed?.Invoke();
    }
}
