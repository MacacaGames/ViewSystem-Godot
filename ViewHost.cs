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
    [Export,ViewId] public string InitialPage { get; set; } = "";
    [Export] public bool UsePreviewModels { get; set; }
    public ViewModels SharedModels { get; set; } = new();
    [Export] public bool UseSimulatedSafeArea {get;set;}
    [Export] public Rect2 SimulatedSafeArea {get;set;}=new(0,0,1,1);
    public HashSet<string> ActiveBreakpoints { get; } = new(StringComparer.Ordinal);
    public List<IViewPageShowHook> ShowHooks { get; } = new();
    public ViewInstance? CurrentPage { get; private set; }
    public IReadOnlyList<ViewInstance> Overlays => overlays;
    public IReadOnlyDictionary<string, ViewInstance> States => states;
    public event Action<string, Control>? ActionRequested;
    public event Action<Exception>? Failed;
    public event Action<ViewInstance>? NavigationCommitted;
    private ulong? lastProtectedNavigation;
    public event Action<ViewActionEvent>? ActionWithPayloadRequested;
    private readonly List<ViewInstance> overlays = new();
    private readonly Dictionary<string, ViewInstance> states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1);
    private readonly CancellationTokenSource lifetime = new();
    private Control pageLayer = null!, stateLayer = null!, overlayLayer = null!;
    private bool initialized;
    private ViewElementPool? elementPool;
    public ViewElementPool ElementPool {get {Initialize();return elementPool!;}}
    public override void _Ready()
    {
        Initialize();
        if(IsPrimaryHost&&EngineDebugger.IsActive()){var debug=(Node)GD.Load<GDScript>("res://addons/macaca_viewsystem/runtime_debugger.gd").New().AsGodotObject();AddChild(debug);debug.Call("setup",this);}
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
        elementPool=new ViewElementPool(this);elementPool.Destroying+=root=>ReleaseDescendants(root,true);
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
            WithSingletons(UsePreviewModels ? ViewModels.From(Catalog.SharedPreviewModel) : SharedModels),
            Dispatch: (action, origin) => ActionRequested?.Invoke(action, origin), ActiveBreakpoints: new HashSet<string>(ActiveBreakpoints,StringComparer.Ordinal), DispatchEvent: action=>ActionWithPayloadRequested?.Invoke(action), NormalizedSafeArea: GetNormalizedSafeArea(), GlobalSafeArea: Catalog.SafeArea);

    private ViewModels WithSingletons(ViewModels models){elementPool?.InjectSingletons(models);return models;}

    public Task<ViewInstance> ShowPageAsync(string id, ViewModels? model = null, CancellationToken token = default,ViewNavigationOptions? options=null)
        => ShowAsync(id, false, model, token,options);
    public Task<ViewInstance> ShowOverlayAsync(string id, ViewModels? model = null, CancellationToken token = default,ViewNavigationOptions? options=null)
        => ShowAsync(id, true, model, token,options);
    private async Task<ViewInstance> ShowTransactionalAsync(string id, bool overlay, ViewModels? model, CancellationToken token,ViewNavigationOptions? options)
    {
        Initialize();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token);
        token = linked.Token;
        var entry = Entry(id, overlay ? ViewKind.Overlay : ViewKind.Page);
        var context = Context(entry, model);
        var request = new ViewPageShowContext(id, overlay, context);
        Control? priorFocus=null;
        if(options?.CustomRoot is {} custom&&(!custom.IsInsideTree()||!IsAncestorOf(custom)))throw new InvalidOperationException("CustomRoot must be a mounted descendant of this host.");
        if(options?.WaitPreviousPageFinish==false&&gate.CurrentCount==0)throw new InvalidOperationException("A navigation transaction is already running.");
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
            if(options!=null&&!options.IgnoreClickProtection&&Catalog.BuiltInClickProtection)
            {
                ulong now=Time.GetTicksMsec();if(lastProtectedNavigation is {} protectedAt&&now-protectedAt<Catalog.MinimumTimeInterval*1000)throw new InvalidOperationException("ViewSystem click protection rejected a repeated navigation.");
                lastProtectedNavigation=now;
            }
            if(!overlay&&options?.ReusePage==true&&!options.ReplayWhileSamePage&&CurrentPage?.Definition.Id==id)return CurrentPage;
            if(overlay&&options?.ReuseOverlay==true&&!options.ReplayWhileSamePage&&overlays.LastOrDefault(v=>v.Definition.Id==id) is {} existing)return existing;
            IsTransitioning = true;priorFocus=GetViewport().GuiGetFocusOwner();options?.OnStart?.Invoke();
            var selectedHooks=ShowHooks.ToArray().Where(h=>h.ShouldHandle(request))
                .OrderBy(h=>(h as IViewPageShowHookExecutionPolicy)?.Order??0).ToArray();
            foreach (var hook in selectedHooks)
            { token.ThrowIfCancellationRequested(); invoked.Add(hook); await RunHookAsync(hook,t=>hook.BeforePrepareAsync(request,t),token); }
            foreach(var hook in invoked)
                if(hook is IViewPageShowPhaseCallback phase)
                    await RunHookAsync(hook,t=>phase.OnBeforePreparePhaseCompletedAsync(request,t),token);
            token.ThrowIfCancellationRequested();
            var scenes=await PrepareScenesAsync(entry,token);
            foreach (var definition in StateDefinitions(entry))
                    if (StateNeedsCreation(definition.Id))
                    { var state = Mount(definition, context, stateLayer,scenes:scenes); nextStates.Add(definition.Id, state); candidates.Add(state); }
            result = Mount(entry, context, options?.CustomRoot??(overlay ? overlayLayer : pageLayer),options,scenes); candidates.Add(result);
            foreach (var state in nextStates.Values)
            {
                started.Add(state); state.Ready = EnterCandidateAsync(state, token);
                await state.Ready;
            }
            started.Add(result); result.Ready = EnterCandidateAsync(result, token);
            await result.Ready;
            foreach (var hook in invoked)
            { token.ThrowIfCancellationRequested(); await RunHookAsync(hook,t=>hook.AfterReadyAsync(request,t),token); }
            token.ThrowIfCancellationRequested();

            // Ownership commit contains no await or application callback.
            var previous=overlay?options?.ReuseOverlay==true?overlays.LastOrDefault(v=>v.Definition.Id==id):null:CurrentPage;
            if(overlay&&previous!=null)overlays.Remove(previous);
            if(overlay)overlays.Add(result);
            else{CurrentPage=result;PageReady=result.Ready;++activationGeneration;}
            var obsolete=states.Where(p=>!StateGroupRequired(p.Value.Definition.State)||nextStates.ContainsKey(p.Key)).ToArray();
            foreach(var pair in obsolete)states.Remove(pair.Key);
            foreach(var pair in nextStates)states.Add(pair.Key,pair.Value);
            committed=true;
            try{CommitPresentation(result,priorFocus);NavigationCommitted?.Invoke(result);options?.OnChanged?.Invoke(result);}catch(Exception error){cleanupErrors.Add(error);}
            foreach(var state in states.Values.Where(v=>!nextStates.ContainsKey(v.Definition.Id)))
                try{state.Rebind(ContextForState(state));state.Lifecycle(p=>p.RefreshView());}catch(Exception error){cleanupErrors.Add(error);}
            foreach(var outgoing in obsolete.Select(p=>p.Value).Concat(previous==null?Array.Empty<ViewInstance>():new[]{previous}))
                try{await RetireDelivered(outgoing,outgoing.Definition.Kind==ViewKind.Overlay);}catch(Exception error){cleanupErrors.Add(error);}
            if(cleanupErrors.Count>0)throw new ViewNavigationCommittedException(entry.Id,overlay,cleanupErrors);
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
    private static async Task RunHookAsync(IViewPageShowHook hook,Func<CancellationToken,Task> run,CancellationToken token)
    {
        float seconds=(hook as IViewPageShowHookExecutionPolicy)?.TimeoutSeconds??0;
        if(!float.IsFinite(seconds))throw new InvalidOperationException("Hook timeout must be finite.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        if(seconds>0)timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { await run(timeout.Token).WaitAsync(timeout.Token); }
        catch(OperationCanceledException error) when(!token.IsCancellationRequested&&timeout.IsCancellationRequested)
        {throw new TimeoutException("ViewSystem hook timed out: "+hook.GetType().Name,error);}
    }
    private IEnumerable<ViewEntry> StateDefinitions(ViewEntry page)
        => Catalog.Entries.Where(e => e.Kind == ViewKind.State && page.State.Length != 0 && e.State == page.State);
    private ViewInstance Mount(ViewEntry entry, ViewContext context, Control layer,ViewNavigationOptions? options=null,IReadOnlyDictionary<string,PackedScene>? scenes=null)
    {
        var instance = CreateOwned(entry, context,scenes);
        try
        {
            instance.SkipTransitions=options?.IgnoreTransition??false;instance.IgnoreTimeScale=options?.IgnoreTimeScale;
            if(options?.SortOrder is {} order)instance.Order=order;
            if(!instance.SkipTransitions)instance.ValidateAnimations(); instance.Root.Visible = false; layer.AddChild(instance.Root); instance.MountLayout();
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
        instance.HasEntered=true;instance.Lifecycle(p=>p.OnChangePage(true)); instance.Lifecycle(p => p.OnBeforeShow()); instance.Root.Show(); instance.Lifecycle(p => p.OnStartShow());
    }
    private async Task EnterCandidateAsync(ViewInstance instance, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); BeginEntry(instance);
        await instance.AnimateAsync(true, token);
        double duration = instance.SkipTransitions?0:instance.Definition.Kind == ViewKind.Overlay ? ProjectTransitions?.OverlayEnterDuration(instance.Root) ?? 0 :
            instance.Definition.Kind == ViewKind.Page ? ProjectTransitions?.EnterDuration(instance.Root) ?? 0 : 0;
        ValidateDuration(duration);
        if (duration > 0) await ViewController.TweenDelay(instance.Root, duration, token,instance.IgnoreTimeScale??false);
        token.ThrowIfCancellationRequested(); instance.Lifecycle(p => {p.OnShown();p.OnChangedPage();});
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
            await RetireDelivered(overlay, true); await ReconcileStatesAsync(); RestoreFocus(overlay);
        }
        finally { gate.Release(); }
    }
    public override void _ExitTree()
    {
        var errors=new List<Exception>();
        try{lifetime.Cancel();}catch(Exception error){errors.Add(error);}
        try{elementPool?.CancelLifetimes();}catch(Exception error){errors.Add(error);}
        finally{ReleaseFrameRate();}
        if(errors.Count>0)throw new AggregateException("ViewHost exit reported cleanup errors.",errors);
        // Godot owns child-node destruction. Cancellation wakes outstanding hooks/animations.
    }
}
