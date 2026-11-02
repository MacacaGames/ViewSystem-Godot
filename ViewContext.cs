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
    { models[(typeof(T), key)] = model;if(model is IViewInjectValues source)foreach(var item in source.Entries)models[(source.ValueType,item.Key)]=item.Value; return this; }
    internal void AddObject(object model) {models[(model.GetType(),"")]=model;if(model is IViewInjectValues source)foreach(var item in source.Entries)models[(source.ValueType,item.Key)]=item.Value;}
    internal bool TryModelByName(string type,string key,out object value)
    {
        static string Alias(Type t)=>t==typeof(string)?"string":t==typeof(int)?"int":t==typeof(float)?"float":t==typeof(double)?"double":t==typeof(bool)?"bool":t==typeof(long)?"long":t.Name;
        var matches=models.Where(p=>p.Key.Item2==key&&(p.Key.Item1.Name==type||p.Key.Item1.FullName==type||Alias(p.Key.Item1)==type)).ToArray();
        if(matches.Length>1)throw new InvalidOperationException("Ambiguous model type name: "+type+"; use full type name.");
        value=matches.Length==1?matches[0].Value:null!;return matches.Length==1;
    }
    internal void RemoveObject(object model){var key=(model.GetType(),"");if(models.TryGetValue(key,out var current)&&ReferenceEquals(current,model))models.Remove(key);}
    internal bool TryValue(string key, out Variant value) => values.TryGetValue(key, out value);
    internal bool TryModel(Type type,string key,out object value) => models.TryGetValue((type,key),out value!);
    internal bool TryModel<T>(string key, out T model)
    {
        if (models.TryGetValue((typeof(T), key), out var value)) { model = (T)value; return true; }
        model = default!; return false;
    }
    public static ViewModels FromObjects(params object[] source)
    {var result=new ViewModels();var types=new HashSet<Type>();foreach(var model in source){if(model==null)throw new ArgumentException("Models cannot be null.");if(!types.Add(model.GetType()))throw new InvalidOperationException("Use named Add<T> values or ViewInjectDictionary<T> for multiple models of one type.");result.AddObject(model);}return result;}
    public static ViewModels From(ViewModelData? data)
    {
        var result = new ViewModels();
        if (data != null) foreach (var item in data.Values) result.Set(item.Key, item.Value);
        return result;
    }
}

public sealed record ViewContext(ViewModels Page, ViewModels Shared, bool IsPreview = false,
    Action<string, Control>? Dispatch = null, IReadOnlySet<string>? ActiveBreakpoints = null, Action<ViewActionEvent>? DispatchEvent = null, Rect2? NormalizedSafeArea = null, ViewSafeAreaSettings? GlobalSafeArea = null)
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
    internal object ModelByName(string type,string key,ViewInjectScope scope)
    {foreach(var source in Search(scope))if(source.TryModelByName(type,key,out var model))return model;throw new InvalidOperationException($"Missing view model {type} '{key}' ({scope}).");}
    public Variant ResolveModelExpression(string expression)=>ViewModelExpression.Resolve(this,expression);
    internal object ModelForMember(Type type,string key,string member,ViewInjectScope scope)
    {foreach(var source in Search(scope)){if(key.Length==0&&source.TryModel(type,member,out var named))return named;if(source.TryModel(type,key,out var value))return value;}throw new InvalidOperationException($"Missing view model {type.Name} for member '{member}' ({scope}).");}
    internal object Model(Type type,string key,ViewInjectScope scope)
    {
        foreach(var source in Search(scope))if(source.TryModel(type,key,out var value))return value;
        throw new InvalidOperationException($"Missing view model {type.Name} '{key}' ({scope}).");
    }
    public T Model<T>(string key = "", ViewInjectScope scope = ViewInjectScope.PageFirst)
    {
        foreach (var source in Search(scope)) if (source.TryModel<T>(key, out var model)) return model;
        throw new InvalidOperationException($"Missing view model {typeof(T).Name} '{key}' ({scope}).");
    }
}

/// <summary>Called before overrides and before the first _Ready. Cache injected data here.</summary>
public interface IViewElementOverrideListener { void OnApplyOverride(){} void OnOverridesApplied(ViewInstance view)=>OnApplyOverride(); }
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
    void OnChangePage(bool show){}
    void OnChangedPage(){}
}
public sealed record ViewPageShowContext(string PageId, bool IsOverlay, ViewContext Models);
public interface IViewPageShowHook
{
    bool ShouldHandle(ViewPageShowContext context) => true;
    Task BeforePrepareAsync(ViewPageShowContext context, CancellationToken token);
    Task AfterReadyAsync(ViewPageShowContext context, CancellationToken token);
    void OnAborted(ViewPageShowContext context);
}
public interface IViewPageShowHookExecutionPolicy
{
    int Order { get; }
    float TimeoutSeconds { get; }
}
public interface IViewPageShowPhaseCallback
{
    Task OnBeforePreparePhaseCompletedAsync(ViewPageShowContext context, CancellationToken token);
}
