using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

/// <summary>Runtime owner for authored pages, persistent states, overlays and asynchronous show hooks.</summary>
[GlobalClass]
public partial class ViewHost : Control
{
    [Export] public ViewCatalog Catalog { get; set; } = null!;
    [Export] public string InitialPage { get; set; } = "";
    [Export] public bool UsePreviewModels { get; set; }
    public ViewModels SharedModels { get; set; } = new();
    public List<IViewPageShowHook> ShowHooks { get; } = new();
    public ViewInstance? CurrentPage { get; private set; }
    public IReadOnlyList<ViewInstance> Overlays => overlays;
    public IReadOnlyDictionary<string, ViewInstance> States => states;
    public event Action<string, Control>? ActionRequested;
    public event Action<Exception>? Failed;
    private readonly List<ViewInstance> overlays = new();
    private readonly Dictionary<string, ViewInstance> states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private Control pageLayer = null!, stateLayer = null!, overlayLayer = null!;
    private bool initialized;
    public override void _Ready()
    {
        Initialize();
        if (!string.IsNullOrWhiteSpace(InitialPage)) _ = StartAsync();
    }
    private async Task StartAsync()
    {
        try { await ShowPageAsync(InitialPage); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (Failed != null) Failed(error); else GD.PushError(error.ToString()); }
    }
    private void Initialize()
    {
        if (initialized) return;
        if (Catalog == null) throw new InvalidOperationException("ViewHost requires a ViewCatalog.");
        Catalog.Validate();
        pageLayer = PagesPath.IsEmpty ? Layer("Pages") : GetNode<Control>(PagesPath);
        stateLayer = StatesPath.IsEmpty ? Layer("States") : GetNode<Control>(StatesPath);
        overlayLayer = OverlaysPath.IsEmpty ? Layer("Overlays") : GetNode<Control>(OverlaysPath);
        initialized = true;
    }
    private Control Layer(string name)
    {
        var layer = new Control { Name = name, MouseFilter = MouseFilterEnum.Ignore };
        if (Catalog.ReferenceHeight > 0)
        {
            if (logicalCanvas == null)
            {
                logicalCanvas = new Control { Name = "Canvas", MouseFilter = MouseFilterEnum.Ignore };
                AddChild(logicalCanvas); FitLogicalCanvas();
            }
            logicalCanvas.AddChild(layer);
        }
        else AddChild(layer);
        layer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); return layer;
    }
    private ViewEntry Entry(string id, ViewKind kind)
    {
        var entries = Catalog.Validate();
        if (!entries.TryGetValue(id, out var entry) || entry.Kind != kind)
            throw new InvalidOperationException($"'{id}' is not a {kind} in this catalog.");
        return entry;
    }
    private ViewContext Context(ViewEntry entry, ViewModels? model)
        => new(model ?? (UsePreviewModels ? ViewModels.From(entry.PreviewModel) : new()),
            UsePreviewModels ? ViewModels.From(Catalog.SharedPreviewModel) : SharedModels,
            Dispatch: (action, origin) => ActionRequested?.Invoke(action, origin));

    public Task<ViewInstance> ShowPageAsync(string id, ViewModels? model = null, CancellationToken token = default)
        => ShowAsync(id, false, model, token);
    public Task<ViewInstance> ShowOverlayAsync(string id, ViewModels? model = null, CancellationToken token = default)
        => ShowAsync(id, true, model, token);
    private async Task<ViewInstance> ShowAsync(string id, bool overlay, ViewModels? model, CancellationToken token)
    {
        Initialize();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        token = linked.Token;
        var entry = Entry(id, overlay ? ViewKind.Overlay : ViewKind.Page);
        var context = Context(entry, model);
        var request = new ViewPageShowContext(id, overlay, context);
        await gate.WaitAsync(token);
        var invoked = new List<IViewPageShowHook>();
        var candidates = new List<ViewInstance>();
        var started = new HashSet<ViewInstance>();
        var nextStates = new Dictionary<string, ViewInstance>(StringComparer.Ordinal);
        ViewInstance? result = null;
        Exception? failure = null;
        bool committed = false;
        var cleanupErrors = new List<Exception>();
        try
        {
            IsTransitioning = true;
            foreach (var hook in ShowHooks.ToArray())
            { token.ThrowIfCancellationRequested(); invoked.Add(hook); await hook.BeforePrepareAsync(request, token); }
            token.ThrowIfCancellationRequested();
            result = Mount(entry, context, overlay ? overlayLayer : pageLayer); candidates.Add(result);
            if (!overlay)
                foreach (var definition in StateDefinitions(entry))
                    if (StateNeedsCreation(definition.Id))
                    { var state = Mount(definition, context, stateLayer); nextStates.Add(definition.Id, state); candidates.Add(state); }
            foreach (var state in nextStates.Values)
            {
                started.Add(state); state.Ready = EnterCandidateAsync(state, token);
                await state.Ready;
            }
            started.Add(result); result.Ready = EnterCandidateAsync(result, token);
            await result.Ready;
            foreach (var hook in invoked)
            { token.ThrowIfCancellationRequested(); await hook.AfterReadyAsync(request, token); }
            token.ThrowIfCancellationRequested();

            if (overlay) { overlays.Add(result); committed = true; }
            else
            {
                // No await or user callback is allowed between these ownership assignments.
                var previous = CurrentPage;
                var obsolete = states.Where(p => p.Value.Definition.State != entry.State || nextStates.ContainsKey(p.Key)).ToArray();
                CurrentPage = result; PageReady = result.Ready; ++activationGeneration;
                foreach (var pair in obsolete) states.Remove(pair.Key);
                foreach (var pair in nextStates) states.Add(pair.Key, pair.Value);
                committed = true;
                foreach (var state in states.Values.Where(v => !nextStates.ContainsKey(v.Definition.Id)))
                    try { state.Rebind(context); state.Lifecycle(p => p.RefreshView()); }
                    catch (Exception error) { cleanupErrors.Add(error); }
                foreach (var outgoing in obsolete.Select(p => p.Value).Concat(previous == null ? Array.Empty<ViewInstance>() : new[] { previous }))
                    try { await RetireDelivered(outgoing, false); } catch (Exception error) { cleanupErrors.Add(error); }
                if (cleanupErrors.Count > 0) throw new ViewNavigationCommittedException(entry.Id, false, cleanupErrors);
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            try
            {
                if (!committed)
                {
                    foreach (var hook in invoked.AsEnumerable().Reverse())
                        try { hook.OnAborted(request); } catch (Exception error) { cleanupErrors.Add(error); }
                    foreach (var candidate in candidates.AsEnumerable().Reverse())
                        await AbortCandidateAsync(candidate, started.Contains(candidate), cleanupErrors);
                }
            }
            finally { IsTransitioning = CurrentPage?.Ready.IsCompleted == false; gate.Release(); }
        }
        if (failure != null)
        {
            if (!committed && cleanupErrors.Count > 0)
                throw new AggregateException("Navigation failed and rollback reported errors.", new[] { failure }.Concat(cleanupErrors));
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return result!;
    }
    private bool StateNeedsCreation(string id)
        => !states.TryGetValue(id, out var state) || state.Ready.IsFaulted || state.Ready.IsCanceled;
    private IEnumerable<ViewEntry> StateDefinitions(ViewEntry page)
        => Catalog.Entries.Where(e => e.Kind == ViewKind.State && page.State.Length != 0 && e.State == page.State);
    private ViewInstance Mount(ViewEntry entry, ViewContext context, Control layer)
    {
        var instance = CreateOwned(entry, context);
        try
        {
            instance.ValidateAnimations(); instance.Root.Visible = false; layer.AddChild(instance.Root);
            return instance;
        }
        catch (Exception failure)
        {
            owned.Remove(instance.Root); var errors = new List<Exception>();
            try { ReleaseNestedElements(instance); } catch (Exception error) { errors.Add(error); }
            try { instance.Dispose(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count > 0) throw new AggregateException("Mount failed and cleanup reported errors.", new[] { failure }.Concat(errors));
            throw;
        }
    }
    private void BeginEntry(ViewInstance instance)
    {
        instance.Lifecycle(p => p.OnBeforeShow()); instance.Root.Show(); instance.Lifecycle(p => p.OnStartShow());
    }
    private async Task EnterCandidateAsync(ViewInstance instance, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); BeginEntry(instance);
        await instance.AnimateAsync(true, token);
        double duration = instance.Definition.Kind == ViewKind.Overlay ? ProjectTransitions?.OverlayEnterDuration(instance.Root) ?? 0 :
            instance.Definition.Kind == ViewKind.Page ? ProjectTransitions?.EnterDuration(instance.Root) ?? 0 : 0;
        ValidateDuration(duration);
        if (duration > 0) await ViewController.TweenDelay(instance.Root, duration, token);
        token.ThrowIfCancellationRequested(); instance.Lifecycle(p => p.OnShown());
    }
    private static void ValidateDuration(double duration)
    { if (!double.IsFinite(duration) || duration < 0) throw new InvalidOperationException("Invalid entry duration."); }
    private async Task AbortCandidateAsync(ViewInstance candidate, bool started, List<Exception> errors)
    {
        if (started)
            foreach (var callback in new Action<IViewElementLifeCycle>[] { p => p.OnBeforeLeave(), p => p.OnStartLeave(), p => p.OnHidden() })
                try { candidate.CleanupLifecycle(callback); } catch (Exception error) { errors.Add(error); }
        try { ReleaseNestedElements(candidate); } catch (Exception error) { errors.Add(error); }
        leaving.Remove(candidate); owned.Remove(candidate.Root);
        try { await candidate.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
    }
    public async Task DismissOverlayAsync(ViewInstance overlay, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        token = linked.Token;
        await gate.WaitAsync(token);
        try
        {
            if (!overlays.Remove(overlay)) throw new InvalidOperationException("Overlay does not belong to this host.");
            await RetireDelivered(overlay, true);
        }
        finally { gate.Release(); }
    }
    public override void _ExitTree()
    {
        lifetime.Cancel();
        // Godot owns child-node destruction. Cancellation wakes outstanding hooks/animations.
    }
}
