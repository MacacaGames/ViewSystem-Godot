# Navigation contract (0.7.0-dev)

The default `ViewCatalog.NavigationMode` is **UnityTiming**, as selected for the
Unity migration. After preparation succeeds, commit ownership before entry and
start outgoing retirement. WithPrevious overlaps entry; AfterPrevious waits for
retirement up to MaximumWaitSeconds; Custom uses the authored delay. Cancellation
or failure after commit does not restore the outgoing page. Cleanup still runs,
and `ViewNavigationCommittedException` identifies the committed view.

The numbered transaction below applies when **Transactional** is explicitly
selected, with either native transitions or `ProjectTransitions`. Calls on one
host are serialized. Direct async calls queue. Explicit options / the fluent
builder can reject busy requests or enforce click protection; requests rejected
by protection do not invoke their callbacks. A hook must not recursively await
the same host's navigation.


1. Await `BeforePrepareAsync`, assemble and inject the candidate and all new
   State instances, mount them, run entry lifecycle/animation/policy timing, and
   await `AfterReadyAsync`.
2. Check cancellation, then commit synchronously. Only here does `CurrentPage`
   or the active overlay list change. Old State membership and the old page
   remain authoritative until this point. Candidates can be visible during entry.
3. Refresh retained States and retire obsolete States/the old page. Caller
   cancellation after commit cannot undo it or interrupt required cleanup.

An exception or cancellation before commit releases every candidate and its
owned nested elements, disconnects signals, and runs invoked hooks' `OnAborted`
in reverse order. The old page and State membership stay intact. Entered
candidates receive best-effort leave/hidden lifecycle during rollback. Cleanup
errors are reported together with the original failure; the navigation gate is
always released. Receivers/hooks must manage their own external side effects.

Retained State refresh occurs after commit because arbitrary application
receiver side effects cannot be transactionally cloned. An outgoing cleanup or
retained State refresh failure is a `ViewNavigationCommittedException` with the
committed view ID; the new page remains current. Cleanup attempts all outgoing
instances before reporting. Later navigation remains available.

`ActivatePage` / `ActivateOverlay` are deliberately immediate APIs. They do not
accept asynchronous show hooks and cannot interrupt an active async transaction.
Scene assembly, injection, entry-start lifecycle, and duration validation happen
before their synchronous commit. New States are prepared before changing the
page or removing old States. Failure during this preparation rolls back.

After an immediate commit, the returned `Ready` task reports entry completion
or failure; cancellation/failure does not restore the previous page. This API
preserves the game's overlapping entry and outgoing retirement. `PageReady`
and `IsTransitioning` refer to the current page's entry, not outgoing cleanup.
Use `DrainTransitionsAsync` to await observed operations and `ShutdownAsync`
to cancel pending navigation and release owned instances.

Call these APIs on the Godot main thread and await them. A hook must not await
navigation or shutdown on its own host while it holds that host's transaction.

Healthy retained States keep their identity across matching groups. A State whose
immediate entry Ready faulted or was cancelled is staged as a replacement on the
next page navigation, so a failed entry does not poison future requests.


Options may select navigation mode per request, ignore transitions/time scale,
replay an already active ID, reuse an overlay, supply a mounted descendant root,
override sorting, and observe OnStart / OnChanged / OnComplete. OnChanged occurs
at commit; OnComplete only after entry and AfterReady succeed. Raw async calls
without options preserve direct Task API queue/rebuild semantics. Fluent defaults
reuse the same ID, enforce click protection and use unscaled transition time.

Overlay State references include all live overlays plus the Page. Closing a
middle overlay releases only its reference. Retained States receive the top
active owner's context. Modal overlays suspend underlying focus and restore the
previous valid focus owner when removed. Sibling order follows rendered order
because ZIndex alone does not order native input.
