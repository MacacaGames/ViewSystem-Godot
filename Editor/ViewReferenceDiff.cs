#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public enum ViewReferenceDifferenceKind { Property,Event,Structure,Layout }
public sealed record ViewReferenceDifference(ViewReferenceDifferenceKind Kind,string Target,string Property,string Before,string After,bool Importable,ViewPropertyOverride? Override=null,ViewEventBinding? Event=null);
/// <summary>Compare a detached snapshot of an edited instance against its effective direct source (including scene inheritance).</summary>
public static class ViewReferenceDiff
{
    private static readonly HashSet<string> ignored=new(StringComparer.Ordinal){"script","name","owner","scene_file_path","unique_name_in_owner","editor_description"};
    private static readonly HashSet<string> layoutProperties=new(StringComparer.Ordinal){"anchor_left","anchor_right","anchor_top","anchor_bottom","offset_left","offset_right","offset_top","offset_bottom","position","size","rotation","scale","pivot_offset","layout_mode","grow_horizontal","grow_vertical"};
    public static IReadOnlyList<ViewReferenceDifference> Compare(Control instance,PackedScene source,RefCounted connectionReader,bool captureLayout=false)
    {
        var baseline=source.Instantiate<Control>();var differences=new List<ViewReferenceDifference>();
        try
        {
            var beforeNodes=PersistedNodes(baseline).ToDictionary(n=>Path(baseline,n));var afterNodes=PersistedNodes(instance).ToDictionary(n=>Path(instance,n));
            foreach(var path in beforeNodes.Keys.Union(afterNodes.Keys))
            {
                beforeNodes.TryGetValue(path,out var before);afterNodes.TryGetValue(path,out var after);
                if(before==null||after==null){differences.Add(new(ViewReferenceDifferenceKind.Structure,path,"Node",before?.GetClass()??"—",after?.GetClass()??"—",false));continue;}
                if(before.GetClass()!=after.GetClass()){differences.Add(new(ViewReferenceDifferenceKind.Structure,path,"Type",before.GetClass(),after.GetClass(),false));continue;}
                if((before.GetScript().AsGodotObject() as Script)?.ResourcePath!=(after.GetScript().AsGodotObject() as Script)?.ResourcePath)differences.Add(new(ViewReferenceDifferenceKind.Structure,path,"script","來源 script","instance script 已替換",false));
                CompareProperties(before,after,path,"",0,new());
                CompareConnections(before,after,path);
            }
            if(captureLayout)differences.Insert(0,new(ViewReferenceDifferenceKind.Layout,".","Layout","來源版面","擷取此 instance 的 anchors／offsets／pivot／transform",true));
            return differences;
        }
        finally{baseline.Free();}
        void CompareProperties(GodotObject before,GodotObject after,string target,string prefix,int depth,HashSet<(ulong,ulong)> visited)
        {
            if(depth>8||!visited.Add((before.GetInstanceId(),after.GetInstanceId())))return;
            var beforeInfo=before.GetPropertyList().GroupBy(p=>p["name"].AsString()).ToDictionary(g=>g.Key,g=>g.First());
            foreach(var info in after.GetPropertyList())
            {
                string name=info["name"].AsString();var usage=(PropertyUsageFlags)info["usage"].AsInt64();
                if(ignored.Contains(name)||(usage&(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage))!=(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage)||(usage&PropertyUsageFlags.ReadOnly)!=0||captureLayout&&target=="."&&prefix.Length==0&&layoutProperties.Contains(name))continue;
                if(!beforeInfo.ContainsKey(name))continue;var original=before.Get(name);var edited=after.Get(name);string property=prefix+name;
                if(Equal(original,edited,new(),0,connectionReader))continue;
                if(original.VariantType==Variant.Type.Object&&edited.VariantType==Variant.Type.Object&&original.AsGodotObject() is Resource first&&edited.AsGodotObject() is Resource second&&first.GetClass()==second.GetClass()&&(first.ResourcePath==second.ResourcePath||second.ResourcePath.Length==0))
                {CompareProperties(first,second,target,property+":",depth+1,visited);continue;}
                bool safe=Portable(edited,new(),0);
                differences.Add(new(ViewReferenceDifferenceKind.Property,target,property,Display(original),Display(edited),safe,safe?new(){Target=target,Property=property,Kind=ViewValueKind.Native,NativeValue=Snapshot(edited)}:null));
            }
        }
        IEnumerable<Godot.Collections.Dictionary> Connections(Node node)=>connectionReader.Call("connections",node).AsGodotArray().Select(v=>v.AsGodotDictionary());
        string ConnectionKey(Godot.Collections.Dictionary c,Node root)
        {var receiver=c["target"].AsGodotObject() as Node;string path=receiver!=null&&(receiver==root||root.IsAncestorOf(receiver))?Path(root,receiver):"external:"+receiver?.GetInstanceId();return c["signal"].AsString()+"|"+path+"|"+c["method"].AsString()+"|"+c["bound"].AsInt32()+"|"+c["unbound"].AsInt32();}
        void CompareConnections(Node before,Node after,string path)
        {
            var original=Connections(before).ToDictionary(c=>ConnectionKey(c,baseline));var edited=Connections(after).ToDictionary(c=>ConnectionKey(c,instance));
            foreach(var key in original.Keys.Union(edited.Keys))
            {
                if(original.ContainsKey(key)&&edited.ContainsKey(key))continue;
                if(!edited.TryGetValue(key,out var c)){differences.Add(new(ViewReferenceDifferenceKind.Event,path,original[key]["signal"].AsString(),"來源連線","instance 已移除；請在繼承 Scene 處理",false));continue;}
                var receiver=c["target"].AsGodotObject() as Node;string method=c["method"].AsString();
                bool valid=receiver!=null&&(receiver==instance||instance.IsAncestorOf(receiver))&&receiver.HasMethod(method)&&baseline.GetNodeOrNull(new NodePath(Path(instance,receiver)))?.HasMethod(method)==true&&c["bound"].AsInt32()==0&&c["unbound"].AsInt32()==0;
                string receiverPath=valid?Path(instance,receiver!):"外部／新增 receiver／bound callable（需自行指定 Action）";
                differences.Add(new(ViewReferenceDifferenceKind.Event,path,c["signal"].AsString(),"—",receiverPath+"::"+method,valid,Event:valid?new(){Target=path,Signal=c["signal"].AsString(),Action=method,Handler=receiverPath+"::"+method,HandlerRelativeToElement=true,NativeHandler=true,ForwardArguments=true}:null));
            }
        }
    }
    private static IEnumerable<Node> PersistedNodes(Node root)
    {
        yield return root;
        // Tool scripts often add unowned display helpers while a scene is open.
        // They are not packed and must not appear as authored structural edits.
        foreach(var child in root.GetChildren())if(child.Owner!=null)
            foreach(var node in PersistedNodes(child))yield return node;
    }
    private static string Path(Node root,Node node)=>node==root?".":root.GetPathTo(node).ToString();
    private static string Display(Variant value)=>value.VariantType==Variant.Type.Object&&value.AsGodotObject() is Resource r?(r.ResourcePath.Length>0?r.ResourcePath:r.GetClass()):value.ToString();
    private static Variant Snapshot(Variant value)=>value.VariantType switch{Variant.Type.Object when value.AsGodotObject() is Resource resource=>resource.Duplicate(true),Variant.Type.Array=>value.AsGodotArray().Duplicate(true),Variant.Type.Dictionary=>value.AsGodotDictionary().Duplicate(true),_=>value};
    private static bool Portable(Variant value,HashSet<ulong> seen,int depth)
    {
        if(depth>16)return false;
        if(value.VariantType is Variant.Type.Callable or Variant.Type.Signal or Variant.Type.Rid)return false;
        if(value.VariantType==Variant.Type.Object)return value.AsGodotObject()==null||value.AsGodotObject() is Resource;
        if(value.VariantType==Variant.Type.Array)return value.AsGodotArray().All(v=>Portable(v,seen,depth+1));
        if(value.VariantType==Variant.Type.Dictionary)return value.AsGodotDictionary().All(p=>Portable(p.Key,seen,depth+1)&&Portable(p.Value,seen,depth+1));return true;
    }
    internal static bool Equivalent(Variant a,Variant b,RefCounted comparer)=>Equal(a,b,new(),0,comparer);
    private static bool Equal(Variant a,Variant b,HashSet<(ulong,ulong)> visited,int depth,RefCounted comparer)
    {
        if(a.VariantType!=b.VariantType||depth>12)return false;
        if(comparer.Call("values_equal",a,b).AsBool())return true;
        if(a.VariantType==Variant.Type.Object&&a.AsGodotObject() is Resource first&&b.AsGodotObject() is Resource second)
        {
            if(first.GetClass()!=second.GetClass())return false;if(first.ResourcePath.Length>0&&second.ResourcePath.Length>0&&first.ResourcePath!=second.ResourcePath)return false;
            if((first.GetScript().AsGodotObject() as Script)?.ResourcePath!=(second.GetScript().AsGodotObject() as Script)?.ResourcePath)return false;
            if(!visited.Add((first.GetInstanceId(),second.GetInstanceId())))return true;
            var firstNames=first.GetPropertyList().Select(p=>p["name"].AsString()).ToHashSet(StringComparer.Ordinal);
            foreach(var info in second.GetPropertyList())
            {
                string name=info["name"].AsString();var usage=(PropertyUsageFlags)info["usage"].AsInt64();
                // Match the authored property surface used by CompareProperties.
                // Storage-only fields include transient native FontFile caches;
                // reading those may call texture getters before the font is warm.
                if(name=="script"||(usage&(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage))!=(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage)||(usage&PropertyUsageFlags.ReadOnly)!=0)continue;
                if(!firstNames.Contains(name))return false;
                if(!Equal(first.Get(name),second.Get(name),visited,depth+1,comparer))return false;
            }
            return true;
        }
        if(a.VariantType==Variant.Type.Array){var x=a.AsGodotArray();var y=b.AsGodotArray();return x.Count==y.Count&&Enumerable.Range(0,x.Count).All(i=>Equal(x[i],y[i],visited,depth+1,comparer));}
        if(a.VariantType==Variant.Type.Dictionary){var x=a.AsGodotDictionary();var y=b.AsGodotDictionary();return x.Count==y.Count&&x.All(p=>y.ContainsKey(p.Key)&&Equal(p.Value,y[p.Key],visited,depth+1,comparer));}return false;
    }
}
#endif
