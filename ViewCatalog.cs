using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;
[Tool, GlobalClass]
public partial class ViewCatalog:Resource, ISerializationListener
{
    [Export] public ViewNavigationMode NavigationMode {get;set;}=ViewNavigationMode.UnityTiming;
    [Export] public double MaximumWaitSeconds {get;set;}=1.5;
    [Export] public bool BuiltInClickProtection {get;set;}=true;
    [Export] public double MinimumTimeInterval {get;set;}=.2;
    [Export] public ViewSafeAreaSettings SafeArea {get;set;} = new();
    [Export] public string[] Breakpoints { get; set; } = System.Array.Empty<string>();
    [Export(PropertyHint.Range,"0,8192,1")] public int ReferenceHeight {get;set;}
    [Export] public global::Godot.Collections.Array<ViewActionDefinition> Actions {get;set;} = new();
    private global::Godot.Collections.Array<ViewEntry> entries=new();
    private readonly HashSet<Resource> retainedAuthoredResources=new(System.Collections.Generic.ReferenceEqualityComparer.Instance);
    [Export] public global::Godot.Collections.Array<ViewEntry> Entries
    {
        get=>entries;
        set { entries=value;RetainAuthoredGraph(); }
    }
    // Godot 4.7.2 can retain a native Resource after its managed wrapper was
    // collected (#112067). Match wrapper ownership to catalog/Undo history,
    // instead of depending on native array/cache references to root wrappers.
    public void RetainAuthoredGraph()
    {
        if(SafeArea!=null)retainedAuthoredResources.Add(SafeArea);
        foreach(var action in Actions)
            if(action!=null){retainedAuthoredResources.Add(action);if(action.HandlerScript!=null)retainedAuthoredResources.Add(action.HandlerScript);}
        if(SharedPreviewModel!=null)retainedAuthoredResources.Add(SharedPreviewModel);
        foreach(var entry in entries)
        {
            if(entry==null)continue;
            retainedAuthoredResources.Add(entry);
            if(entry.PoolPolicy!=null)retainedAuthoredResources.Add(entry.PoolPolicy);
            if(entry.ShowTransition!=null)retainedAuthoredResources.Add(entry.ShowTransition);
            if(entry.HideTransition!=null)retainedAuthoredResources.Add(entry.HideTransition);
            if(entry.SafeArea!=null)retainedAuthoredResources.Add(entry.SafeArea);
            foreach(var navigation in entry.StateNavigation){if(navigation==null)continue;retainedAuthoredResources.Add(navigation);foreach(var link in navigation.Links)if(link!=null)retainedAuthoredResources.Add(link);}
            foreach(var focus in entry.FocusLinks)if(focus!=null)retainedAuthoredResources.Add(focus);
            if(entry.Scene!=null)retainedAuthoredResources.Add(entry.Scene);
            if(entry.PreviewModel!=null)retainedAuthoredResources.Add(entry.PreviewModel);
            foreach(var rule in entry.Overrides)KeepRule(rule);
            foreach(var binding in entry.Events)if(binding!=null)retainedAuthoredResources.Add(binding);
            foreach(var item in entry.Elements)
            {
                if(item==null)continue;
                retainedAuthoredResources.Add(item);
                if(item.ShowTransition!=null)retainedAuthoredResources.Add(item.ShowTransition);
                if(item.HideTransition!=null)retainedAuthoredResources.Add(item.HideTransition);
                if(item.PoolPolicy!=null)retainedAuthoredResources.Add(item.PoolPolicy);
                if(item.Scene!=null)retainedAuthoredResources.Add(item.Scene);
                if(item.Layout!=null)retainedAuthoredResources.Add(item.Layout.Data);
                foreach(var breakpoint in item.Breakpoints)
                {if(breakpoint==null)continue;retainedAuthoredResources.Add(breakpoint);if(breakpoint.LayoutData!=null)retainedAuthoredResources.Add(breakpoint.LayoutData);}
                foreach(var rule in item.Overrides)KeepRule(rule);
                foreach(var binding in item.Events)if(binding!=null)retainedAuthoredResources.Add(binding);
            }
        }
    }
    private void KeepRule(ViewPropertyOverride? rule)
    {
        if(rule==null)return;
        retainedAuthoredResources.Add(rule);
        if(rule.Resource!=null)retainedAuthoredResources.Add(rule.Resource);
        foreach(var resource in rule.RetainedNativeResources)retainedAuthoredResources.Add(resource);
    }
    private const string ReloadOwnership="_macaca_viewsystem_resource_ownership";
    public void OnBeforeSerialize()
    {
        RetainAuthoredGraph();
        using var handoff = new global::Godot.Collections.Array();
        foreach(var resource in retainedAuthoredResources)handoff.Add(resource);
        SetMeta(ReloadOwnership,handoff);
    }
    public void OnAfterDeserialize()=>CallDeferred(nameof(RestoreManagedOwnership));
    private void RestoreManagedOwnership()
    {
        RetainAuthoredGraph();
        if(!HasMeta(ReloadOwnership))return;
        using var handoff=GetMeta(ReloadOwnership).AsGodotArray();
        foreach(var value in handoff)
            if(value.AsGodotObject() is Resource resource)retainedAuthoredResources.Add(resource);
        RemoveMeta(ReloadOwnership);
    }
    [Export] public ViewModelData SharedPreviewModel { get; set; } = new();
    public IReadOnlyDictionary<string,ViewEntry> Validate()
    {
        RetainAuthoredGraph();
        if(!double.IsFinite(MaximumWaitSeconds)||MaximumWaitSeconds<0)throw new InvalidOperationException("MaximumWaitSeconds must be finite and nonnegative.");
        if(!double.IsFinite(MinimumTimeInterval)||MinimumTimeInterval<0)throw new InvalidOperationException("MinimumTimeInterval must be finite and nonnegative.");
        if(Breakpoints.Any(string.IsNullOrWhiteSpace)||Breakpoints.Distinct(StringComparer.Ordinal).Count()!=Breakpoints.Length)throw new InvalidOperationException("Breakpoint names must be nonempty and unique.");
        var result=new Dictionary<string,ViewEntry>(StringComparer.Ordinal);
        foreach(var entry in Entries)
        {
            if(entry==null||string.IsNullOrWhiteSpace(entry.Id)||entry.Scene==null&&string.IsNullOrWhiteSpace(entry.ScenePath))throw new InvalidOperationException("Every ViewCatalog entry needs an ID and PackedScene.");
            if(!double.IsFinite(entry.CustomTransitionWait)||entry.CustomTransitionWait<0||entry.TargetFrameRate < -1)throw new InvalidOperationException("Invalid transition wait/frame rate: "+entry.Id);
            if(!result.TryAdd(entry.Id,entry))throw new InvalidOperationException("Duplicate view ID: "+entry.Id);
        }
        if(Actions.Count>0)
        {
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var action in Actions)
            {
                if(action==null||string.IsNullOrWhiteSpace(action.Id)||!ids.Add(action.Id))throw new InvalidOperationException("Action IDs must be nonempty and unique.");
                if(action.HandlerScript==null||string.IsNullOrWhiteSpace(action.HandlerMethod))throw new InvalidOperationException("Action needs an application handler: "+action.Id);
                if(Engine.IsEditorHint()&&!action.HasHandlerMethod())throw new InvalidOperationException("Action handler method not found: "+action.Id+" → "+action.HandlerMethod);
            }
            foreach(var binding in Entries.SelectMany(e=>e.Events.Concat(e.Elements.SelectMany(p=>p.Events))))
                if(binding!=null&&binding.Enabled&&binding.Handler.Length==0&&!ids.Contains(binding.Action))throw new InvalidOperationException("Undeclared application action: "+binding.Action);
        }
        return result;
    }
}
