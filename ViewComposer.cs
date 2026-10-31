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
    public static ViewInstance Create(ViewEntry entry, ViewContext context)
    {
        if (entry.Scene == null) throw new InvalidOperationException($"{entry.Id}: missing root scene.");
        var root = InstantiateControl(entry.Scene, entry.Id);
        var instance = new ViewInstance(entry, root);
        try
        {
            root.ZIndex = entry.SortOrder;
            foreach (var placement in entry.Elements)
            {
                if (placement == null || string.IsNullOrWhiteSpace(placement.Id) || placement.Scene == null)
                    throw new InvalidOperationException($"{entry.Id}: every element needs an ID and scene.");
                if (instance.Elements.ContainsKey(placement.Id))
                    throw new InvalidOperationException($"{entry.Id}: duplicate element '{placement.Id}'.");
                var parent = root.GetNodeOrNull(placement.ParentPath)
                    ?? throw new InvalidOperationException($"{entry.Id}/{placement.Id}: parent '{placement.ParentPath}' is missing.");
                if (placement.NodeName.Length != 0)
                {
                    if (parent.HasNode(new NodePath(placement.NodeName)))
                        throw new InvalidOperationException($"{entry.Id}: duplicate authored node name '{placement.NodeName}'.");
                }
                var element = InstantiateControl(placement.Scene, entry.Id + "/" + placement.Id);
                if (placement.NodeName.Length != 0) element.Name = placement.NodeName;
                parent.AddChild(element);
                if (placement.SiblingIndex >= 0) parent.MoveChild(element, Math.Min(placement.SiblingIndex,parent.GetChildCount()-1));
                instance.Elements.Add(placement.Id, element);
                instance.BindLayout(placement,element);
            }
            instance.Rebind(context);
            instance.ConnectEvents();
            return instance;
        }
        catch { instance.Dispose(); throw; }
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

public sealed class ViewInstance : IDisposable, IAsyncDisposable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, ViewInstance> owners = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationTokenSource entryLifetime = new();
    public CancellationToken LifetimeToken => lifetime.Token;
    internal CancellationToken EntryToken => entryLifetime.Token;
    internal void CancelEntry() => entryLifetime.Cancel();
    public Task Ready { get; internal set; } = Task.CompletedTask;
    public ViewEntry Definition { get; }
    public Control Root { get; }
    public Dictionary<string, Control> Elements { get; } = new(StringComparer.Ordinal);
    public ViewContext Context { get; private set; } = ViewContext.Empty;
    private readonly Dictionary<(Node, string), Variant> originals = new();
    private readonly List<(Node Node, StringName Signal, Callable Callback)> connections = new();
    private readonly List<(Control Node,Callable Callback)> layoutConnections=new();
    internal void BindLayout(ViewElementPlacement placement,Control element)
    {
        if(placement.Layout==null)return;
        var callback=Callable.From(()=>placement.Layout?.ApplyPivot(element));
        element.Connect(Control.SignalName.Resized,callback);layoutConnections.Add((element,callback));
    }
    private bool disposed;
    internal ViewInstance(ViewEntry definition, Control root) { Definition = definition; Root = root; owners.Add(root, this); }
    private IEnumerable<Node> OwnedWalk(Node node)
    {
        if (node != Root && node is Control control && owners.TryGetValue(control, out var owner) && !ReferenceEquals(owner, this)) yield break;
        yield return node;
        foreach (var child in node.GetChildren()) foreach (var nested in OwnedWalk(child)) yield return nested;
    }

    public void Rebind(ViewContext context)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ViewInstance));
        if (connections.Count > 0 && context.Dispatch == null) throw new InvalidOperationException("A bound view requires an action dispatcher when rebinding.");
        Context = context;
        foreach (var item in originals) item.Key.Item1.Set(item.Key.Item2, item.Value);
        foreach (var placement in Definition.Elements) placement.Layout?.Apply(Elements[placement.Id]);
        foreach (var node in OwnedWalk(Root))
            if (node is IViewModelReceiver receiver && (!context.IsPreview || node is IViewPreviewParticipant))
                receiver.Inject(context);
        Apply(Root, Definition.Overrides);
        foreach (var placement in Definition.Elements) Apply(Elements[placement.Id], placement.Overrides);
    }
    private void Apply(Control root, IEnumerable<ViewPropertyOverride> rules)
    {
        // Literal page settings first; model bindings take precedence independent of array order.
        foreach (var rule in rules.OrderBy(rule => string.IsNullOrWhiteSpace(rule?.ModelKey) ? 0 : 1))
        {
            if (rule == null) throw new InvalidOperationException($"{Definition.Id}: empty override.");
            var node = root.GetNodeOrNull(rule.Target)
                ?? throw new InvalidOperationException($"{Definition.Id}: override target '{rule.Target}' is missing.");
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
    internal void ConnectEvents()
    {
        Connect(Root, Definition.Events);
        foreach (var placement in Definition.Elements) Connect(Elements[placement.Id], placement.Events);
    }
    private void Connect(Control element, IEnumerable<ViewEventBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.Action)) throw new InvalidOperationException("Event requires an action ID.");
            if (Context.Dispatch == null) throw new InvalidOperationException($"{Definition.Id}: no action dispatcher for '{binding.Action}'.");
            var target = element.GetNodeOrNull(binding.Target) ?? throw new InvalidOperationException($"Missing event target '{binding.Target}'.");
            var signal = target.GetSignalList().FirstOrDefault(item => item["name"].AsString() == binding.Signal);
            if (signal == null || signal["args"].AsGodotArray().Count != 0)
                throw new InvalidOperationException($"{Definition.Id}: event '{binding.Signal}' must be a zero-argument signal.");
            var callback = Callable.From(() => Context.Dispatch!(binding.Action, element));
            target.Connect(binding.Signal, callback);
            connections.Add((target, binding.Signal, callback));
        }
    }
    public void Lifecycle(Action<IViewElementLifeCycle> callback)
    {
        if (Context.IsPreview) return;
        foreach (var node in OwnedWalk(Root)) if (node is IViewElementLifeCycle participant) callback(participant);
    }
    internal void CleanupLifecycle(Action<IViewElementLifeCycle> callback)
    {
        if (Context.IsPreview || !GodotObject.IsInstanceValid(Root)) return;
        var errors = new List<Exception>();
        foreach (var node in OwnedWalk(Root).ToArray())
            if (node is IViewElementLifeCycle participant)
                try { callback(participant); } catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException("View lifecycle cleanup failed.", errors);
    }
    internal void ValidateAnimations()
    {
        foreach (string name in new[] { Definition.ShowAnimation, Definition.HideAnimation }.Where(n => n.Length > 0))
        {
            var player = Root.GetNodeOrNull<AnimationPlayer>(Definition.AnimationPlayerPath)
                ?? throw new InvalidOperationException($"{Definition.Id}: animation player is missing.");
            if (!player.HasAnimation(name) || player.GetAnimation(name).LoopMode != Animation.LoopModeEnum.None)
                throw new InvalidOperationException($"{Definition.Id}: animation '{name}' must exist and must not loop.");
        }
    }
    public async Task AnimateAsync(bool show, CancellationToken token)
    {
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
        try { token.ThrowIfCancellationRequested(); player.Play(name); await completion.Task.WaitAsync(token); }
        finally
        {
            if (GodotObject.IsInstanceValid(player))
            { player.AnimationFinished -= Finished; if (player.IsPlaying()) player.Stop(); }
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; lifetime.Cancel(); entryLifetime.Cancel();
        lifetime.Dispose(); entryLifetime.Dispose();
        owners.Remove(Root);
        foreach (var connection in connections)
            if (GodotObject.IsInstanceValid(connection.Node) && connection.Node.IsConnected(connection.Signal, connection.Callback))
                connection.Node.Disconnect(connection.Signal, connection.Callback);
        connections.Clear(); originals.Clear();
        foreach(var connection in layoutConnections)
            if(GodotObject.IsInstanceValid(connection.Node) && connection.Node.IsConnected(Control.SignalName.Resized,connection.Callback))
                connection.Node.Disconnect(Control.SignalName.Resized,connection.Callback);
        layoutConnections.Clear();
        if (GodotObject.IsInstanceValid(Root))
        {
            bool mounted = Root.IsInsideTree();
            Root.GetParent()?.RemoveChild(Root);
            // Input signals may still be traversing a mounted view's controls.
            if (mounted) Root.QueueFree(); else Root.Free();
        }
    }
    public async ValueTask DisposeAsync()
    {
        var tree = GodotObject.IsInstanceValid(Root) && Root.IsInsideTree() ? Root.GetTree() : null;
        Dispose();
        if (tree != null && GodotObject.IsInstanceValid(Root))
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}
