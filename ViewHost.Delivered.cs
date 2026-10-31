using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

/// <summary>Animation timing only. ViewHost exclusively owns root disposal.</summary>
public interface IViewAnimationPolicy
{
    double EnterDuration(Control view);
    double OverlayEnterDuration(Control view) => EnterDuration(view);
    Task RetirePageAsync(Control view, CancellationToken token);
    Task RetireOverlayAsync(Control view, CancellationToken token);
}

// The delivered game's immediate activation/overlapping retirement policy.
// ViewHost owns instances and lifecycle; the project adapter owns animation only.
public partial class ViewHost
{
    [Export] public NodePath PagesPath { get; set; } = new("");
    [Export] public NodePath StatesPath { get; set; } = new("");
    [Export] public NodePath OverlaysPath { get; set; } = new("");
    public IViewAnimationPolicy? ProjectTransitions { get; set; }
    public event Action<ViewInstance>? Prepared;
    public bool IsTransitioning { get; private set; }
    public string CurrentState => CurrentPage?.Definition.State ?? "";
    public Task PageReady { get; private set; } = Task.CompletedTask;
    public void RefreshPageModels(ViewModels model)
    {
        if (CurrentPage == null) throw new InvalidOperationException("No active page to refresh.");
        CurrentPage.Rebind(Context(CurrentPage.Definition, model));
        CurrentPage.Lifecycle(p => p.RefreshView());
        foreach (var state in states.Values)
        { state.Rebind(CurrentPage.Context); state.Lifecycle(p => p.RefreshView()); }
    }
    public int OwnedInstanceCount => owned.Count;
    private readonly Dictionary<Control, ViewInstance> owned = new();
    private readonly HashSet<Task> retiring = new();
    private readonly HashSet<ViewInstance> leaving = new();
    private void BeginLeave(ViewInstance view)
    {
        if (!leaving.Add(view)) return;
        view.CancelEntry();
        var errors = new List<Exception>();
        try { view.CleanupLifecycle(p => p.OnBeforeLeave()); } catch (Exception error) { errors.Add(error); }
        try { view.CleanupLifecycle(p => p.OnStartLeave()); } catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException("Outgoing lifecycle failed.", errors);
    }
    private void ReleaseNestedElements(ViewInstance parent)
    {
        var errors = new List<Exception>();
        foreach (var child in owned.Values.Where(v => v != parent && GodotObject.IsInstanceValid(v.Root) && parent.Root.IsAncestorOf(v.Root)).ToArray())
        {
            if (!owned.ContainsKey(child.Root)) continue;
            try { ReleaseElement(child.Root); } catch (Exception error) { errors.Add(error); }
        }
        if (errors.Count != 0) throw new AggregateException("Nested view cleanup failed.", errors);
    }
    private int activationGeneration;
    private Control? logicalCanvas;
    public override void _Notification(int what)
    { if (what == NotificationResized && logicalCanvas != null) FitLogicalCanvas(); }
    private void FitLogicalCanvas()
    {
        if (logicalCanvas == null || Size.X <= 0 || Size.Y <= 0 || Catalog.ReferenceHeight <= 0) return;
        logicalCanvas.Size = new Vector2(Size.X / Size.Y * Catalog.ReferenceHeight, Catalog.ReferenceHeight);
        logicalCanvas.Scale = Vector2.One * (Size.Y / Catalog.ReferenceHeight);
    }

