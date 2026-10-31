#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
namespace MacacaGames.ViewSystem;

public partial class ViewSystemEditor
{
    private sealed record AuthoringChoice(string Value,string Caption,string Tooltip,Variant.Type Type=Variant.Type.Nil,bool Enabled=true);
    private readonly List<AuthoringChoice> authoringChoices=new();
    private PopupPanel authoringPopup=null!;
    private ItemList authoringList=null!;
    private LineEdit authoringSearch=null!,parentPath=null!;
    private Control parentRow=null!;
    private string authoringKey="";
    private string locateScene="",locatePath="";
    private Node2D? authoringOutline;
    private void BuildAuthoringPickers(Node items)
    {
        parentRow=new VBoxContainer {Name="PlacementParentRow"};items.AddChild(parentRow);
        parentPath=LabeledField(parentRow,"Parent","ParentPath","相對頁面根節點的父 Control 路徑",".");
        WrapPicker(parentPath,"Parent",()=>Try(ApplyParentPath));
        foreach(var pair in new[]{(target,"Target"),(property,"Property"),(eventTarget,"EventTarget"),(eventSignal,"EventSignal"),(eventAction,"EventAction")})
            WrapPicker(pair.Item1,pair.Item2,null);
        authoringPopup=new PopupPanel {Name="AuthoringPicker"};panel.AddChild(authoringPopup);
        var box=new VBoxContainer();authoringPopup.AddChild(box);
        authoringSearch=Field(box,"PickerSearch","搜尋","");
        ConnectUi(authoringSearch,LineEdit.SignalName.TextChanged,nameof(OnAuthoringSearch));
        authoringList=new ItemList {Name="PickerOptions",CustomMinimumSize=new Vector2(460,360),SizeFlagsVertical=Control.SizeFlags.ExpandFill};box.AddChild(authoringList);
        ConnectUi(authoringList,ItemList.SignalName.ItemActivated,nameof(OnAuthoringChosen));
        Button(box,"選取","ConfirmPicker",()=>{var selected=authoringList.GetSelectedItems();if(selected.Length>0)OnAuthoringChosen(selected[0]);});
    }
    private void WrapPicker(LineEdit field,string key,Action? apply)
    {
        var owner=field.GetParent();int position=field.GetIndex();owner.RemoveChild(field);
        var row=new HBoxContainer {Name=key+"PickerRow"};owner.AddChild(row);owner.MoveChild(row,position);
        field.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(field);
        Button(row,"…","Pick"+key,()=>Try(()=>OpenAuthoringPicker(key)));
        Button(row,"↗","Locate"+key,()=>Try(()=>LocateAuthoringTarget(key)));
        if(apply!=null)Button(row,"✓","Apply"+key,apply);
    }
    private Control? AuthoringRoot()=>previews.FirstOrDefault(p=>p.Definition==entry) is ViewInstance view
        ?SelectedDefinition() is ViewElementPlacement item?view.Elements.GetValueOrDefault(item.Id):view.Root:null;
    private Control? PageAuthoringRoot()=>previews.FirstOrDefault(p=>p.Definition==entry)?.Root;
    private static IEnumerable<Node> PickerNodes(Node root)
    {
        yield return root;
        foreach(var child in root.GetChildren())
            if(child.Name!="_ViewSelectionOutline")foreach(var node in PickerNodes(child))yield return node;
    }
    private static string PathFrom(Node root,Node node)=>ReferenceEquals(root,node)?".":root.GetPathTo(node).ToString();
    private bool IsAvailableParent(Control node)
    {
        if(SelectedDefinition() is not ViewElementPlacement item||entry==null)return false;
        var current=AuthoringRoot();
        if(current!=null&&(node==current||current.IsAncestorOf(node)))return false;
        var page=PageAuthoringRoot();if(page==null)return false;
        int index=entry.Elements.IndexOf(item);
        foreach(var other in entry.Elements.Skip(index+1))
            if(previews.First(p=>p.Definition==entry).Elements.TryGetValue(other.Id,out var later)&&(node==later||later.IsAncestorOf(node)))return false;
        return true;
    }
    private IEnumerable<AuthoringChoice> AuthoringOptions(string key)
    {
        var root=key=="Parent"?PageAuthoringRoot():AuthoringRoot();
        if(key=="EventAction")
        {
            if(catalog==null)yield break;
            if(catalog.Actions.Count>0)
                foreach(var action in catalog.Actions.OrderBy(a=>a.Id,StringComparer.Ordinal))
                    yield return new(action.Id,action.Id,$"{action.HandlerScript?.ResourcePath}:{action.HandlerMethod}",Enabled:action.HasHandlerMethod());
            else
                foreach(var action in catalog.Entries.SelectMany(e=>e.Events.Concat(e.Elements.SelectMany(p=>p.Events))).Select(e=>e.Action).Where(a=>a.Length>0).Distinct().OrderBy(a=>a,StringComparer.Ordinal))
                    yield return new(action,action,"既有 binding；可於 Catalog.Actions 宣告應用程式派送入口。");
            yield break;
        }
        if(root==null)yield break;
        if(key is "Target" or "EventTarget" or "Parent")
        {
            foreach(var node in PickerNodes(root))
            {
                if(key=="Parent"&&(node is not Control control||!IsAvailableParent(control)))continue;
                string path=PathFrom(root,node);int depth=path=="."?0:path.Count(c=>c=='/')+1;
                yield return new(path,new string(' ',depth*2)+(path=="."?root.Name.ToString():node.Name.ToString()),path+" · "+node.GetClass());
            }
            yield break;
        }
        string relative=key=="Property"?target.Text:eventTarget.Text;
        var selected=root.GetNodeOrNull(new NodePath(relative));if(selected==null)yield break;
        if(key=="Property")
            foreach(var choice in PropertyOptions(selected,"",new HashSet<ulong>(),0))yield return choice;
        else if(key=="EventSignal")
            foreach(var signal in selected.GetSignalList().OrderBy(s=>s["name"].AsString(),StringComparer.Ordinal))
            {
                string name=signal["name"].AsString();int count=signal["args"].AsGodotArray().Count;
                yield return new(name,name+(count>0?" (…)":""),count>0?"帶參數 signal 尚未提供 payload binding。":name,Enabled:count==0);
            }
    }
    private IEnumerable<AuthoringChoice> PropertyOptions(GodotObject owner,string prefix,HashSet<ulong> visited,int depth)
    {
        if(depth>3||!visited.Add(owner.GetInstanceId()))yield break;
        foreach(var info in owner.GetPropertyList().OrderBy(i=>i["name"].AsString(),StringComparer.Ordinal))
        {
            string name=info["name"].AsString();var usage=(PropertyUsageFlags)info["usage"].AsInt64();
            if((usage&PropertyUsageFlags.Editor)==0||(usage&PropertyUsageFlags.ReadOnly)!=0)continue;
            var type=(Variant.Type)info["type"].AsInt32();string path=prefix+name;
            bool supported=type is Variant.Type.String or Variant.Type.Int or Variant.Type.Float or Variant.Type.Bool or Variant.Type.Color or Variant.Type.Vector2 or Variant.Type.Object;
            if(supported)yield return new(path,path,type.ToString(),type);
            if(type==Variant.Type.Object&&owner.Get(name).AsGodotObject() is Resource resource)
                foreach(var child in PropertyOptions(resource,path+":",visited,depth+1))yield return child;
        }
        visited.Remove(owner.GetInstanceId());
    }
    public string[] GetAuthoringOptions(string key)=>AuthoringOptions(key).Where(o=>o.Enabled).Select(o=>o.Value).ToArray();
    private void OpenAuthoringPicker(string key)
    {
        authoringKey=key;authoringChoices.Clear();authoringChoices.AddRange(AuthoringOptions(key));authoringSearch.Text="";
        RefreshAuthoringChoices();authoringPopup.PopupCentered(new Vector2I(500,450));authoringSearch.GrabFocus();
    }
    private void OnAuthoringSearch(string text)=>RefreshAuthoringChoices();
    private void RefreshAuthoringChoices()
    {
        authoringList.Clear();
        for(int index=0;index<authoringChoices.Count;index++)
        {
            var choice=authoringChoices[index];
            if(authoringSearch.Text.Length>0&&!(choice.Value+" "+choice.Caption).Contains(authoringSearch.Text,StringComparison.OrdinalIgnoreCase))continue;
            int row=authoringList.AddItem(choice.Caption);authoringList.SetItemMetadata(row,index);authoringList.SetItemTooltip(row,choice.Tooltip);authoringList.SetItemDisabled(row,!choice.Enabled);
        }
    }
    private void OnAuthoringChosen(long row)=>Try(()=>
    {
        var choice=authoringChoices[authoringList.GetItemMetadata((int)row).AsInt32()];if(!choice.Enabled)return;
        switch(authoringKey)
        {
            case "Target":target.Text=choice.Value;break;
            case "EventTarget":eventTarget.Text=choice.Value;break;
            case "EventSignal":eventSignal.Text=choice.Value;break;
            case "EventAction":eventAction.Text=choice.Value;break;
            case "Parent":parentPath.Text=choice.Value;ApplyParentPath();break;
            case "Property":
                property.Text=choice.Value;
                kinds.Select((int)(choice.Type switch{Variant.Type.Int=>ViewValueKind.Integer,Variant.Type.Float=>ViewValueKind.Number,Variant.Type.Bool=>ViewValueKind.Boolean,Variant.Type.Color=>ViewValueKind.Color,Variant.Type.Vector2=>ViewValueKind.Vector2,Variant.Type.Object=>ViewValueKind.Resource,_=>ViewValueKind.Text}));
                var current=AuthoringRoot()!.GetNode(new NodePath(target.Text)).GetIndexed(new NodePath(choice.Value));
                value.Text=choice.Type switch{Variant.Type.Color=>"#"+current.AsColor().ToHtml(),Variant.Type.Vector2=>FormattableString.Invariant($"{current.AsVector2().X},{current.AsVector2().Y}"),Variant.Type.Object=>(current.AsGodotObject() as Resource)?.ResourcePath??"",Variant.Type.Float=>current.AsDouble().ToString(CultureInfo.InvariantCulture),_=>current.AsString()};
                break;
        }
        authoringPopup.Hide();if(authoringKey!="EventAction")HighlightAuthoringTarget(authoringKey);
    });
    private void ApplyParentPath()
    {
        if(SelectedDefinition() is not ViewElementPlacement placement)throw new InvalidOperationException("Select an Element placement.");
        var parent=PageAuthoringRoot()?.GetNodeOrNull<Control>(new NodePath(parentPath.Text));
        if(parent==null||!IsAvailableParent(parent))throw new InvalidOperationException("Parent must exist before this placement and cannot be inside it.");
        if(parent is Container&&placement.Layout!=null)throw new InvalidOperationException("Container owns layout. Use native scene layout before reparenting.");
        Change(placement,"ParentPath",new NodePath(parentPath.Text),placement.ParentPath,"ViewSystem parent");
    }
    private void RefreshAuthoringSelection()
    {
        if(!GodotObject.IsInstanceValid(parentRow))return;
        parentRow.Visible=SelectedDefinition() is ViewElementPlacement;
        parentPath.Text=(SelectedDefinition() as ViewElementPlacement)?.ParentPath.ToString()??".";
    }
    private void HighlightAuthoringTarget(string key)
    {
        if(GodotObject.IsInstanceValid(authoringOutline))authoringOutline!.Free();authoringOutline=null;
        var root=key=="Parent"?PageAuthoringRoot():AuthoringRoot();if(root==null)return;
        string path=key=="Parent"?parentPath.Text:key.StartsWith("Event",StringComparison.Ordinal)?eventTarget.Text:target.Text;
        Node? node=root.GetNodeOrNull(new NodePath(path));bool missing=node==null;
        while(node==null&&path!=".") {int slash=path.LastIndexOf('/');path=slash>=0?path[..slash]:".";node=root.GetNodeOrNull(new NodePath(path));}
        if(node is not Control control)return;
        using var script=ResourceLoader.Load<GDScript>("res://addons/macaca_viewsystem/Editor/selection_outline.gd");
        var frame=(Node2D)script.New().AsGodotObject();frame.Name="_ViewSelectionOutline";frame.ZIndex=(int)RenderingServer.CanvasItemZMax;control.AddChild(frame);
        var color=missing||PreviewError.Length>0?new Color(1,.25f,.2f):new Color(.2f,.7f,1);
        frame.Call("configure",control.Size,color);
        authoringOutline=frame;
        if(missing){PreviewError="Missing target: "+(key.StartsWith("Event",StringComparison.Ordinal)?eventTarget.Text:target.Text);status.Text=PreviewError;}
    }
    private void LocateAuthoringTarget(string key)
    {
        if(key!="EventAction")
        {
            if(AuthoringRoot()!=null){HighlightAuthoringTarget(key);return;}
            var scene=key=="Parent"?entry?.Scene:SelectedDefinition() is ViewElementPlacement placement?placement.Scene:entry?.Scene;
            if(scene==null||scene.ResourcePath.Length==0)throw new InvalidOperationException("No source Scene is available for this target.");
            locateScene=scene.ResourcePath;locatePath=key=="Parent"?parentPath.Text:key.StartsWith("Event",StringComparison.Ordinal)?eventTarget.Text:target.Text;
            EditorInterface.Singleton.OpenSceneFromPath(locateScene);CallDeferred(nameof(LocateSourceNode),0);return;
        }
        var action=catalog?.Actions.FirstOrDefault(a=>a.Id==eventAction.Text);
        if(action?.HandlerScript==null)throw new InvalidOperationException("Declare this Action's HandlerScript/HandlerMethod in Catalog.Actions.");
        if(!action.HasHandlerMethod())throw new InvalidOperationException("Handler method not found: "+action.HandlerMethod);
        EditorInterface.Singleton.GetFileSystemDock().NavigateToPath(action.HandlerScript.ResourcePath);
        EditorInterface.Singleton.EditResource(action);
    }
    private void LocateSourceNode(int attempt)
    {
        if(!GodotObject.IsInstanceValid(panel)||locateScene.Length==0)return;
        var root=EditorInterface.Singleton.GetEditedSceneRoot();
        if(root==null||root.SceneFilePath!=locateScene)
        {if(attempt<20)CallDeferred(nameof(LocateSourceNode),attempt+1);return;}
        string path=locatePath;Node? node=root.GetNodeOrNull(new NodePath(path));
        while(node==null&&path!="."){int slash=path.LastIndexOf('/');path=slash>=0?path[..slash]:".";node=root.GetNodeOrNull(new NodePath(path));}
        if(node!=null){var selection=EditorInterface.Singleton.GetSelection();selection.Clear();selection.AddNode(node);EditorInterface.Singleton.EditNode(node);}
        locateScene="";
    }
    private void ReleaseAuthoringPickers()
    {authoringChoices.Clear();locateScene="";locatePath="";authoringPopup=null!;authoringList=null!;authoringSearch=null!;parentPath=null!;parentRow=null!;authoringOutline=null;}
}
#endif
