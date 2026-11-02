using Godot;
namespace MacacaGames.ViewSystem;
[Tool,GlobalClass]
public partial class ViewOverrideSet:Resource,ISerializationListener
{
    private global::Godot.Collections.Array<ViewPropertyOverride> values=new();
    private readonly System.Collections.Generic.HashSet<Resource> retained=new();
    [Export] public global::Godot.Collections.Array<ViewPropertyOverride> Values
    {get=>values;set{values=value;Retain();}}
    private void Retain(){foreach(var rule in values)if(rule!=null){retained.Add(rule);if(rule.Resource!=null)retained.Add(rule.Resource);}}
    public void OnBeforeSerialize()
    {Retain();using var handoff=new global::Godot.Collections.Array();foreach(var resource in retained)handoff.Add(resource);SetMeta("_view_override_ownership",handoff);}
    public void OnAfterDeserialize()=>CallDeferred(nameof(RestoreOwnership));
    private void RestoreOwnership()
    {Retain();if(!HasMeta("_view_override_ownership"))return;using var resources=GetMeta("_view_override_ownership").AsGodotArray();foreach(var item in resources)if(item.AsGodotObject() is Resource resource)retained.Add(resource);RemoveMeta("_view_override_ownership");}
}