    private ViewInstance CreateOwned(ViewEntry entry, ViewContext context)
    {
        var view = ViewComposer.Create(entry, context);
        owned.Add(view.Root, view);
        view.Root.TreeExiting += () => owned.Remove(view.Root);
        try { Prepared?.Invoke(view); }
        catch (Exception failure)
        {
            owned.Remove(view.Root);var errors=new List<Exception>();
            try { ReleaseNestedElements(view); } catch(Exception error) { errors.Add(error); }
            try { view.Dispose(); } catch(Exception error) { errors.Add(error); }
            if(errors.Count>0)throw new AggregateException("View preparation and cleanup failed.",new[]{failure}.Concat(errors));
            throw;
        }
        return view;
    }
    private void RequireImmediate()
    {
        RequireNoAsyncNavigation();
        if (ShowHooks.Count != 0)
            throw new InvalidOperationException("An asynchronous show hook requires ShowPageAsync/ShowOverlayAsync.");
    }
    private void RequireNoAsyncNavigation()
    {
        if (gate.CurrentCount == 0)
            throw new InvalidOperationException("Immediate activation cannot interrupt an asynchronous navigation transaction.");
    }
    public ViewActivation ActivatePage(string id, Action<Control> prepare, double maximumWaitSeconds,
        ViewModels? model = null)
    {
        RequireImmediate();
        return ActivatePageCore(id, prepare, maximumWaitSeconds, model);
    }
    private ViewActivation ActivatePageCore(string id, Action<Control> prepare, double maximumWaitSeconds,
        ViewModels? model, ViewContext? preparedContext = null)
    {
        Initialize();
        if (ProjectTransitions == null) throw new InvalidOperationException("Assign project transition semantics.");
        if (!double.IsFinite(maximumWaitSeconds) || maximumWaitSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumWaitSeconds));
        var entry = Entry(id, ViewKind.Page);
        var context = preparedContext ?? Context(entry, model);
        var staged = new List<ViewInstance>();
        var newStates = new Dictionary<string, ViewInstance>(StringComparer.Ordinal);
        var started = new HashSet<ViewInstance>();
        ViewInstance candidate;
        double duration;
        try
        {
            candidate = Mount(entry, context, pageLayer); staged.Add(candidate);
            foreach (var definition in StateDefinitions(entry))
                if (StateNeedsCreation(definition.Id))
                { var state = Mount(definition, context, stateLayer); newStates.Add(definition.Id, state); staged.Add(state); }
            prepare(candidate.Root);
            foreach (var state in newStates.Values) { started.Add(state); BeginEntry(state); }
            started.Add(candidate); BeginEntry(candidate);
            duration = ProjectTransitions.EnterDuration(candidate.Root); ValidateDuration(duration);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception>();
            foreach (var view in staged.AsEnumerable().Reverse())
            {
                if (started.Contains(view))
                    foreach (var callback in new Action<IViewElementLifeCycle>[] { p => p.OnBeforeLeave(), p => p.OnStartLeave(), p => p.OnHidden() })
                        try { view.CleanupLifecycle(callback); } catch (Exception error) { errors.Add(error); }
                try { ReleaseNestedElements(view); } catch (Exception error) { errors.Add(error); }
                owned.Remove(view.Root);
                try { view.Dispose(); } catch (Exception error) { errors.Add(error); }
            }
            if (errors.Count > 0) throw new AggregateException("Immediate preparation failed and rollback reported errors.", new[] { failure }.Concat(errors));
            throw;
        }
        var previous = CurrentPage;
        var obsolete = states.Where(p => p.Value.Definition.State != entry.State || newStates.ContainsKey(p.Key)).ToArray();
        CurrentPage = candidate;
        foreach (var pair in obsolete) states.Remove(pair.Key);
        foreach (var pair in newStates) states.Add(pair.Key, pair.Value);
        IsTransitioning = true;
        int version = ++activationGeneration;
        var stateReady = new List<Task>();
        foreach (var state in states.Values)
        {
            if (newStates.ContainsKey(state.Definition.Id))
            { state.Ready = EnterDeliveredState(state); stateReady.Add(state.Ready); }
            else
                try { state.Rebind(context); state.Lifecycle(p => p.RefreshView()); stateReady.Add(state.Ready); }
                catch (Exception error) { stateReady.Add(Task.FromException(error)); }
        }
        PageReady = FinishDeliveredEntry(candidate, Math.Min(duration, maximumWaitSeconds), version, stateReady.ToArray());
        candidate.Ready = PageReady; ObserveDelivered(PageReady);
        foreach (var outgoing in obsolete.Select(p => p.Value).Concat(previous == null ? Array.Empty<ViewInstance>() : new[] { previous }))
            ObserveDelivered(RetireDelivered(outgoing, false));
        return new(candidate.Root, PageReady);
    }
    private async Task FinishDeliveredEntry(ViewInstance view, double wait, int version, Task[] stateReady)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, view.EntryToken);
        try
        {
            await view.AnimateAsync(true, linked.Token);
            if (wait > 0) await ViewController.TweenDelay(view.Root, wait, linked.Token);
            await Task.WhenAll(stateReady).WaitAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (GodotObject.IsInstanceValid(view.Root) && view.Root.IsInsideTree()) view.Lifecycle(p => p.OnShown());
        }
        finally { if (version == activationGeneration) IsTransitioning = false; }
    }
    public Control ActivateState(string id)
    {
        RequireNoAsyncNavigation();
        Initialize();
        var entry = Entry(id, ViewKind.State);
        var context = CurrentPage?.Context ?? Context(entry, null);
        if (states.TryGetValue(id, out var existing))
        {
            if (!ReferenceEquals(existing.Context, context))
            { existing.Rebind(context); existing.Lifecycle(p => p.RefreshView()); }
            return existing.Root;
        }
        var view = CreateOwned(entry, context);
        try
        {
            view.ValidateAnimations(); stateLayer.AddChild(view.Root); view.Lifecycle(p => p.OnBeforeShow());
            view.Lifecycle(p => p.OnStartShow());
            states.Add(id, view); view.Ready = EnterDeliveredState(view); ObserveDelivered(view.Ready); return view.Root;
        }
        catch { owned.Remove(view.Root); view.Dispose(); throw; }
    }
    public void BringStateForward(string id)
        => stateLayer.MoveChild(states[id].Root, stateLayer.GetChildCount() - 1);
    public void DeactivateState(string id)
    {
        RequireNoAsyncNavigation();
        if (!states.Remove(id, out var view)) return;
        ObserveDelivered(RetireDelivered(view, false));
    }
    private async Task EnterDeliveredState(ViewInstance view)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, view.EntryToken);
        await view.AnimateAsync(true, linked.Token);
        linked.Token.ThrowIfCancellationRequested(); view.Lifecycle(p => p.OnShown());
    }
    public ViewInstance ActivateOverlay(string id, ViewModels? model = null)
    { RequireImmediate(); return ActivateOverlayCore(id, model); }
    private ViewInstance ActivateOverlayCore(string id, ViewModels? model, ViewContext? preparedContext = null)
    {
        Initialize(); var entry = Entry(id, ViewKind.Overlay);
        var view = Mount(entry, preparedContext ?? Context(entry, model), overlayLayer);
        try
        {
            BeginEntry(view);
            double wait = ProjectTransitions?.OverlayEnterDuration(view.Root) ?? 0; ValidateDuration(wait);
            overlays.Add(view); view.Ready = EnterDeliveredOverlay(view, wait);
            ObserveDelivered(view.Ready); return view;
        }
        catch (Exception failure)
        {
            overlays.Remove(view); var errors = new List<Exception>();
            foreach (var callback in new Action<IViewElementLifeCycle>[] { p => p.OnBeforeLeave(), p => p.OnStartLeave(), p => p.OnHidden() })
                try { view.CleanupLifecycle(callback); } catch (Exception error) { errors.Add(error); }
            try { ReleaseNestedElements(view); } catch (Exception error) { errors.Add(error); }
            owned.Remove(view.Root);
            try { view.Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Overlay preparation and cleanup failed.", new[] { failure }.Concat(errors));
            throw;
        }
    }
    private async Task EnterDeliveredOverlay(ViewInstance view, double wait)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, view.EntryToken);
        await view.AnimateAsync(true, linked.Token);
        if (wait > 0) await ViewController.TweenDelay(view.Root, wait, linked.Token);
        linked.Token.ThrowIfCancellationRequested(); view.Lifecycle(p => p.OnShown());
    }
    public Task DismissDeliveredOverlayAsync(Control root)
    {
        if (!owned.TryGetValue(root, out var view) || !overlays.Remove(view))
            throw new InvalidOperationException("Overlay does not belong to this host.");
        var operation = RetireDelivered(view, true); ObserveDelivered(operation); return operation;
    }
    private async Task RetireDelivered(ViewInstance view, bool overlay)
    {
        var errors = new List<Exception>();
        bool hidden = false;
        try
        {
            BeginLeave(view);
            await view.AnimateAsync(false, lifetime.Token);
            if (ProjectTransitions != null && view.Definition.Kind != ViewKind.State)
            {
                if (overlay) await ProjectTransitions.RetireOverlayAsync(view.Root, lifetime.Token);
                else await ProjectTransitions.RetirePageAsync(view.Root, lifetime.Token);
            }
            view.Root.Hide(); hidden = true; view.Lifecycle(p => p.OnHidden());
        }
        catch (Exception error) { errors.Add(error); }
        // Cancellation shortens animation, not the leave lifecycle. Cleanup
        // errors remain visible without masking the original transition error.
        if (!hidden)
        {
            try { view.Root.Hide(); view.Lifecycle(p => p.OnHidden()); }
            catch (Exception error) { errors.Add(error); }
        }
        try { ReleaseNestedElements(view); } catch (Exception error) { errors.Add(error); }
        leaving.Remove(view); owned.Remove(view.Root);
        try { await view.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("View retirement and cleanup failed.", errors);
    }
    public Control CreateElement(string id, ViewModels? model = null)
    {
        Initialize(); var entry = Entry(id, ViewKind.Element);
        if (entry.ShowAnimation.Length != 0 || entry.HideAnimation.Length != 0)
            throw new InvalidOperationException("Detached synchronous elements cannot own asynchronous animations.");
        var view = CreateOwned(entry, Context(entry, model));
        view.Root.Ready += () => { view.Lifecycle(p => p.OnBeforeShow()); view.Lifecycle(p => p.OnStartShow()); view.Lifecycle(p => p.OnShown()); };
        return view.Root;
    }
    public void ReleaseElement(Control root)
    {
        if (!owned.Remove(root, out var view)) throw new InvalidOperationException("Unknown element.");
        try
        {
            BeginLeave(view); ReleaseNestedElements(view);
            root.Hide(); view.Lifecycle(p => p.OnHidden());
        }
        finally { leaving.Remove(view); view.Dispose(); }
    }
    private void ObserveDelivered(Task task)
    { retiring.Add(task); _ = ObserveDeliveredCore(task); }
    private async Task ObserveDeliveredCore(Task task)
    {
        try { await task; }
        catch (OperationCanceledException) when (task.IsCanceled) { }
        catch (Exception error) { if (Failed != null) Failed(error); else GD.PushError(error.ToString()); }
        finally { retiring.Remove(task); }
    }
    public async Task DrainTransitionsAsync() => await Task.WhenAll(retiring.ToArray());
    public async Task ShutdownAsync()
    {
        lifetime.Cancel();
        var errors = new List<Exception>();
        try { await DrainTransitionsAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { errors.Add(error); }
        // Let an aborted transaction release its candidates before disposing the host.
        await gate.WaitAsync();
        try
        {
            static int Depth(Node node) { int depth = 0; while (node.GetParent() is Node parent) { depth++; node = parent; } return depth; }
            foreach (var view in owned.Values.Where(v => GodotObject.IsInstanceValid(v.Root)).OrderByDescending(v => Depth(v.Root)).ToArray())
            {
                if (!owned.ContainsKey(view.Root)) continue;
                try { BeginLeave(view); } catch (Exception error) { errors.Add(error); }
                try { view.Root.Hide(); view.CleanupLifecycle(p => p.OnHidden()); } catch (Exception error) { errors.Add(error); }
                owned.Remove(view.Root); leaving.Remove(view);
                try { await view.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            }
            owned.Clear(); states.Clear(); overlays.Clear(); CurrentPage = null; IsTransitioning = false;
        }
        finally { gate.Release(); }
        if (errors.Count > 0) throw new AggregateException("ViewHost shutdown reported cleanup errors.", errors);
    }
}
