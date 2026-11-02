using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
namespace MacacaGames.ViewSystem;

public sealed partial class ViewElementPool
{
    private sealed record NestedTemplate(string Path,string Parent,string Name,int Index,PackedScene Scene,Resource Layout,Godot.Collections.Array<ViewPropertyOverride> Values);
    private readonly Dictionary<string,List<NestedTemplate>> nestedTemplates=new(StringComparer.Ordinal);
    private static Control? SingletonUnit(Node marker,Control root)
    {for(Node? cursor=marker;cursor!=null;cursor=cursor.GetParent())if(cursor is Control control)return control==root?null:control;return null;}
    private List<NestedTemplate> PrepareNested(Control root,PackedScene scene)
    {
        string key=Key(scene);
        if(!nestedTemplates.TryGetValue(key,out var templates))
        {
            templates=new();
            var units=ViewComposer.Walk(root).Where(n=>n is IViewElementSingleton).Select(n=>SingletonUnit(n,root)).OfType<Control>().Distinct().ToArray();
            foreach(var unit in units.Where(n=>!units.Any(other=>other!=n&&other.IsAncestorOf(n))))
            {
                string path=root.GetPathTo(unit).ToString();PackedScene template;
                var values=new Godot.Collections.Array<ViewPropertyOverride>();
                if(unit.SceneFilePath.Length>0&&ResourceLoader.Exists(unit.SceneFilePath,"PackedScene"))
                {
                    template=GD.Load<PackedScene>(unit.SceneFilePath);
                    // Preserve editable instance values while sharing the original scene's identity.
                    foreach(var node in ViewComposer.Walk(unit))foreach(var info in node.GetPropertyList())
                    {
                        string name=info["name"].AsString();var usage=(PropertyUsageFlags)info["usage"].AsInt64();
                        if(name=="script"||name=="name"||(usage&(PropertyUsageFlags.Storage|PropertyUsageFlags.Editor))!=(PropertyUsageFlags.Storage|PropertyUsageFlags.Editor)||(usage&PropertyUsageFlags.ReadOnly)!=0)continue;
                        var value=node.Get(name);if(value.VariantType==Variant.Type.Object&&value.AsGodotObject() is Node)continue;
                        values.Add(new(){Target=unit.GetPathTo(node),Property=name,Kind=ViewValueKind.Native,NativeValue=value});
                    }
                }
                else
                {
                    template=new();var owners=ViewComposer.Walk(unit).Skip(1).Select(n=>(Node:n,Owner:n.Owner)).ToArray();
                    try{foreach(var item in owners)if(item.Owner==null||!unit.IsAncestorOf(item.Owner))item.Node.Owner=unit;if(template.Pack(unit)!=Error.Ok)throw new InvalidOperationException("Cannot pack nested singleton: "+path);}
                    finally{foreach(var item in owners)item.Node.Owner=item.Owner;}
                    template.SetMeta("_viewsystem_pool_key",key+"::"+path);
                }
                templates.Add(new(path,root.GetPathTo(unit.GetParent()).ToString(),unit.Name.ToString(),unit.GetIndex(),template,ViewElementLayout.Capture(unit).Data,values));
            }
            nestedTemplates.Add(key,templates);
        }
        foreach(var item in templates)if(root.GetNodeOrNull(item.Path) is {} original){original.GetParent().RemoveChild(original);original.Free();}
        return templates;
    }
    internal void AttachNested(ViewInstance owner,Control root,PackedScene source)
    {
        foreach(var item in lifetimes.ContainsKey(root)?nestedTemplates[Key(source)]:PrepareNested(root,source))
        {
            var parent=root.GetNodeOrNull(item.Parent)??throw new InvalidOperationException("Nested singleton parent missing: "+item.Parent);
            var placement=new ViewElementPlacement {Id="@nested:"+owner.Root.GetPathTo(root)+"/"+item.Path,NodeName=item.Name,ParentPath=owner.Root.GetPathTo(parent),SiblingIndex=item.Index,Scene=item.Scene,LayoutData=item.Layout,Overrides=item.Values,PoolPolicy=new(){Unique=true}};
            var child=Acquire(owner,placement,parent,item.Scene);owner.AddNested(placement,child);
            AttachNested(owner,child,item.Scene);
        }
    }
    internal (PackedScene Scene,ViewElementPolicy Policy) SingletonSource(Control element)
    {
        if(disposed)throw new ObjectDisposedException(nameof(ViewElementPool));
        var bucket=buckets.Values.FirstOrDefault(b=>b.Unique&&(b.Active.ContainsKey(element)||b.Queue.Contains(element)))??throw new InvalidOperationException("Control is not a singleton unit in this host.");
        return (bucket.Scene,bucket.Policy);
    }
    internal void Park(ViewInstance owner,Control element)
    {if(!IsOwner(element,owner))return;owner.SuspendElement(element);owner.RestoreElement(element);element.GetParent()?.RemoveChild(element);parking.AddChild(element);element.Hide();}
}

public sealed partial class ViewInstance
{
    internal void AddNested(ViewElementPlacement placement,Control element)
    {nestedPlacements.Add(placement);Elements.Add(placement.Id,element);BindLayout(placement,element);}
    internal void ParkComposedChildren(Control lent)
    {foreach(var child in Elements.Values.Where(child=>child!=lent&&GodotObject.IsInstanceValid(child)&&lent.IsAncestorOf(child)).ToArray())if(Owns(child))pool?.Park(this,child);}
}
