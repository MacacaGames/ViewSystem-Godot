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
        foreach(var action in Actions)
            if(action!=null){retainedAuthoredResources.Add(action);if(action.HandlerScript!=null)retainedAuthoredResources.Add(action.HandlerScript);}
        if(SharedPreviewModel!=null)retainedAuthoredResources.Add(SharedPreviewModel);
        foreach(var entry in entries)
        {
            if(entry==null)continue;
            retainedAuthoredResources.Add(entry);
            if(entry.Scene!=null)retainedAuthoredResources.Add(entry.Scene);
            if(entry.PreviewModel!=null)retainedAuthoredResources.Add(entry.PreviewModel);
            foreach(var rule in entry.Overrides)KeepRule(rule);
            foreach(var binding in entry.Events)if(binding!=null)retainedAuthoredResources.Add(binding);
            foreach(var item in entry.Elements)
            {
                if(item==null)continue;
                retainedAuthoredResources.Add(item);
                if(item.Scene!=null)retainedAuthoredResources.Add(item.Scene);
                if(item.Layout!=null)retainedAuthoredResources.Add(item.Layout.Data);
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
        var result=new Dictionary<string,ViewEntry>(StringComparer.Ordinal);
        foreach(var entry in Entries)
        {
            if(entry==null||string.IsNullOrWhiteSpace(entry.Id)||entry.Scene==null)throw new InvalidOperationException("Every ViewCatalog entry needs an ID and PackedScene.");
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
                if(binding!=null&&!ids.Contains(binding.Action))throw new InvalidOperationException("Undeclared application action: "+binding.Action);
        }
        return result;
    }
}
