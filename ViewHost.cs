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
        if (ProjectTransitions != null) return await ShowDeliveredAsync(id, overlay, model, token);
        Initialize();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        token = linked.Token;
        var entry = Entry(id, overlay ? ViewKind.Overlay : ViewKind.Page);
        var context = Context(entry, model);
        var request = new ViewPageShowContext(id, overlay, context);
        await gate.WaitAsync(token);
        var invoked = new List<IViewPageShowHook>();
        ViewInstance? candidate = null;
        bool committed = false;
        var newStates = new Dictionary<string, ViewInstance>(StringComparer.Ordinal);
        try
        {
            foreach (var hook in ShowHooks)
            { token.ThrowIfCancellationRequested(); invoked.Add(hook); await hook.BeforePrepareAsync(request, token); }
            token.ThrowIfCancellationRequested();
            candidate = Mount(entry, context, overlay ? overlayLayer : pageLayer);
            if (!overlay)
                foreach (var definition in Catalog.Entries.Where(e => e.Kind == ViewKind.State && e.State == entry.State && entry.State.Length != 0))
                    if (!states.ContainsKey(definition.Id)) newStates.Add(definition.Id, Mount(definition, context, stateLayer));
            foreach (var state in newStates.Values) await EnterAsync(state, token);
            await EnterAsync(candidate, token);
            foreach (var hook in invoked) { token.ThrowIfCancellationRequested(); await hook.AfterReadyAsync(request, token); }
            token.ThrowIfCancellationRequested();

            var result = candidate;
            if (overlay) { overlays.Add(candidate); candidate = null; committed = true; }
            else
            {
                var previous = CurrentPage;
                CurrentPage = candidate;
                candidate = null; committed = true;
                foreach (var pair in newStates) states.Add(pair.Key, pair.Value);
                newStates.Clear();
                var cleanupErrors = new List<Exception>();
                foreach (var pair in states.ToArray())
                {
                    try
                    {
                        if (pair.Value.Definition.State != entry.State)
                        { states.Remove(pair.Key); await LeaveAsync(pair.Value, lifetime.Token); }
                        else if (!ReferenceEquals(pair.Value.Context, context))
                        { pair.Value.Rebind(context); pair.Value.Lifecycle(p => p.RefreshView()); }
                    }
                    catch (Exception error) { cleanupErrors.Add(error); }
                }
                if (previous != null)
                    try { await LeaveAsync(previous, lifetime.Token); } catch (Exception error) { cleanupErrors.Add(error); }
                if (cleanupErrors.Count > 0) throw new AggregateException("Page committed, but outgoing cleanup or state refresh failed.", cleanupErrors);
            }
            return result;
        }
        catch
        {
            foreach (var hook in committed ? Enumerable.Empty<IViewPageShowHook>() : invoked.AsEnumerable().Reverse())
                try { hook.OnAborted(request); } catch (Exception error) { GD.PushError(error.ToString()); }
            throw;
        }
        finally
        {
            if (candidate != null) await candidate.DisposeAsync();
            foreach (var state in newStates.Values) await state.DisposeAsync();
            gate.Release();
        }
    }
    private ViewInstance Mount(ViewEntry entry, ViewContext context, Control layer)
    {
        var instance = CreateOwned(entry, context);
        instance.Root.Visible = false;
        layer.AddChild(instance.Root);
        return instance;
    }
    private static async Task EnterAsync(ViewInstance instance, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        instance.Lifecycle(p => p.OnBeforeShow());
        instance.Root.Show(); instance.Lifecycle(p => p.OnStartShow());
        await instance.AnimateAsync(true, token);
        token.ThrowIfCancellationRequested(); instance.Lifecycle(p => p.OnShown());
    }
    private static async Task LeaveAsync(ViewInstance instance, CancellationToken token)
    {
        try
        {
            instance.Lifecycle(p => p.OnBeforeLeave());
            instance.Lifecycle(p => p.OnStartLeave());
            await instance.AnimateAsync(false, token);
            instance.Root.Hide(); instance.Lifecycle(p => p.OnHidden());
        }
        finally { await instance.DisposeAsync(); }
    }
    public async Task DismissOverlayAsync(ViewInstance overlay, CancellationToken token = default)
    {
        if (ProjectTransitions != null)
        {
            token.ThrowIfCancellationRequested();
            await DismissDeliveredOverlayAsync(overlay.Root).WaitAsync(token);
            return;
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        token = linked.Token;
        await gate.WaitAsync(token);
        try
        {
            if (!overlays.Remove(overlay)) throw new InvalidOperationException("Overlay does not belong to this host.");
            await LeaveAsync(overlay, token);
        }
        finally { gate.Release(); }
    }
    public override void _ExitTree()
    {
        lifetime.Cancel();
        // Godot owns child-node destruction. Cancellation wakes outstanding hooks/animations.
    }
}
