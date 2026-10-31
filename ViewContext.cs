using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

/// <summary>Per-request values and explicitly supplied typed models; no global service locator.</summary>
public sealed class ViewModels
{
    private readonly Dictionary<string, Variant> values = new(StringComparer.Ordinal);
    private readonly Dictionary<(Type, string), object> models = new();
    public ViewModels Set(string key, Variant value) { values[key] = value; return this; }
    public ViewModels Add<T>(T model, string key = "") where T : notnull
    { models[(typeof(T), key)] = model; return this; }
    internal bool TryValue(string key, out Variant value) => values.TryGetValue(key, out value);
    internal bool TryModel<T>(string key, out T model)
    {
        if (models.TryGetValue((typeof(T), key), out var value)) { model = (T)value; return true; }
        model = default!; return false;
    }
    public static ViewModels From(ViewModelData? data)
    {
        var result = new ViewModels();
        if (data != null) foreach (var item in data.Values) result.Set(item.Key, item.Value);
        return result;
    }
}

public sealed record ViewContext(ViewModels Page, ViewModels Shared, bool IsPreview = false,
    Action<string, Control>? Dispatch = null)
{
    public static ViewContext Empty => new(new(), new());
    private IEnumerable<ViewModels> Search(ViewInjectScope scope) => scope switch
    {
        ViewInjectScope.PageFirst => new[] { Page, Shared },
        ViewInjectScope.PageOnly => new[] { Page },
        ViewInjectScope.SharedFirst => new[] { Shared, Page },
        ViewInjectScope.SharedOnly => new[] { Shared },
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };
    public Variant Value(string key, ViewInjectScope scope = ViewInjectScope.PageFirst)
    {
        foreach (var source in Search(scope)) if (source.TryValue(key, out var value)) return value;
        throw new InvalidOperationException($"Missing view model value '{key}' ({scope}).");
    }
    public T Model<T>(string key = "", ViewInjectScope scope = ViewInjectScope.PageFirst)
    {
        foreach (var source in Search(scope)) if (source.TryModel<T>(key, out var model)) return model;
        throw new InvalidOperationException($"Missing view model {typeof(T).Name} '{key}' ({scope}).");
    }
}

/// <summary>Called before overrides and before the first _Ready. Cache injected data here.</summary>
public interface IViewModelReceiver { void Inject(ViewContext context); }
/// <summary>Explicit permission for presentation-only injection in an editor preview.</summary>
public interface IViewPreviewParticipant { }
public interface IViewElementLifeCycle
{
    void OnBeforeShow();
    void OnStartShow();
    void OnShown();
    void OnBeforeLeave();
    void OnStartLeave();
    void OnHidden();
    void RefreshView();
}
public sealed record ViewPageShowContext(string PageId, bool IsOverlay, ViewContext Models);
public interface IViewPageShowHook
{
    Task BeforePrepareAsync(ViewPageShowContext context, CancellationToken token);
    Task AfterReadyAsync(ViewPageShowContext context, CancellationToken token);
    void OnAborted(ViewPageShowContext context);
}
