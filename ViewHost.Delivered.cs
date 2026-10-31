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
        view.CancelEntry(); view.Lifecycle(p => p.OnBeforeLeave()); view.Lifecycle(p => p.OnStartLeave());
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
        catch { owned.Remove(view.Root); view.Dispose(); throw; }
        return view;
    }
    private void RequireImmediate()
    {
        if (ShowHooks.Count != 0)
            throw new InvalidOperationException("An asynchronous show hook requires ShowPageAsync/ShowOverlayAsync.");
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
        var candidate = CreateOwned(entry, preparedContext ?? Context(entry, model));
        try
        {
            pageLayer.AddChild(candidate.Root);
            prepare(candidate.Root);
            candidate.Lifecycle(p => p.OnBeforeShow());
            candidate.Lifecycle(p => p.OnStartShow());
            double duration = ProjectTransitions.EnterDuration(candidate.Root);
            if (!double.IsFinite(duration) || duration < 0) throw new InvalidOperationException("Invalid entry duration.");
            var previous = CurrentPage;
            CurrentPage = candidate;
            foreach (var pair in states.ToArray())
                if (pair.Value.Definition.State != entry.State) DeactivateState(pair.Key);
            if (entry.State.Length != 0)
                foreach (var state in Catalog.Entries.Where(e => e.Kind == ViewKind.State && e.State == entry.State)) ActivateState(state.Id);
            IsTransitioning = duration > 0;
            int version = ++activationGeneration;
            PageReady = FinishDeliveredEntry(candidate, Math.Min(duration, maximumWaitSeconds), version, states.Values.Select(s => s.Ready).ToArray());
            candidate.Ready = PageReady;
            ObserveDelivered(PageReady);
            if (previous != null) ObserveDelivered(RetireDelivered(previous, false));
            return new(candidate.Root, PageReady);
        }
        catch { if (!ReferenceEquals(CurrentPage, candidate)) candidate.Dispose(); throw; }
    }
    private async Task FinishDeliveredEntry(ViewInstance view, double wait, int version, Task[] stateReady)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, view.EntryToken);
        await view.AnimateAsync(true, linked.Token);
        if (wait > 0) await ViewController.TweenDelay(view.Root, wait, linked.Token);
        await Task.WhenAll(stateReady).WaitAsync(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (GodotObject.IsInstanceValid(view.Root) && view.Root.IsInsideTree()) view.Lifecycle(p => p.OnShown());
        if (version == activationGeneration) IsTransitioning = false;
    }
    public Control ActivateState(string id)
    {
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
            stateLayer.AddChild(view.Root); view.Lifecycle(p => p.OnBeforeShow());
            view.Lifecycle(p => p.OnStartShow());
            states.Add(id, view); view.Ready = EnterDeliveredState(view); ObserveDelivered(view.Ready); return view.Root;
        }
        catch { view.Dispose(); throw; }
    }
    public void BringStateForward(string id)
        => stateLayer.MoveChild(states[id].Root, stateLayer.GetChildCount() - 1);
    public void DeactivateState(string id)
    {
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
        var view = CreateOwned(entry, preparedContext ?? Context(entry, model));
        try
        {
            overlayLayer.AddChild(view.Root); view.Lifecycle(p => p.OnBeforeShow());
            view.Lifecycle(p => p.OnStartShow()); overlays.Add(view);
            view.Ready = EnterDeliveredOverlay(view);
            ObserveDelivered(view.Ready); return view;
        }
        catch { view.Dispose(); throw; }
    }
    private async Task EnterDeliveredOverlay(ViewInstance view)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, view.EntryToken);
        await view.AnimateAsync(true, linked.Token);
        double wait = ProjectTransitions?.OverlayEnterDuration(view.Root) ?? 0;
        if (!double.IsFinite(wait) || wait < 0) throw new InvalidOperationException("Invalid overlay entry duration.");
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
        try { await DrainTransitionsAsync(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        // Release registered children before their owning page frees its subtree.
        static int Depth(Node node) { int depth = 0; while (node.GetParent() is Node parent) { depth++; node = parent; } return depth; }
        foreach (var view in owned.Values.Where(v => GodotObject.IsInstanceValid(v.Root)).OrderByDescending(v => Depth(v.Root)).ToArray())
        {
            if (!GodotObject.IsInstanceValid(view.Root)) continue;
            view.Lifecycle(p => p.OnBeforeLeave()); view.Lifecycle(p => p.OnStartLeave());
            view.Root.Hide(); view.Lifecycle(p => p.OnHidden()); view.Dispose();
        }
        owned.Clear(); states.Clear(); overlays.Clear(); CurrentPage = null;
        if (IsInsideTree()) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task<ViewInstance> ShowDeliveredAsync(string id, bool overlay, ViewModels? model, CancellationToken token)
    {
        Initialize();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        token = linked.Token;
        var entry = Entry(id, overlay ? ViewKind.Overlay : ViewKind.Page);
        var request = new ViewPageShowContext(id, overlay, Context(entry, model));
        await gate.WaitAsync(token);
        var invoked = new List<IViewPageShowHook>(); bool committed = false;
        try
        {
            foreach (var hook in ShowHooks) { token.ThrowIfCancellationRequested(); invoked.Add(hook); await hook.BeforePrepareAsync(request, token); }
            token.ThrowIfCancellationRequested(); ViewInstance result;
            if (overlay) result = ActivateOverlayCore(id, model, request.Models);
            else { ActivatePageCore(id, _ => { }, double.MaxValue, model, request.Models); result = CurrentPage!; }
            committed = true;
            await result.Ready.WaitAsync(token);
            foreach (var hook in invoked) { token.ThrowIfCancellationRequested(); await hook.AfterReadyAsync(request, token); }
            return result;
        }
        catch
        {
            if (!committed) foreach (var hook in invoked.AsEnumerable().Reverse()) hook.OnAborted(request);
            throw;
        }
        finally { gate.Release(); }
    }
}
