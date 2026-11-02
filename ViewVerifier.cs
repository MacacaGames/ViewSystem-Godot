using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;

public sealed record ViewIssue(string View,string Element,string Field,string Message,Resource Owner,Resource? Item=null);
public static class ViewVerifier
{
    public static IReadOnlyList<ViewIssue> Verify(ViewCatalog catalog)
    {
        var issues=new List<ViewIssue>();
        try{catalog.Validate();}catch(Exception error){issues.Add(new("","","Catalog",error.Message,catalog));}
        foreach(var entry in catalog.Entries)
        {
            if(entry==null)continue;
            ViewInstance instance;
            try
            {
                var clean=(ViewEntry)entry.Duplicate(true);clean.Overrides=new();clean.Events=new();
                foreach(var placement in clean.Elements){placement.Overrides=new();placement.Events=new();}
                instance=ViewComposer.Create(clean,new(ViewModels.From(entry.PreviewModel),ViewModels.From(catalog.SharedPreviewModel),true,(_,_)=>{}));
            }
            catch(Exception error){issues.Add(new(entry.Id,"","Scene / Layout",error.Message,entry));continue;}
            using(instance)
            {
                Check(entry,"",entry,instance.Root,instance.Root,entry.Overrides,entry.Events);
                foreach(var placement in entry.Elements)
                    if(placement!=null&&instance.Elements.TryGetValue(placement.Id,out var node))Check(entry,placement.Id,placement,node,instance.Root,placement.Overrides,placement.Events);
            }
            if(entry.State.Length>0&&entry.Kind!=ViewKind.State&&!catalog.Entries.Any(e=>e.Kind==ViewKind.State&&e.State==entry.State))
                issues.Add(new(entry.Id,"","State","No State definition for group: "+entry.State,entry));
        }
        return issues;
        void Check(ViewEntry entry,string element,Resource owner,Control root,Control pageRoot,IEnumerable<ViewPropertyOverride> rules,IEnumerable<ViewEventBinding> events)
        {
            foreach(var rule in rules)
            {
                try
                {
                    if(rule==null)throw new InvalidOperationException("Empty override.");
                    var probe=new ViewEntry {Id=entry.Id,Scene=entry.Scene,PreviewModel=entry.PreviewModel};
                    var node=root.GetNodeOrNull(rule.Target)??throw new InvalidOperationException("Missing target: "+rule.Target);
                    GodotObject current=node;var path=rule.Property.Split(':');
                    for(int i=0;i<path.Length;i++)
                    {
                        var property=current.GetPropertyList().FirstOrDefault(p=>p["name"].AsString()==path[i])??throw new InvalidOperationException("Unknown property: "+rule.Property);
                        if(i<path.Length-1)current=current.Get(path[i]).AsGodotObject() as Resource??throw new InvalidOperationException("Subproperty requires Resource: "+path[i]);
                        else
                        {
                            var value=rule.Resolve(new(ViewModels.From(entry.PreviewModel),ViewModels.From(catalog.SharedPreviewModel),true));
                            var type=(Variant.Type)property["type"].AsInt32();
                            if(type!=Variant.Type.Nil&&value.VariantType!=type&&!(type==Variant.Type.Float&&value.VariantType==Variant.Type.Int)&&!(type==Variant.Type.Object&&value.VariantType==Variant.Type.Nil))
                                throw new InvalidOperationException($"Expected {type}, got {value.VariantType}.");
                        }
                    }
                }
                catch(Exception error){issues.Add(new(entry.Id,element,"Overrides",error.Message,owner,rule));}
            }
            foreach(var binding in events)
            {
                try
                {
                    if(binding?.Enabled==false)continue;
                    if(binding==null||string.IsNullOrWhiteSpace(binding.Action))throw new InvalidOperationException("Empty action.");
                    var target=root.GetNodeOrNull(binding.Target)??throw new InvalidOperationException("Missing target: "+binding.Target);
                    var signal=target.GetSignalList().FirstOrDefault(s=>s["name"].AsString()==binding.Signal);
                    if(signal==null||signal["args"].AsGodotArray().Count>8)throw new InvalidOperationException("Missing or unsupported signal: "+binding.Signal);
                    if(binding.Handler.Length>0)
                    {
                        int split=binding.Handler.LastIndexOf("::",StringComparison.Ordinal);if(split<1)throw new InvalidOperationException("Handler must be page-relative NodePath::Method.");
                        var handler=(binding.HandlerRelativeToElement?root:pageRoot).GetNodeOrNull(new NodePath(binding.Handler[..split]))??throw new InvalidOperationException("Handler node is missing: "+binding.Handler);
                        bool declared;
#if TOOLS
                        declared=false;for(Type? type=handler.GetType();type!=null;type=type.BaseType)
                            if(type.GetMethods(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly).Any(m=>m.Name==binding.Handler[(split+2)..]&&m.IsDefined(typeof(ViewSystemEventAttribute),false))){declared=true;break;}
#else
                        declared=ViewAttributeBindings.For(handler)?.Actions.Any(a=>a.Method.Name==binding.Handler[(split+2)..])==true;
#endif
                        if(binding.NativeHandler)declared=handler.HasMethod(binding.Handler[(split+2)..]);
                        if(!declared)throw new InvalidOperationException("Handler needs [ViewSystemEvent] or an imported native method: "+binding.Handler);
                    }
                    if(binding.Handler.Length==0&&catalog.Actions.Count>0&&!catalog.Actions.Any(a=>a.Id==binding.Action))throw new InvalidOperationException("Undeclared action: "+binding.Action);
                }
                catch(Exception error){issues.Add(new(entry.Id,element,"Events",error.Message,owner,binding));}
            }
        }
    }
}
