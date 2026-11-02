#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private sealed record NestedEditor(EditorInspector Inspector,Resource Source,Resource Proxy,EditorInspector Root,string Field);
    private readonly Dictionary<ulong,NestedEditor> nestedEditors=new();
    // Built-in subinspectors otherwise create their own Scene-history actions for
    // detached Resources. Keep their native property widgets, forwarding writes
    // through the same document/draft path as the top-level fields.
    private void SyncNestedInspectors()
    {
        foreach(var pair in nestedEditors.ToArray())if(!GodotObject.IsInstanceValid(pair.Value.Inspector)||!pair.Value.Inspector.IsInsideTree())
        {DisconnectUi(pair.Value.Proxy);pair.Value.Proxy.Dispose();nestedEditors.Remove(pair.Key);}
        foreach(var root in new[]{entrySettings,elementSettings,overrideValueInspector,advancedInspector?.GetEditedObject()==advancedProxy?advancedInspector:null})
        {
            if(!GodotObject.IsInstanceValid(root)||!root!.IsVisibleInTree())continue;
            foreach(var inspector in root.FindChildren("*","EditorInspector",true,false).OfType<EditorInspector>().ToArray())
            {
                if(!GodotObject.IsInstanceValid(inspector)||!inspector.IsInsideTree()||inspector.GetEditedObject() is not Resource source||source is PackedScene or Script)continue;
                ulong id=inspector.GetInstanceId();
                if(nestedEditors.TryGetValue(id,out var current))
                {
                    if(source==current.Proxy)continue;
                    if(source==current.Source){inspector.Edit(current.Proxy);continue;}
                    DisconnectUi(current.Proxy);current.Proxy.Dispose();nestedEditors.Remove(id);
                }
                string field="";for(Node? node=inspector.GetParent();node!=null&&node!=root;node=node.GetParent())if(node is EditorProperty property)field=property.GetEditedProperty().ToString();
                if(field.Length==0)continue;field=field.Split('/')[0];
                var names=source.GetPropertyList().Where(p=>
                {string name=p["name"].AsString();var usage=(PropertyUsageFlags)p["usage"].AsInt64();return name!="script"&&!name.StartsWith("resource_",StringComparison.Ordinal)&&!name.StartsWith("metadata/_",StringComparison.Ordinal)&&(usage&(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage))==(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage)&&(usage&PropertyUsageFlags.ReadOnly)==0;}).Select(p=>p["name"].AsString()).ToArray();
                var proxy=(Resource)GD.Load<GDScript>("res://addons/macaca_viewsystem/Editor/selection_settings_proxy.gd").New().AsGodotObject();proxy.Call("configure",source,names);
                var binding=new NestedEditor(inspector,source,proxy,root,field);nestedEditors[id]=binding;
                ConnectUi(proxy,"setting_changed",nameof(OnNestedSettingChanged),id);inspector.Edit(proxy);
            }
        }
    }
    private void OnNestedSettingChanged(StringName property,Variant next,ulong id)=>Try(()=>
    {
        if(!nestedEditors.TryGetValue(id,out var binding))return;
        binding.Source.Set(property,next);
        for(Node? node=binding.Inspector.GetParent();node!=null&&node!=binding.Root;node=node.GetParent())
            if(node is EditorInspector parent&&nestedEditors.TryGetValue(parent.GetInstanceId(),out var ancestor)&&referenceRouter!=null)
                foreach(var info in ancestor.Source.GetPropertyList())
                {
                    string name=info["name"].AsString();var usage=(PropertyUsageFlags)info["usage"].AsInt64();
                    if(name=="script"||name.StartsWith("resource_",StringComparison.Ordinal)||name.StartsWith("metadata/_",StringComparison.Ordinal)||(usage&(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage))!=(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage)||(usage&PropertyUsageFlags.ReadOnly)!=0)continue;
                    var value=ancestor.Proxy.Get(name);if(!ViewReferenceDiff.Equivalent(ancestor.Source.Get(name),value,referenceRouter))ancestor.Source.Set(name,value);
                }
        if(binding.Root==overrideValueInspector)CaptureNestedTypedEdits();else if(binding.Root==advancedInspector)OnAdvancedPropertyEdited(binding.Field);else OnContextPropertyEdited(binding.Field);
    });
    private void ReleaseNestedInspectors()
    {
        foreach(var binding in nestedEditors.Values)
        {if(GodotObject.IsInstanceValid(binding.Inspector))binding.Inspector.Edit(null);DisconnectUi(binding.Proxy);binding.Proxy.Dispose();}
        nestedEditors.Clear();
    }
}
#endif
