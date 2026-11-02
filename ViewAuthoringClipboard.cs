using Godot;
using System;
namespace MacacaGames.ViewSystem;

/// <summary>Detached snapshots. Pasting never shares mutable authoring resources.</summary>
public sealed class ViewAuthoringClipboard
{
    private global::Godot.Collections.Array<ViewPropertyOverride>? overrides;
    private global::Godot.Collections.Array<ViewEventBinding>? events;
    public bool HasOverrides => overrides != null;
    public bool HasEvents => events != null;
    public void CopyOverrides(global::Godot.Collections.Array<ViewPropertyOverride> source)
        => overrides = Copy(source);
    public void CopyEvents(global::Godot.Collections.Array<ViewEventBinding> source)
        => events = Copy(source);
    public global::Godot.Collections.Array<ViewPropertyOverride> PasteOverrides()
        => Copy(overrides ?? throw new InvalidOperationException("Copy overrides first."));
    public global::Godot.Collections.Array<ViewEventBinding> PasteEvents()
        => Copy(events ?? throw new InvalidOperationException("Copy events first."));
    private static global::Godot.Collections.Array<T> Copy<[MustBeVariant] T>(global::Godot.Collections.Array<T> source) where T : Resource
    {
        var result = new global::Godot.Collections.Array<T>();
        foreach (var item in source)
            result.Add(item == null ? throw new InvalidOperationException("Clipboard contains an empty resource.") : (T)item.Duplicate(true));
        return result;
    }
}
