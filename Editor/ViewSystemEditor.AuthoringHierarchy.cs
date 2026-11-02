#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private Tree? authoringTree,overrideHierarchy,eventHierarchy;
    private readonly Dictionary<string,TreeItem> pickerTreeRows=new(StringComparer.Ordinal);
    private bool bindingTreesSync;
    private static Tree HierarchyTree(string name,float height)
    {
        var tree=new Tree{Name=name,HideRoot=false,HideFolding=false,CustomMinimumSize=new Vector2(0,height),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill,AllowReselect=true};
        tree.AddThemeConstantOverride("draw_relationship_lines",1);tree.AddThemeConstantOverride("item_margin",18);tree.AddThemeConstantOverride("icon_max_width",16);return tree;
    }
    private static Dictionary<string,bool> Folding(Tree tree)
    {
        var state=new Dictionary<string,bool>(StringComparer.Ordinal);
        void Read(TreeItem? item){for(;item!=null;item=item.GetNext()){if(item.HasMeta("path"))state[item.GetMeta("path").AsString()]=item.Collapsed;Read(item.GetFirstChild());}}
        Read(tree.GetRoot());return state;
    }
    private void BuildAuthoringTree(Node box)
    {
        authoringTree=HierarchyTree("PickerTree",360);box.AddChild(authoringTree);authoringTree.CustomMinimumSize=new Vector2(460,360);
        ConnectUi(authoringTree,Tree.SignalName.ItemActivated,nameof(OnAuthoringTreeActivated));
    }
    private void RefreshAuthoringTree()
    {
        if(authoringTree==null)return;bool hierarchy=authoringKey is "Target" or "EventTarget" or "Parent" or "Property" or "EventSignal";
        authoringTree.Columns=authoringKey=="Property"?2:1;
        if(authoringTree.Columns==2){authoringTree.SetColumnExpandRatio(0,2);authoringTree.SetColumnExpandRatio(1,1);}
        authoringTree.Visible=hierarchy;authoringList.Visible=!hierarchy;
        var folding=Folding(authoringTree);authoringTree.Clear();pickerTreeRows.Clear();if(!hierarchy)return;
        if(authoringKey is "Property" or "EventSignal"){FillMemberPickerTree();return;}
        var indexed=authoringChoices.Select((c,i)=>(Choice:c,Index:i)).ToArray();var included=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in indexed.Where(i=>authoringSearch.Text.Length==0||(i.Choice.Value+" "+i.Choice.Caption).Contains(authoringSearch.Text,StringComparison.OrdinalIgnoreCase)))
        {string path=item.Choice.Value;while(true){included.Add(path);if(path==".")break;int slash=path.LastIndexOf('/');path=slash<0?".":path[..slash];}}
        TreeItem Ensure(string path)
        {
            if(pickerTreeRows.TryGetValue(path,out var existing))return existing;
            int slash=path.LastIndexOf('/');var parent=path=="."?null:Ensure(slash<0?".":path[..slash]);var row=authoringTree.CreateItem(parent);pickerTreeRows[path]=row;row.SetMeta("path",path);
            var found=indexed.FirstOrDefault(i=>i.Choice.Value==path);bool exists=found.Choice!=null;
            row.SetText(0,path=="."?indexed.FirstOrDefault(i=>i.Choice.Value==".").Choice?.Caption.Trim()??"Root":path[(slash+1)..]);
            row.SetIcon(0,EditorTypeIcon(exists?found.Choice!.Icon:"Node"));row.SetTooltipText(0,exists?found.Choice!.Tooltip:path);
            row.SetMetadata(0,exists?found.Index:-1);row.SetSelectable(0,exists&&found.Choice!.Enabled);
            row.Collapsed=authoringSearch.Text.Length==0&&folding.GetValueOrDefault(path,false);return row;
        }
        foreach(var item in indexed)if(included.Contains(item.Choice.Value))Ensure(item.Choice.Value);
        string selected=authoringKey=="Parent"?parentPath.Text:authoringKey=="EventTarget"?eventTarget.Text:target.Text;
        if(pickerTreeRows.TryGetValue(selected,out var selectedRow)){selectedRow.Select(0);selectedRow.UncollapseTree();}
    }
    private void FillMemberPickerTree()
    {
        if(authoringTree==null)return;var root=authoringTree.CreateItem();root.SetText(0,authoringKey=="Property"?target.Text:eventTarget.Text);root.SetMetadata(0,-1);root.SetIcon(0,EditorTypeIcon("Node"));
        var groups=new Dictionary<string,TreeItem>();
        foreach(var item in authoringChoices.Select((c,i)=>(Choice:c,Index:i)))
        {
            if(authoringSearch.Text.Length>0&&!item.Choice.Value.Contains(authoringSearch.Text,StringComparison.OrdinalIgnoreCase))continue;
            var parent=root;string prefix="";var parts=item.Choice.Value.Split(':');
            for(int i=0;i<parts.Length-1;i++){prefix+=(prefix.Length>0?":":"")+parts[i];if(!groups.TryGetValue(prefix,out var group)){group=authoringTree.CreateItem(parent);group.SetText(0,parts[i]);group.SetIcon(0,EditorTypeIcon("Resource"));group.SetMetadata(0,-1);groups[prefix]=group;}parent=group;}
            var row=authoringTree.CreateItem(parent);row.SetText(0,parts[^1]);row.SetMetadata(0,item.Index);row.SetIcon(0,EditorTypeIcon(item.Choice.Icon));row.SetTooltipText(0,item.Choice.Tooltip);row.SetSelectable(0,item.Choice.Enabled);
            if(authoringKey=="Property")
            {
                bool modified=SelectedRules().Any(r=>r.Target.ToString()==target.Text&&r.Property==item.Choice.Value);
                row.SetText(0,(modified?"● ":"")+parts[^1]);string summary=PropertySummary(target.Text,item.Choice.Value);row.SetText(1,summary);row.SetTooltipText(1,summary);
                if(modified)row.SetCustomColor(0,new Color(.4f,.75f,1));
            }
        }
    }
    // Changing scope rebuilds these Trees. Native Tree forbids clearing its rows
    // while it is dispatching mouse selection/activation, even though an emitted
    // signal outside GUI input would allow it.
    private void OnAuthoringTreeActivated(){var selected=authoringTree?.GetSelected();if(selected!=null&&selected.GetMetadata(0).AsInt32()>=0)CallDeferred(nameof(ChooseAuthoringTreeChoice),selected.GetMetadata(0).AsInt32(),authoringKey);}
    private void ChooseAuthoringTreeChoice(int index,string key)
    {if(authoringPopup.Visible&&authoringKey==key)ChooseAuthoringChoice(index);}
    private void ConfirmAuthoringChoice()
    {
        if(authoringTree?.Visible==true){OnAuthoringTreeActivated();return;}
        var selected=authoringList.GetSelectedItems();if(authoringKey=="Value"&&authoringChoices.Any(c=>c.ToggleValue))authoringPopup.Hide();else if(selected.Length>0)OnAuthoringChosen(selected[0]);
    }
    private void BuildBindingTree(Node parent,bool events)
    {
        var tree=HierarchyTree(events?"EventsTree":"OverridesTree",140);parent.AddChild(tree);
        tree.Columns=2;tree.SetColumnExpandRatio(0,2);tree.SetColumnExpandRatio(1,1);
        if(events)eventHierarchy=tree;else overrideHierarchy=tree;
        ConnectUi(tree,Tree.SignalName.ItemSelected,nameof(OnBindingTreeSelected),events);
        ConnectUi(tree,Tree.SignalName.ButtonClicked,nameof(OnBindingTreeAdd),events);
        ConnectUi(tree,Tree.SignalName.ItemActivated,nameof(OnBindingActivated),events);
        ConnectUi(tree,Control.SignalName.GuiInput,nameof(OnBindingKey),events);
    }
    private void RefreshBindingTrees()
    {
        if(overrideHierarchy==null||eventHierarchy==null||rules==null||eventBindings==null)return;
        bindingTreesSync=true;try{FillBindings(overrideHierarchy,false);FillBindings(eventHierarchy,true);}finally{bindingTreesSync=false;}
    }
    private void FillBindings(Tree tree,bool events)
    {
        var folding=Folding(tree);tree.Clear();var groups=new Dictionary<string,TreeItem>(StringComparer.Ordinal);var root=AuthoringRoot();
        TreeItem Group(string path)
        {
            if(groups.TryGetValue(path,out var found))return found;int slash=path.LastIndexOf('/');var parent=path=="."?null:Group(slash<0?".":path[..slash]);var item=tree.CreateItem(parent);groups[path]=item;item.SetMeta("path",path);
            var node=root?.GetNodeOrNull(new NodePath(path));item.SetText(0,path=="."?root?.Name.ToString()??"Root":path[(slash+1)..]);item.SetIcon(0,EditorTypeIcon(node?.GetClass()??"Warning"));item.SetTooltipText(0,path);item.SetMetadata(0,-1);
            item.AddButton(0,EditorTypeIcon("Add"),1,false,events?"新增 Event":"新增 Override");item.Collapsed=folding.GetValueOrDefault(path,false);return item;
        }
        Group(".");int count=events?SelectedEvents().Count:SelectedRules().Count;
        for(int i=0;i<count;i++)
        {
            string path=events?SelectedEvents()[i].Target.ToString():SelectedRules()[i].Target.ToString();if(path.Length==0)path=".";
            var row=tree.CreateItem(Group(path));row.SetMeta("path",path+"#"+i);row.SetMetadata(0,i);
            row.SetText(0,events?SelectedEvents()[i].Signal+" → "+(SelectedEvents()[i].Enabled?SelectedEvents()[i].Action:"（未設定）"):SelectedRules()[i].Property+(SelectedRules()[i].IsModelBinding?" ← "+(SelectedRules()[i].ModelExpression.Length>0?SelectedRules()[i].ModelExpression:SelectedRules()[i].ModelKey):""));
            row.SetIcon(0,EditorTypeIcon(events?"Signal":"Edit"));row.SetTooltipText(0,path+":"+(events?SelectedEvents()[i].Signal:SelectedRules()[i].Property));
            string summary=events?SelectedEvents()[i].Handler:PropertySummary(path,SelectedRules()[i].Property);row.SetText(1,summary);row.SetTooltipText(1,summary);
            if(i==(events?selectedEvent:selectedRule)){row.Select(0);row.UncollapseTree();}
        }
    }
    private void OnBindingTreeSelected(bool events)
    {
        if(bindingTreesSync)return;var tree=events?eventHierarchy:overrideHierarchy;var selected=tree?.GetSelected();if(selected==null)return;int index=selected.GetMetadata(0).AsInt32();
        if(index>=0){if(events){if(selectedEvent!=index)OnEventSelected(index);}else if(selectedRule!=index)LoadRule(index);return;}
        // Group rows are navigation only. Existing binding drafts keep their target.
    }
    private void OnBindingTreeAdd(TreeItem item,long column,long id,long button,bool events)
    {if(events)BeginEventAtTarget(item.GetMeta("path").AsString());else BeginOverrideAtTarget(item.GetMeta("path").AsString());}
    private string PropertySummary(string path,string member)
    {
        var root=AuthoringRoot();if(root==null)return "";var owner=OverrideOwner(root,path,member,out _);if(owner==null)return "（不存在）";
        var value=owner.Get(member.Split(':').Last());string text=value.VariantType==Variant.Type.Object?(value.AsGodotObject() is Resource resource?resource.ResourcePath.Length>0?resource.ResourcePath:resource.GetClass():"空"):value.ToString();
        return text.Replace("\n"," ↵ ");
    }
    private void OnBindingActivated(bool events){OnBindingTreeSelected(events);RevealBindingEditor(events);}
    private void OnBindingKey(InputEvent input,bool events)
    {if(input is InputEventKey{Pressed:true,Echo:false} key&&key.Keycode is Key.Delete or Key.Backspace){if((events?eventHierarchy:overrideHierarchy)?.GetSelected()?.GetMetadata(0).AsInt32()>=0){if(events)CallDeferred(nameof(RemoveEvent));else CallDeferred(nameof(RemoveRule));}panel.GetViewport().SetInputAsHandled();}}
    private void SaveAuthoringTreeState(Godot.Collections.Dictionary state)
    {
        if(overrideHierarchy!=null)state["override_folding"]=new Godot.Collections.Dictionary<string,bool>(Folding(overrideHierarchy));
        if(eventHierarchy!=null)state["event_folding"]=new Godot.Collections.Dictionary<string,bool>(Folding(eventHierarchy));
    }
    private void RestoreAuthoringTreeState(Godot.Collections.Dictionary state)
    {
        void Restore(Tree? tree,string name)
        {if(tree==null||!state.TryGetValue(name,out var value))return;var data=value.AsGodotDictionary();void Walk(TreeItem? row){for(;row!=null;row=row.GetNext()){if(row.HasMeta("path")&&data.TryGetValue(row.GetMeta("path"),out var folded))row.Collapsed=folded.AsBool();Walk(row.GetFirstChild());}}Walk(tree.GetRoot());}
        bindingTreesSync=true;
        try
        {
            Restore(overrideHierarchy,"override_folding");Restore(eventHierarchy,"event_folding");
        }
        finally{bindingTreesSync=false;}
    }
    private void ReleaseAuthoringTrees()
    {authoringTree=null;overrideHierarchy=null;eventHierarchy=null;pickerTreeRows.Clear();bindingFlow="";}
}
#endif
