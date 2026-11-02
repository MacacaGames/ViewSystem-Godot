using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

/// <summary>The one scene assembly/binding path used by the editor and both runtime hosts.</summary>
public static class ViewComposer
{
    public static ViewInstance Create(ViewEntry entry, ViewContext context, ViewElementPool? pool = null,IReadOnlyDictionary<string,PackedScene>? scenes=null)
    {
        var root = InstantiateControl(ResolveScene(entry.Scene,entry.ScenePath,scenes), entry.Id);
        var instance = new ViewInstance(entry, root, context, pool);
        try
        {
            if(entry.SortOrder<RenderingServer.CanvasItemZMin||entry.SortOrder>RenderingServer.CanvasItemZMax)throw new InvalidOperationException("SortOrder exceeds Godot ZIndex range.");
            root.ZIndex = entry.SortOrder;
            pool?.AttachNested(instance,root,ResolveScene(entry.Scene,entry.ScenePath,scenes));
            AssembleElements(instance,entry,root,context,pool,scenes);
            instance.Rebind(context);
            instance.ConnectEvents();
            instance.ApplyFocusLinks();
            return instance;
        }
        catch { instance.Dispose(); throw; }
    }

    internal static void AssembleElements(ViewInstance instance,ViewEntry entry,Control root,ViewContext context,ViewElementPool? pool,IReadOnlyDictionary<string,PackedScene>? scenes=null)
    {
            foreach (var placement in entry.Elements)
            {
                if(placement?.ExcludeFeatures.Any(OS.HasFeature)==true)continue;
                if (placement == null || string.IsNullOrWhiteSpace(placement.Id) || placement.Scene == null&&string.IsNullOrWhiteSpace(placement.ScenePath))
                    throw new InvalidOperationException($"{entry.Id}: every element needs an ID and scene.");
                if (instance.Elements.ContainsKey(placement.Id))
                    throw new InvalidOperationException($"{entry.Id}: duplicate element '{placement.Id}'.");
                var parent = root.GetNodeOrNull(placement.ResolveParent(context))
                    ?? throw new InvalidOperationException($"{entry.Id}/{placement.Id}: parent '{placement.ParentPath}' is missing.");
                if (placement.NodeName.Length != 0)
                {
                    if (parent.HasNode(new NodePath(placement.NodeName)))
                        throw new InvalidOperationException($"{entry.Id}: duplicate authored node name '{placement.NodeName}'.");
                }
                var scene=ResolveScene(placement.Scene,placement.ScenePath,scenes);
                var element = pool==null?InstantiateControl(scene, entry.Id + "/" + placement.Id):pool.Acquire(instance,placement,parent,scene);
                if (placement.NodeName.Length != 0) element.Name = placement.NodeName;
                if(element.GetParent()==null)parent.AddChild(element);
                if(placement.ManualOnly)element.Hide();
                if (placement.SiblingIndex >= 0) parent.MoveChild(element, Math.Min(placement.SiblingIndex,parent.GetChildCount()-1));
                instance.Elements.Add(placement.Id, element);
                instance.BindLayout(placement,element);
                pool?.AttachNested(instance,element,scene);
            }
    }

    private static PackedScene ResolveScene(PackedScene? direct,string path,IReadOnlyDictionary<string,PackedScene>? scenes)
    {
        if(direct!=null)return direct;
        if(scenes?.TryGetValue(path,out var cached)==true)return cached;
        if(string.IsNullOrWhiteSpace(path)||!ResourceLoader.Exists(path,"PackedScene"))throw new InvalidOperationException("Missing scene: "+path);
        return ResourceLoader.Load<PackedScene>(path)??throw new InvalidOperationException("Not a PackedScene: "+path);
    }
    private static Control InstantiateControl(PackedScene scene, string id)
    {
        var node = scene.Instantiate();
        if (node is Control control) return control;
        node.Free(); throw new InvalidOperationException($"{id}: scene root must be a Control.");
    }

    internal static IEnumerable<Node> Walk(Node root)
    {
        yield return root;
        foreach (var child in root.GetChildren()) foreach (var nested in Walk(child)) yield return nested;
    }
}

