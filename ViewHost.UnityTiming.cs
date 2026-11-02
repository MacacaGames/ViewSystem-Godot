using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

public enum ViewNavigationMode { Transactional,UnityTiming }
public enum ViewPageTransitionTiming { WithPrevious,AfterPrevious,Custom }
public partial class ViewHost
{
    private Task<ViewInstance> ShowAsync(string id,bool overlay,ViewModels? model,CancellationToken token,ViewNavigationOptions? options)
    {
        Initialize();
        return (options?.Mode??Catalog.NavigationMode)==ViewNavigationMode.UnityTiming
            ?ShowUnityAsync(id,overlay,model,token,options):ShowTransactionalAsync(id,overlay,model,token,options);
    }
    private async Task<ViewInstance> ShowUnityAsync(string id,bool overlay,ViewModels? model,CancellationToken token,ViewNavigationOptions? options)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,token);token=linked.Token;
        var entry=Entry(id,overlay?ViewKind.Overlay:ViewKind.Page);
        if(options?.CustomRoot is {} custom&&(!custom.IsInsideTree()||!IsAncestorOf(custom)))throw new InvalidOperationException("CustomRoot must be a mounted descendant of this host.");
        if(options?.WaitPreviousPageFinish==false&&gate.CurrentCount==0)throw new InvalidOperationException("A navigation transaction is already running.");
        await gate.WaitAsync(token);
        var invoked=new List<IViewPageShowHook>();var candidates=new List<ViewInstance>();var started=new HashSet<ViewInstance>();
        var errors=new List<Exception>();var retirements=new List<Task>();
        var nextStates=new Dictionary<string,ViewInstance>(StringComparer.Ordinal);
        ViewInstance? result=null;bool committed=false;Exception? failure=null;
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if(options!=null&&!options.IgnoreClickProtection&&Catalog.BuiltInClickProtection)
            {
                ulong now=Time.GetTicksMsec();if(lastProtectedNavigation is {} at&&now-at<Catalog.MinimumTimeInterval*1000)throw new InvalidOperationException("ViewSystem click protection rejected a repeated navigation.");
                lastProtectedNavigation=now;
            }
            if(!overlay&&options?.ReusePage==true&&!options.ReplayWhileSamePage&&CurrentPage?.Definition.Id==id)return CurrentPage;
            if(overlay&&options?.ReuseOverlay==true&&!options.ReplayWhileSamePage&&overlays.LastOrDefault(v=>v.Definition.Id==id) is {} existing)return existing;
            IsTransitioning=true;
            var context=Context(entry,model);var request=new ViewPageShowContext(id,overlay,context);
            foreach(var hook in ShowHooks.ToArray().Where(h=>h.ShouldHandle(request)).OrderBy(h=>(h as IViewPageShowHookExecutionPolicy)?.Order??0))
            {invoked.Add(hook);await RunHookAsync(hook,t=>hook.BeforePrepareAsync(request,t),token);}
            foreach(var hook in invoked)if(hook is IViewPageShowPhaseCallback phase)await RunHookAsync(hook,t=>phase.OnBeforePreparePhaseCompletedAsync(request,t),token);
            token.ThrowIfCancellationRequested();
            var focus=GetViewport().GuiGetFocusOwner();
            var scenes=await PrepareScenesAsync(entry,token);
            foreach(var definition in StateDefinitions(entry))if(StateNeedsCreation(definition.Id))
            {var state=Mount(definition,context,stateLayer,scenes:scenes);nextStates.Add(definition.Id,state);candidates.Add(state);}
            result=Mount(entry,context,options?.CustomRoot??(overlay?overlayLayer:pageLayer),options,scenes);candidates.Add(result);result.Ready=ready.Task;
            token.ThrowIfCancellationRequested();
            options?.OnStart?.Invoke();
            var previous=overlay?(options?.ReuseOverlay==true?overlays.LastOrDefault(v=>v.Definition.Id==id):null):CurrentPage;
            if(overlay){if(previous!=null)overlays.Remove(previous);overlays.Add(result);}
            else{CurrentPage=result;PageReady=result.Ready;++activationGeneration;}
            var obsolete=states.Where(p=>!StateGroupRequired(p.Value.Definition.State)||nextStates.ContainsKey(p.Key)).ToArray();
            foreach(var pair in obsolete)states.Remove(pair.Key);
            foreach(var pair in nextStates)states.Add(pair.Key,pair.Value);
            committed=true;
            // Outgoing transitions start before incoming transitions in all three timings.
            foreach(var outgoing in obsolete.Select(p=>p.Value).Concat(previous==null?Array.Empty<ViewInstance>():new[]{previous}))
                retirements.Add(RetireDelivered(outgoing,outgoing.Definition.Kind==ViewKind.Overlay));
            var outgoingReady=Task.WhenAll(retirements);
            if(!result.SkipTransitions)
            {
                if(entry.TransitionTiming==ViewPageTransitionTiming.AfterPrevious&&retirements.Count>0)
                {
                    using var wait=CancellationTokenSource.CreateLinkedTokenSource(token);
                    var maximum=ViewController.TweenDelay(result.Root,Catalog.MaximumWaitSeconds,wait.Token,result.IgnoreTimeScale??false);
                    await Task.WhenAny(outgoingReady,maximum);wait.Cancel();
                    try{await maximum;}catch(OperationCanceledException)when(wait.IsCancellationRequested){}
                }
                else if(entry.TransitionTiming==ViewPageTransitionTiming.Custom&&entry.CustomTransitionWait>0)
                    await ViewController.TweenDelay(result.Root,entry.CustomTransitionWait,token,result.IgnoreTimeScale??false);
            }
            var incoming=new List<Task>();
            foreach(var state in nextStates.Values){started.Add(state);state.Ready=EnterCandidateAsync(state,token);incoming.Add(state.Ready);}
            foreach(var state in states.Values.Where(s=>!nextStates.ContainsKey(s.Definition.Id)))
                try{state.Rebind(ContextForState(state));state.Lifecycle(p=>p.RefreshView());}catch(Exception error){errors.Add(error);}
            started.Add(result);incoming.Add(EnterCandidateAsync(result,token));
            try{CommitPresentation(result,focus);NavigationCommitted?.Invoke(result);options?.OnChanged?.Invoke(result);}catch(Exception error){errors.Add(error);}
            await Task.WhenAll(incoming);ready.TrySetResult();
            foreach(var hook in invoked)await RunHookAsync(hook,t=>hook.AfterReadyAsync(request,t),token);
        }
        catch(Exception error)
        {
            failure=error;
            if(result!=null){if(error is OperationCanceledException)ready.TrySetCanceled(token);else ready.TrySetException(error);_ =ready.Task.Exception;}
        }
        finally
        {
            try
            {
                if(failure!=null)
                    foreach(var hook in invoked.AsEnumerable().Reverse())
                        try{hook.OnAborted(new(id,overlay,result?.Context??Context(entry,model)));}catch(Exception error){errors.Add(error);}
                if(!committed)foreach(var candidate in candidates.AsEnumerable().Reverse())await AbortCandidateAsync(candidate,started.Contains(candidate),errors);
                // A caller cancellation cannot abandon required outgoing cleanup.
                foreach(var task in retirements)try{await task;}catch(Exception error){errors.Add(error);}
            }
            finally{IsTransitioning=CurrentPage?.Ready.IsCompleted==false;gate.Release();}
        }
        if(committed&&(failure!=null||errors.Count>0))throw new ViewNavigationCommittedException(id,overlay,(failure==null?errors:new[]{failure}.Concat(errors)).ToArray());
        if(failure!=null)
        {if(errors.Count>0)throw new AggregateException(new[]{failure}.Concat(errors));System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();}
        return result!;
    }
}