public sealed partial class ViewInstance : IDisposable, IAsyncDisposable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, ViewInstance> owners = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken lifetimeToken;
    private CancellationTokenSource entryLifetime = new();
    public CancellationToken LifetimeToken => lifetimeToken;
    internal CancellationToken EntryToken => entryLifetime.Token;
    internal void CancelEntry() => entryLifetime.Cancel();
    public Task Ready { get; internal set; } = Task.CompletedTask;
    public ViewEntry Definition { get; }
    public Control Root { get; private set; }
    private ViewElementPlacement? rootLease;
    private readonly List<ViewElementPlacement> nestedPlacements=new();
    internal IEnumerable<ViewElementPlacement> Placements=>Definition.Elements.Concat(nestedPlacements);
    internal bool HasUniqueUnits=>pool!=null&&(pool.IsUnique(Root)||Elements.Values.Any(pool.IsUnique));
    internal void RestoreRecoveredElements(){foreach(var node in Elements.Values)if(Owns(node))RestoreElement(node);RestoreElement(Root);}
    internal bool ForceKeepRecovery,ForceDestroyRecovery,Dormant,HasEntered;
    internal bool IsPooledRoot=>rootLease!=null;
    public bool IsCurrentOwner=>!disposed&&(rootLease==null||Owns(Root));
    public bool SkipTransitions {get;internal set;}
    public bool? IgnoreTimeScale {get;internal set;}
    public int Order {get=>Root.ZIndex;set{if(value<RenderingServer.CanvasItemZMin||value>RenderingServer.CanvasItemZMax)throw new InvalidOperationException("Sort order exceeds Godot ZIndex range.");Root.ZIndex=value;}}
    private readonly ViewElementPool? pool;
    public bool IsDisposed=>disposed;
    internal static void AssignElementOwner(Control element,ViewInstance? owner){owners.Remove(element);if(owner!=null)owners.Add(element,owner);}
    private bool Owns(Control element)=>pool==null||pool.IsOwner(element,this);
    internal void RegisterSingleton(Node singleton)=>pool?.RegisterSingleton(singleton,Context.Shared);
    public Dictionary<string, Control> Elements { get; } = new(StringComparer.Ordinal);
    public ViewContext Context { get; private set; } = ViewContext.Empty;
    private readonly List<ViewPropertyOverride> runtimeOverrides=new();
    private readonly Dictionary<Guid,ViewPropertyOverride> runtimeOverrideHandles=new();
    private readonly Dictionary<(Node, string), Variant> originals = new();
    private readonly Dictionary<string,Node> knownTargets=new(StringComparer.Ordinal);
    private readonly List<(Node Node, StringName Signal, Callable Callback)> connections = new();
    private readonly List<(Control Node,Callable Callback)> layoutConnections=new();
    private readonly ViewElementLayout rootSourceLayout;
    private bool safeAreaTouched;
    private Control? mountParent;
    private Callable mountLayoutCallback;
    private readonly Dictionary<Control,(Color Modulate,Color Self)> sourceVisuals=new();
    private readonly Dictionary<string,ViewElementLayout> sourceLayouts=new(StringComparer.Ordinal);
    internal void BindLayout(ViewElementPlacement placement,Control element)
    {
        RenewElementOwnership(element);
        foreach(var node in ViewComposer.Walk(element))knownTargets[Root.GetPathTo(node).ToString()]=node;
        sourceLayouts[placement.Id]=ViewElementLayout.Capture(element);sourceVisuals[element]=(element.Modulate,element.SelfModulate);
        var callback=Callable.From(()=>{if(Owns(element))placement.ResolveLayout(Context)?.ApplyPivot(element);});
        element.Connect(Control.SignalName.Resized,callback);layoutConnections.Add((element,callback));
    }
    private readonly Dictionary<(object Owner,System.Reflection.MemberInfo Member),object?> attributeOriginals=new();
    private bool disposed;
    internal ViewInstance(ViewEntry definition, Control root,ViewContext context,ViewElementPool? pool) { Definition = definition; Root = root;Context=context;this.pool=pool;lifetimeToken=lifetime.Token;rootSourceLayout=ViewElementLayout.Capture(root);rootSourceLayout.Flags=ViewLayoutFlags.AnchorMin|ViewLayoutFlags.AnchorMax|ViewLayoutFlags.SizeDelta|ViewLayoutFlags.AnchoredPosition; owners.Add(root, this); }
    internal bool CanResumeUnder(Node node)=>OwnsNode(node);
    private bool OwnsNode(Node node)
    {for(Node? cursor=node;cursor!=null;cursor=cursor.GetParent()){if(cursor is Control c&&owners.TryGetValue(c,out var owner)&&owner!=this)return false;if(cursor==Root)return true;}return false;}
    private IEnumerable<Node> OwnedWalk(Node node)
    {
        if (node != Root && node is Control control && owners.TryGetValue(control, out var owner) && !ReferenceEquals(owner, this)) yield break;
        yield return node;
        foreach (var child in node.GetChildren()) foreach (var nested in OwnedWalk(child)) yield return nested;
    }

    internal static ViewInstance CreatePooledElement(ViewEntry entry,ViewContext context,ViewElementPool pool,PackedScene scene,bool bind=true)
    {
        var placeholder=new Control();var instance=new ViewInstance(entry,placeholder,context,pool);
        var placement=new ViewElementPlacement {Id="__root",Scene=scene,PoolPolicy=entry.PoolPolicy};
        try
        {
            var element=pool.Acquire(instance,placement,placeholder,scene);
            owners.Remove(placeholder);element.GetParent()?.RemoveChild(element);placeholder.Free();
            instance.Root=element;instance.rootLease=placement;AssignElementOwner(element,instance);instance.BindLayout(placement,element);
            pool.AttachNested(instance,element,scene);
            ViewComposer.AssembleElements(instance,entry,element,context,pool);
            if(bind){instance.Rebind(context);instance.ConnectEvents();instance.ApplyFocusLinks();}instance.ValidateAnimations();return instance;
        }
        catch{instance.Dispose();throw;}
    }
    internal void ResetEntry(){entryLifetime.Dispose();entryLifetime=new();Ready=Task.CompletedTask;Dormant=false;HasEntered=false;}
    public void MountLayout()
    {
        if(rootLease!=null)pool!.UpdateParent(this,Root,Root.GetParent()!);
        if(mountParent==Root.GetParentControl())return;
        if(GodotObject.IsInstanceValid(mountParent)&&mountParent!.IsConnected(Control.SignalName.Resized,mountLayoutCallback))mountParent.Disconnect(Control.SignalName.Resized,mountLayoutCallback);
        mountParent=Root.GetParentControl();
        if(mountParent==null)return;
        mountLayoutCallback=Callable.From(ApplySafeArea);
        mountParent.Connect(Control.SignalName.Resized,mountLayoutCallback);ApplySafeArea();
    }
    internal void UpdateSafeArea(Rect2 safe){Context=Context with {NormalizedSafeArea=safe};ApplySafeArea();}
    private void ApplySafeArea()
    {
        var settings=Definition.UseGlobalSafeArea?Context.GlobalSafeArea:Definition.SafeArea;
        if(mountParent==null||Definition.Kind==ViewKind.Element)return;
        if(settings?.Enabled!=true){if(safeAreaTouched)rootSourceLayout.Apply(Root);return;}
        safeAreaTouched=true;
        if(mountParent is Container)throw new InvalidOperationException("Safe area root cannot be managed by a Container; use an intermediate Control.");
        var inset=settings.Insets(Context.NormalizedSafeArea??new Rect2(0,0,1,1));var size=mountParent.Size;
        Root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Root.OffsetLeft=inset.X*size.X;Root.OffsetTop=inset.Y*size.Y;Root.OffsetRight=-inset.Z*size.X;Root.OffsetBottom=-inset.W*size.Y;
    }
    private readonly Dictionary<Control,(Control.FocusModeEnum Mode,NodePath Left,NodePath Top,NodePath Right,NodePath Bottom,NodePath Next,NodePath Previous)> focusOriginals=new();
    internal void ApplyFocusLinks()=>ApplyFocusLinks(Definition.FocusLinks);
    internal void ResetFocusLinks()
    {
        foreach(var pair in focusOriginals)if(GodotObject.IsInstanceValid(pair.Key))
        {var node=pair.Key;var value=pair.Value;node.FocusMode=value.Mode;node.FocusNeighborLeft=value.Left;node.FocusNeighborTop=value.Top;node.FocusNeighborRight=value.Right;node.FocusNeighborBottom=value.Bottom;node.FocusNext=value.Next;node.FocusPrevious=value.Previous;}
        ApplyFocusLinks();
    }
    internal void ApplyFocusLinks(IEnumerable<ViewFocusLink> links)
    {
        foreach(var link in links)
        {
            var target=Root.GetNodeOrNull<Control>(link.Target)??throw new InvalidOperationException("Focus target missing: "+link.Target);
            focusOriginals.TryAdd(target,(target.FocusMode,target.FocusNeighborLeft,target.FocusNeighborTop,target.FocusNeighborRight,target.FocusNeighborBottom,target.FocusNext,target.FocusPrevious));
            if(!Enum.IsDefined(link.Mode)||!Enum.IsDefined(link.Direction))throw new InvalidOperationException("Invalid focus mode.");
            target.FocusMode=link.Direction==ViewFocusDirection.None?Control.FocusModeEnum.Click:link.Mode;
            NodePath Path(NodePath path)=>path.IsEmpty?new NodePath(""):target.GetPathTo(Root.GetNodeOrNull<Control>(path)??throw new InvalidOperationException("Focus neighbour missing: "+path));
            target.FocusNeighborLeft=Path(link.Left);target.FocusNeighborTop=Path(link.Top);target.FocusNeighborRight=Path(link.Right);target.FocusNeighborBottom=Path(link.Bottom);target.FocusNext=Path(link.Next);target.FocusPrevious=Path(link.Previous);
            if(link.Direction is ViewFocusDirection.None or ViewFocusDirection.Vertical){target.FocusNeighborLeft=".";target.FocusNeighborRight=".";}
            if(link.Direction is ViewFocusDirection.None or ViewFocusDirection.Horizontal){target.FocusNeighborTop=".";target.FocusNeighborBottom=".";}
            if(link.Direction==ViewFocusDirection.Explicit){if(link.Left.IsEmpty)target.FocusNeighborLeft=".";if(link.Right.IsEmpty)target.FocusNeighborRight=".";if(link.Top.IsEmpty)target.FocusNeighborTop=".";if(link.Bottom.IsEmpty)target.FocusNeighborBottom=".";}
        }
    }
    public void Focus()
    {
        if(Context.IsPreview||!Definition.NavigationEnabled)return;FocusControl(Definition.FirstSelected);
    }
    internal void FocusControl(NodePath path)
    {
        var target=path.IsEmpty?OwnedWalk(Root).OfType<Control>().FirstOrDefault(n=>n.FocusMode==Control.FocusModeEnum.All&&n.IsVisibleInTree()):Root.GetNodeOrNull<Control>(path);
        if(target==null||target.FocusMode==Control.FocusModeEnum.None)throw new InvalidOperationException("No focusable FirstSelected in "+Definition.Id);
        target.GrabFocus();
    }
    public void Rebind(ViewContext context)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ViewInstance));
        if ((Definition.Events.Any(e=>e.Enabled&&e.Handler.Length==0)||Placements.Any(p=>Elements.ContainsKey(p.Id)&&p.Events.Any(e=>e.Enabled&&e.Handler.Length==0))) && context.Dispatch == null && context.DispatchEvent == null) throw new InvalidOperationException("A bound view requires an action dispatcher when rebinding.");
        Context = context;
        if(rootLease!=null&&!Owns(Root))return;
        foreach (var item in originals)if(OwnsNode(item.Key.Item1))item.Key.Item1.Set(item.Key.Item2, item.Value);
        foreach(var item in attributeOriginals.Where(p=>p.Key.Owner is not Node node||OwnsNode(node)))ViewAttributeBindings.Write(item.Key.Member,item.Key.Owner,item.Value);
        ApplySafeArea();
        foreach (var placement in Placements.Where(p=>Elements.ContainsKey(p.Id)))
        {
            var element=Elements[placement.Id];if(!Owns(element))continue;
            var parent=Root.GetNodeOrNull(placement.ResolveParent(context));
            if(parent==null&&knownTargets.TryGetValue(placement.ResolveParent(context).ToString(),out var lentParent)&&GodotObject.IsInstanceValid(lentParent)&&!OwnsNode(lentParent))continue;
            if(parent==null)throw new InvalidOperationException($"{Definition.Id}/{placement.Id}: missing breakpoint parent.");
            if(parent==element||element.IsAncestorOf(parent))throw new InvalidOperationException("A breakpoint parent cannot be inside its own element.");
            if(element.GetParent()!=parent){element.GetParent()?.RemoveChild(element);parent.AddChild(element);RenewElementOwnership(element);}
            if(placement.SiblingIndex>=0)parent.MoveChild(element,Math.Min(placement.SiblingIndex,parent.GetChildCount()-1));
            var layout=placement.ResolveLayout(context);
            if(element.GetParent() is not Container)sourceLayouts[placement.Id].Apply(element);
            layout?.Apply(element);
        }
        foreach (var node in OwnedWalk(Root))
            if (node is IViewModelReceiver receiver && (!context.IsPreview || node is IViewPreviewParticipant))
                receiver.Inject(context);
        ApplyAttributes();
        Apply(Root, Definition.Overrides);
        foreach (var placement in Placements.Where(p=>Elements.ContainsKey(p.Id)))if(Owns(Elements[placement.Id])&&OwnsNode(Elements[placement.Id]))Apply(Elements[placement.Id], placement.Overrides);
        Apply(Root,runtimeOverrides);
        if(!Context.IsPreview)foreach(var listener in OwnedWalk(Root).OfType<IViewElementOverrideListener>())listener.OnOverridesApplied(this);
    }
    private void Apply(Control root, IEnumerable<ViewPropertyOverride> rules)
    {
        // Literal page settings first; model bindings take precedence independent of array order.
        foreach (var rule in rules.OrderBy(rule => rule?.IsModelBinding==true ? 1 : 0))
        {
            if (rule == null) throw new InvalidOperationException($"{Definition.Id}: empty override.");
            var node = root.GetNodeOrNull(rule.Target);
            if(node==null&&knownTargets.TryGetValue((Root.GetPathTo(root).ToString().TrimEnd('/')+"/"+rule.Target).Replace("./", ""),out var lent)&&GodotObject.IsInstanceValid(lent)&&!OwnsNode(lent))continue;
            if(node==null)throw new InvalidOperationException($"{Definition.Id}: override target '{rule.Target}' is missing.");
            if(!OwnsNode(node))continue;
            if (string.IsNullOrWhiteSpace(rule.Property)) throw new InvalidOperationException("Override property is empty.");
            var path = rule.Property.Split(':');
            string first = path[0];
            var property = node.GetPropertyList().FirstOrDefault(item => item["name"].AsString() == first);
            if (property == null) throw new InvalidOperationException($"{Definition.Id}: '{rule.Target}' has no property '{first}'.");
            originals.TryAdd((node, first), node.Get(first));
            // Copy every resource along the edited path, including externally shared children.
            // Snapshot the top-level value so repeated Rebind never mutates a source resource.
            GodotObject owner = node;
            for (int i = 0; i < path.Length - 1; i++)
            {
                var parentValue = owner.Get(path[i]);
                if (parentValue.VariantType != Variant.Type.Object || parentValue.AsGodotObject() is not Resource resource)
                    throw new InvalidOperationException($"{Definition.Id}: '{path[i]}' must be a Resource for a nested override; override Vector2/Color as a whole value.");
                var copy = resource.Duplicate(); owner.Set(path[i], copy); owner = copy;
                property = owner.GetPropertyList().FirstOrDefault(item => item["name"].AsString() == path[i + 1]);
                if (property == null) throw new InvalidOperationException($"{Definition.Id}: '{rule.Property}' contains an unknown property '{path[i + 1]}'.");
            }
            var value = rule.Resolve(Context);
            var expected = (Variant.Type)property["type"].AsInt32();
            if (expected != Variant.Type.Nil && value.VariantType != expected &&
                !(expected == Variant.Type.Float && value.VariantType == Variant.Type.Int) &&
                !(expected == Variant.Type.Object && value.VariantType == Variant.Type.Nil))
                throw new InvalidOperationException($"{Definition.Id}: {rule.Target}:{rule.Property} expects {expected}, got {value.VariantType}.");
            if (expected == Variant.Type.Object && value.VariantType == Variant.Type.Object && value.AsGodotObject() is GodotObject assigned)
            {
                string className = property["class_name"].AsString();
                if (className.Length > 0 && ClassDB.ClassExists(className) && !assigned.IsClass(className))
                    throw new InvalidOperationException($"{Definition.Id}: {rule.Property} requires {className}.");
            }
            // A material supplied by a placement must remain instance-local.
            if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is Material material)
                value = Variant.From(material.Duplicate());
            owner.Set(path[^1], value);
        }
    }
    private bool eventsConnected;
    internal void ConnectEvents()
    {
        if(eventsConnected)return;eventsConnected=true;
        ConnectAttributeEvents();
        Connect(Root, Definition.Events);
        foreach (var placement in Placements.Where(p=>Elements.ContainsKey(p.Id))) Connect(Elements[placement.Id], placement.Events);
    }
    private void Connect(Control element, IEnumerable<ViewEventBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            if(binding?.Enabled==false)continue;
            if (binding == null || string.IsNullOrWhiteSpace(binding.Action)) throw new InvalidOperationException("Event requires an action ID.");
            if (binding.Handler.Length==0 && Context.Dispatch == null && Context.DispatchEvent == null) throw new InvalidOperationException($"{Definition.Id}: no action dispatcher for '{binding.Action}'.");
            var target = element.GetNodeOrNull(binding.Target) ?? throw new InvalidOperationException($"Missing event target '{binding.Target}'.");
            var signal = target.GetSignalList().FirstOrDefault(item => item["name"].AsString() == binding.Signal);
            if(signal==null)throw new InvalidOperationException($"{Definition.Id}: signal '{binding.Signal}' is missing.");
            if(binding.Handler.Length>0&&!Context.IsPreview){if(binding.NativeHandler)ValidateNativeHandler(binding,element);else ValidateHandler(binding.Handler,binding.HandlerRelativeToElement?element:Root);}
            var callback=ViewSignalAdapter.Create(signal["args"].AsGodotArray().Count,args=>
            {
                if(Context.IsPreview||disposed||Dormant||!IsCurrentOwner||element!=Root&&(!Owns(element)||!OwnsNode(element)))return;
                if(binding.Handler.Length>0){InvokeHandler(binding,element,args);return;}
                Context.Dispatch?.Invoke(binding.Action,element);
                Context.DispatchEvent?.Invoke(new(binding.Action,element,binding.ForwardArguments?args:Array.Empty<Variant>()));
            });
            target.Connect(binding.Signal, callback);
            connections.Add((target, binding.Signal, callback));
        }
    }
    private Control AttributeRoot(Node source)
    {for(Node? cursor=source;cursor!=null;cursor=cursor.GetParent())if(cursor is Control control&&Elements.Values.Contains(control))return control;return Root;}
    private IEnumerable<Node> AttributeNodes() => OwnedWalk(Root).Where(n=>!Context.IsPreview||n is IViewPreviewParticipant);
    private void ApplyAttributes(Node? subtree=null)
    {
        foreach(var source in AttributeNodes().Where(n=>subtree==null||n==subtree||subtree.IsAncestorOf(n)))
        {
            var schema=ViewAttributeBindings.For(source);if(schema==null)continue;
            foreach(var binding in schema.Members)
                if(binding.Inject is {} inject)ViewAttributeBindings.Write(binding.Member,source,Context.ModelForMember(ViewAttributeBindings.ValueType(binding.Member),inject.Key,binding.Member.Name,inject.Scope));
            foreach(var binding in schema.Members)
            {
                foreach(var attribute in binding.Overrides)
                {
                    var target=AttributeRoot(source).GetNodeOrNull(new NodePath(attribute.Path))??throw new InvalidOperationException("Attribute override target missing: "+attribute.Path);
                    if(!attribute.Component.IsInstanceOfType(target))throw new InvalidOperationException("Attribute override target has the wrong type: "+attribute.Path);
                    var member=ViewAttributeBindings.TargetMember(attribute);
                    if(!ViewAttributeBindings.ValueType(member).IsAssignableFrom(ViewAttributeBindings.ValueType(binding.Member)))throw new InvalidOperationException("Attribute override types must match: "+attribute.Property);
                    attributeOriginals.TryAdd((target,member),ViewAttributeBindings.Read(member,target));
                    var value=ViewAttributeBindings.Read(binding.Member,source);if(value is Material material)value=material.Duplicate();
                    ViewAttributeBindings.Write(member,target,value);
                }
            }
        }
    }
    private void ConnectAttributeEvents()
    {
        foreach(var source in AttributeNodes())
        {
            var schema=ViewAttributeBindings.For(source);if(schema==null)continue;
            foreach(var binding in schema.Events)
            {
                var target=AttributeRoot(source).GetNodeOrNull<Button>(new NodePath(binding.Attribute.Path))??throw new InvalidOperationException("Attribute button target missing: "+binding.Attribute.Path);
                var origin=AttributeRoot(source);
                var callback=Callable.From(()=>{if(!disposed&&!Dormant&&IsCurrentOwner&&(origin==Root||Owns(origin)))binding.Method.Invoke(source,binding.Method.GetParameters().Length==0?null:new object[]{origin});});
                target.Connect(BaseButton.SignalName.Pressed,callback);connections.Add((target,BaseButton.SignalName.Pressed,callback));
            }
        }
    }
    private (Node Node,System.Reflection.MethodInfo Method) ValidateHandler(string handler,Node? scope=null)
    {
        int split=handler.LastIndexOf("::",StringComparison.Ordinal);if(split<1)throw new InvalidOperationException("Handler must be page-relative NodePath::Method.");
        var node=(scope??Root).GetNodeOrNull(new NodePath(handler[..split]))??throw new InvalidOperationException("Handler node missing: "+handler[..split]);
        var methods=ViewAttributeBindings.For(node)?.Actions.Where(a=>a.Method.Name==handler[(split+2)..]).ToArray();
        if(methods==null||methods.Length!=1)throw new InvalidOperationException("Register exactly one [ViewSystemEvent] handler: "+handler);
        return(node,methods[0].Method);
    }
    private (Node Node,StringName Method) ValidateNativeHandler(ViewEventBinding binding,Control origin)
    {
        int split=binding.Handler.LastIndexOf("::",StringComparison.Ordinal);if(split<1)throw new InvalidOperationException("Native handler needs NodePath::Method.");
        var path=new NodePath(binding.Handler[..split]);if(path.IsAbsolute())throw new InvalidOperationException("Native handler must be relative.");
        var scope=binding.HandlerRelativeToElement?origin:Root;var node=scope.GetNodeOrNull(path)??throw new InvalidOperationException("Native handler target missing.");
        if(node!=scope&&!scope.IsAncestorOf(node))throw new InvalidOperationException("Native handler is outside its view scope.");
        var method=new StringName(binding.Handler[(split+2)..]);if(!node.HasMethod(method))throw new InvalidOperationException("Native handler method missing: "+method);return(node,method);
    }
    private void InvokeHandler(ViewEventBinding binding,Control origin,Variant[] args)
    {
        if(binding.NativeHandler){var native=ValidateNativeHandler(binding,origin);native.Node.Call(native.Method,binding.ForwardArguments?args:Array.Empty<Variant>());return;}
        var handler=ValidateHandler(binding.Handler,binding.HandlerRelativeToElement?origin:Root);var parameters=handler.Method.GetParameters();
        object? argument=parameters.Length==0?null:parameters[0].ParameterType==typeof(ViewActionEvent)?new ViewActionEvent(binding.Action,origin,binding.ForwardArguments?args:Array.Empty<Variant>()):origin;
        handler.Method.Invoke(handler.Node,parameters.Length==0?null:new[]{argument});
    }
    public void Lifecycle(Action<IViewElementLifeCycle> callback)
    {
        if (Context.IsPreview||!IsCurrentOwner) return;
        foreach (var node in LifecycleNodes()) if (node is IViewElementLifeCycle participant) callback(participant);
    }
    internal void CleanupLifecycle(Action<IViewElementLifeCycle> callback)
    {
        if (Context.IsPreview || !GodotObject.IsInstanceValid(Root)||!IsCurrentOwner) return;
        var errors = new List<Exception>();
        foreach (var node in LifecycleNodes().ToArray())
            if (node is IViewElementLifeCycle participant)
                try { callback(participant); } catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException("View lifecycle cleanup failed.", errors);
    }
    private IEnumerable<Node> LifecycleNodes()=>OwnedWalk(Root).Where(n=>!UnderManualGroup(n)&&!Placements.Any(p=>p.ManualOnly&&Elements.TryGetValue(p.Id,out var e)&&(n==e||e.IsAncestorOf(n))));
    private bool UnderManualGroup(Node node)
    {for(Node? cursor=node;cursor!=null&&cursor!=Root;cursor=cursor.GetParent())if(cursor is ViewElementGroup {OnlyManualMode:true})return true;return false;}
    public void ApplyOverrides(ViewOverrideSet set)=>ApplyOverrides(set.Values);
    public void ApplyOverrides(IEnumerable<ViewPropertyOverride> rules)=>AddRuntimeOverrides(rules);
    public Guid AddRuntimeOverride(ViewPropertyOverride rule)=>AddRuntimeOverrides(new[]{rule})[0];
    public IReadOnlyList<Guid> AddRuntimeOverrides(IEnumerable<ViewPropertyOverride> rules)
    {
        if(disposed)throw new ObjectDisposedException(nameof(ViewInstance));
        var next=rules.Select(r=>(ViewPropertyOverride)r.Duplicate(true)).ToArray();int previous=runtimeOverrides.Count;runtimeOverrides.AddRange(next);
        try{Rebind(Context);}catch(Exception failure){runtimeOverrides.RemoveRange(previous,next.Length);try{Rebind(Context);}catch(Exception cleanup){throw new AggregateException("Runtime override and rollback failed.",failure,cleanup);}throw;}
        var handles=next.Select(_=>Guid.NewGuid()).ToArray();for(int i=0;i<next.Length;i++)runtimeOverrideHandles.Add(handles[i],next[i]);return handles;
    }
    public bool RemoveRuntimeOverride(Guid handle)
    {
        if(disposed)throw new ObjectDisposedException(nameof(ViewInstance));
        if(!runtimeOverrideHandles.TryGetValue(handle,out var rule))return false;
        int index=runtimeOverrides.IndexOf(rule);runtimeOverrides.RemoveAt(index);
        try{Rebind(Context);}catch(Exception failure){runtimeOverrides.Insert(index,rule);try{Rebind(Context);}catch(Exception cleanup){throw new AggregateException("Runtime override removal and rollback failed.",failure,cleanup);}throw;}
        runtimeOverrideHandles.Remove(handle);return true;
    }
    public void ResetRuntimeOverrides(){if(disposed)throw new ObjectDisposedException(nameof(ViewInstance));runtimeOverrides.Clear();runtimeOverrideHandles.Clear();Rebind(Context);}
    public async Task SetElementShownAsync(string id,bool show,CancellationToken token=default)
    {
        if(!Elements.TryGetValue(id,out var element)||!Owns(element))throw new InvalidOperationException("Element is unavailable: "+id);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken,token,ElementOwnershipToken(element));token=linked.Token;token.ThrowIfCancellationRequested();
        var placement=Placements.First(p=>p.Id==id);var nodes=ViewComposer.Walk(element).OfType<IViewElementLifeCycle>().ToArray();
        foreach(var node in nodes)if(show)node.OnBeforeShow();else node.OnBeforeLeave();
        if(show)element.Show();foreach(var node in nodes)if(show)node.OnStartShow();else node.OnStartLeave();
        await AnimateElementAsync(placement,show,token);
        token.ThrowIfCancellationRequested();
        if(!show)element.Hide();foreach(var node in nodes)if(show)node.OnShown();else node.OnHidden();
    }
    private async Task AnimateElementAsync(ViewElementPlacement placement,bool show,CancellationToken token)
    {
        if(!Elements.TryGetValue(placement.Id,out var element)||!Owns(element))return;
        await AnimateOwnedAsync(element,token,t=>AnimateElementCoreAsync(element,placement,show,t));
    }
    private async Task AnimateElementCoreAsync(Control element,ViewElementPlacement placement,bool show,CancellationToken token)
    {
        double delay=show?placement.DelayIn:placement.DelayOut;
        if(delay>0)await ViewController.TweenDelay(element,delay,token,IgnoreTimeScale??false);
        await PlayTransferAsync(element,placement,token);
        var animation=show?placement.ShowTransition:placement.HideTransition;
        if(animation!=null)await animation.PlayAsync(element,show,token,IgnoreTimeScale);
    }
    internal void ValidateAnimations()
    {
        Definition.ShowTransition?.Validate();Definition.HideTransition?.Validate();
        foreach(var item in Placements)
        {if(!double.IsFinite(item.DelayIn)||!double.IsFinite(item.DelayOut)||item.DelayIn<0||item.DelayOut<0)throw new InvalidOperationException("Element delays must be finite and nonnegative.");item.ShowTransition?.Validate();item.HideTransition?.Validate();}
        foreach (string name in new[] { Definition.ShowAnimation, Definition.HideAnimation }.Where(n => n.Length > 0))
        {
            var player = Root.GetNodeOrNull<AnimationPlayer>(Definition.AnimationPlayerPath)
                ?? throw new InvalidOperationException($"{Definition.Id}: animation player is missing.");
            if (!player.HasAnimation(name) || player.GetAnimation(name).LoopMode != Animation.LoopModeEnum.None)
                throw new InvalidOperationException($"{Definition.Id}: animation '{name}' must exist and must not loop.");
        }
    }
    public async Task AnimateAsync(bool show,CancellationToken token)
    {
        if(SkipTransitions)return;
        var tasks=new List<Task>{AnimateRootAsync(show,token)};
        foreach(var placement in Placements.Where(p=>!p.ManualOnly))tasks.Add(AnimateElementAsync(placement,show,token));
        await Task.WhenAll(tasks);
    }
    private Task AnimateRootAsync(bool show,CancellationToken token)
        =>rootLease==null?AnimateRootCoreAsync(show,token):AnimateOwnedAsync(Root,token,t=>AnimateRootCoreAsync(show,t),allowLoan:false);
    private async Task AnimateRootCoreAsync(bool show,CancellationToken token)
    {
        if(show&&rootLease!=null)await PlayTransferAsync(Root,rootLease,token);
        var transition=show?Definition.ShowTransition:Definition.HideTransition;
        if(transition!=null){await transition.PlayAsync(Root,show,token,IgnoreTimeScale);return;}
        string name = show ? Definition.ShowAnimation : Definition.HideAnimation;
        if (name.Length == 0) return;
        var player = Root.GetNodeOrNull<AnimationPlayer>(Definition.AnimationPlayerPath)
            ?? throw new InvalidOperationException($"{Definition.Id}: animation player is missing.");
        if (!player.HasAnimation(name)) throw new InvalidOperationException($"{Definition.Id}: animation '{name}' is missing.");
        if (player.GetAnimation(name).LoopMode != Animation.LoopModeEnum.None)
            throw new InvalidOperationException($"{Definition.Id}: show/hide animations must not loop.");
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finished(StringName animation) { if (animation == name) completion.TrySetResult(); }
        player.AnimationFinished += Finished;
        var mode=player.CallbackModeProcess;
        try
        {
            token.ThrowIfCancellationRequested();
            if(IgnoreTimeScale==true)player.CallbackModeProcess=AnimationMixer.AnimationCallbackModeProcess.Manual;
            player.Play(name);
            if(IgnoreTimeScale==true)
            {
                ulong previous=Time.GetTicksUsec();
                while(!completion.Task.IsCompleted)
                {
                    await Root.ToSignal(Root.GetTree(),SceneTree.SignalName.ProcessFrame).AsTask().WaitAsync(token);
                    ulong now=Time.GetTicksUsec();player.Advance((now-previous)/1000000.0);previous=now;
                }
            }
            await completion.Task.WaitAsync(token);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(player))
            { player.AnimationFinished -= Finished; if (player.IsPlaying()) player.Stop();player.CallbackModeProcess=mode; }
        }
    }
    internal void BeforeElementReturn(Control element)
    {var errors=new List<Exception>();foreach(var node in OwnedWalk(element))if(node is IViewElementPoolLifeCycle callback)try{callback.OnBeforeReturnToPool();}catch(Exception error){errors.Add(error);}if(errors.Count>0)throw new AggregateException("Pool return lifecycle failed.",errors);}
    internal void RestoreElement(Control element)
    {
        bool In(Node node)=>node==element||element.IsAncestorOf(node);
        foreach(var item in originals.Where(p=>In(p.Key.Item1)))if(GodotObject.IsInstanceValid(item.Key.Item1))item.Key.Item1.Set(item.Key.Item2,item.Value);
        foreach(var item in attributeOriginals.Where(p=>p.Key.Owner is Node n&&In(n)))ViewAttributeBindings.Write(item.Key.Member,item.Key.Owner,item.Value);
        if(sourceVisuals.TryGetValue(element,out var visual)){element.Modulate=visual.Modulate;element.SelfModulate=visual.Self;}
        var placement=rootLease!=null&&element==Root?rootLease:Placements.FirstOrDefault(p=>Elements.GetValueOrDefault(p.Id)==element);
        if(placement!=null&&sourceLayouts.TryGetValue(placement.Id,out var source)&&element.GetParent() is not Container)source.Apply(element);
        else if(placement!=null&&sourceLayouts.TryGetValue(placement.Id,out source)){element.Scale=source.Scale;element.Rotation=source.Rotation;element.PivotOffset=source.PivotOffset;element.ZIndex=source.SortOrder;}
    }
    internal void ResumeElement(ViewElementPlacement placement)
    {
        RenewElementOwnership(rootLease!=null&&placement.Id=="__root"?Root:Elements[placement.Id]);
        Rebind(Context);
        var element=rootLease!=null&&placement.Id=="__root"?Root:Elements[placement.Id];
        foreach(var node in OwnedWalk(element))if(node is IViewElementLifeCycle participant)participant.RefreshView();
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        var errors=new List<Exception>();void Attempt(Action action){try{action();}catch(Exception error){errors.Add(error);}}
        Attempt(lifetime.Cancel);Attempt(entryLifetime.Cancel);lifetime.Dispose();entryLifetime.Dispose();
        foreach(var element in elementOwnership.Keys.ToArray())Attempt(()=>SuspendElement(element));
        if(owners.TryGetValue(Root,out var currentOwner)&&currentOwner==this)owners.Remove(Root);
        foreach(var connection in connections)
            Attempt(()=>{if(GodotObject.IsInstanceValid(connection.Node)&&connection.Node.IsConnected(connection.Signal,connection.Callback))connection.Node.Disconnect(connection.Signal,connection.Callback);});
        connections.Clear();
        if(pool!=null)foreach(var element in Elements.Values)Attempt(()=>pool.Release(this,element));
        if(pool!=null&&rootLease!=null)Attempt(()=>pool.Release(this,Root,ForceKeepRecovery,ForceDestroyRecovery));
        pool?.MarkPending(this,false);
        originals.Clear();attributeOriginals.Clear();runtimeOverrides.Clear();runtimeOverrideHandles.Clear();
        foreach(var connection in layoutConnections)
            Attempt(()=>{if(GodotObject.IsInstanceValid(connection.Node)&&connection.Node.IsConnected(Control.SignalName.Resized,connection.Callback))connection.Node.Disconnect(Control.SignalName.Resized,connection.Callback);});
        foreach(var layout in sourceLayouts.Values)Attempt(layout.Data.Dispose);Attempt(rootSourceLayout.Data.Dispose);
        layoutConnections.Clear();sourceLayouts.Clear();sourceVisuals.Clear();focusOriginals.Clear();transfers.Clear();
        Attempt(()=>{if(GodotObject.IsInstanceValid(mountParent)&&mountParent!.IsConnected(Control.SignalName.Resized,mountLayoutCallback))mountParent.Disconnect(Control.SignalName.Resized,mountLayoutCallback);});mountParent=null;
        Attempt(()=>{if(rootLease==null&&GodotObject.IsInstanceValid(Root)){bool mounted=Root.IsInsideTree();Root.GetParent()?.RemoveChild(Root);if(mounted)Root.QueueFree();else Root.Free();}});
        if(errors.Count>0)throw new AggregateException("View ownership cleanup failed.",errors);
    }
    public async ValueTask DisposeAsync()
    {
        var tree = GodotObject.IsInstanceValid(Root) && Root.IsInsideTree() ? Root.GetTree() : null;
        var errors=new List<Exception>();try{Dispose();}catch(Exception error){errors.Add(error);}
        try{await RecoveryReady;}catch(Exception error){errors.Add(error);}
        if (tree != null && GodotObject.IsInstanceValid(Root))await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if(errors.Count==1)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if(errors.Count>1)throw new AggregateException("View disposal and transfer failed.",errors);
    }
}
