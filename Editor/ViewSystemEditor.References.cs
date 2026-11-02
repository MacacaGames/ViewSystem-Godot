#if TOOLS
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private RefCounted? referenceRouter;
    private AcceptDialog? referenceDialog;
    private Tree? referenceDifferences;
    private ViewCatalog? referenceCatalog;
    private ViewEntry? referencePage;
    private Resource? referenceOwner;
    private PackedScene? referenceScene;
    private Resource? referenceLayout;
    private string referenceParent=".",referenceName="",referenceRole="";
    private IReadOnlyList<ViewReferenceDifference> referenceRows=Array.Empty<ViewReferenceDifference>();
    private void AttachReferenceDrop(Control control,string role)
    {
        if(referenceRouter==null){referenceRouter=(RefCounted)GD.Load<GDScript>("res://addons/macaca_viewsystem/Editor/reference_drop.gd").New().AsGodotObject();referenceRouter.Call("setup",this);}
        referenceRouter.Call("attach",control,role);
    }
    private void BuildReferenceDrop(Node parent,string role,string text)
    {
        var area=new PanelContainer{Name=role=="reference"?"InstanceReferenceDrop":role=="elements"?"ElementReferenceDrop":role=="override"?"OverrideReferenceDrop":"EventReferenceDrop",CustomMinimumSize=new Vector2(0,38)};parent.AddChild(area);
        area.AddChild(new Label{Text=text,MouseFilter=Control.MouseFilterEnum.Ignore,HorizontalAlignment=HorizontalAlignment.Center,AutowrapMode=TextServer.AutowrapMode.WordSmart});AttachReferenceDrop(area,role);
        var choose=Button(parent,"使用場景選取",role=="reference"?"ImportSelectedReference":role=="elements"?"AddSelectedReference":role=="override"?"ImportSelectedOverrideReference":"ImportSelectedEventReference",()=>Try(()=>ImportSelectedReference(role,area)));
        AttachReferenceDrop(choose,role);
    }
    private void ImportSelectedReference(string role,Control area)
    {
        var selected=EditorInterface.Singleton.GetSelection().GetSelectedNodes();
        if(selected.Count!=1)throw new InvalidOperationException("請先在 Scene Tree 選取一個 instance 根節點。");
        var data=new Godot.Collections.Dictionary{{"type","nodes"},{"nodes",new Godot.Collections.Array<NodePath>{selected[0].GetPath()}}};
        OnReferenceDropped(data,role,Vector2.Zero,area);
    }
    private static IEnumerable<Variant> DropItems(Variant input)
    {
        if(input.VariantType==Variant.Type.Array)return input.AsGodotArray();
        if(input.VariantType==Variant.Type.PackedStringArray)return input.AsStringArray().Select(s=>Variant.From(s));return Array.Empty<Variant>();
    }
    private (PackedScene Scene,Control? Instance) ResolveReference(Variant data)
    {
        if(data.VariantType!=Variant.Type.Dictionary)throw new InvalidOperationException("拖入 PackedScene 或本地 Scene instance。");var values=data.AsGodotDictionary();string type=values.TryGetValue("type",out var kind)?kind.AsString():"";
        if(type=="resource"&&values.TryGetValue("resource",out var resource)&&resource.AsGodotObject() is PackedScene packed)return(packed,null);
        if(type=="files"&&values.TryGetValue("files",out var files))
        {var paths=DropItems(files).ToArray();if(paths.Length!=1)throw new InvalidOperationException("請一次拖入一個 Scene。");string path=paths[0].AsString();if(Path.GetExtension(path) is not (".tscn" or ".scn"))throw new InvalidOperationException("需要 PackedScene 檔案。");return(GD.Load<PackedScene>(path),null);}
        if(type=="nodes"&&values.TryGetValue("nodes",out var nodes))
        {
            var paths=DropItems(nodes).ToArray();if(paths.Length!=1)throw new InvalidOperationException("請一次拖入一個 instance 根節點。");var edited=EditorInterface.Singleton.GetEditedSceneRoot()??throw new InvalidOperationException("沒有開啟本地 Scene。");
            var node=paths[0].VariantType==Variant.Type.Object?paths[0].AsGodotObject() as Node:edited.GetNodeOrNull(new NodePath(paths[0].AsString()));
            if(node is not Control control||node!=edited&&!edited.IsAncestorOf(node))throw new InvalidOperationException("需要本地 Scene 中的 Control instance。");
            if(control.SceneFilePath.Length==0)throw new InvalidOperationException("請拖入 instance 根節點；普通節點請先存為 PackedScene。");
            return(ResourceLoader.Load<PackedScene>(control.SceneFilePath,"PackedScene",ResourceLoader.CacheMode.IgnoreDeep),control);
        }
        throw new InvalidOperationException("不支援的 reference；請使用 FileSystem Scene 或本地 Scene instance。");
    }
    private (ViewEntry Page,string Parent) DropDestination(string role,Vector2 position,Control control)
    {
        if(catalog==null)throw new InvalidOperationException("先開啟 Catalog。");ViewEntry? page=entry;string parent=".";
        if(role=="entries")
        {
            if(navigationEntry!=null)page=navigationEntry;
            else{int index=entries.GetItemAtPosition(position,true);if(index<0)throw new InvalidOperationException("拖到 Page／State 項目上。");page=catalog.Entries[entries.GetItemMetadata(index).AsInt32()];}
        }
        else if(role.StartsWith("entry:",StringComparison.Ordinal))page=catalog.Entries[int.Parse(role[6..],System.Globalization.CultureInfo.InvariantCulture)];
        if(page==null||page.Kind is not (ViewKind.Page or ViewKind.State or ViewKind.Overlay or ViewKind.Element))throw new InvalidOperationException("選擇 Page／State。");return(page,parent);
    }
    public bool CanAcceptReference(Variant data,string role,Vector2 position,Control control)
    {
        try
        {
            if(referenceDialog?.Visible==true)return false;var source=ResolveReference(data);var destination=DropDestination(role,position,control);
            if(role is "override" or "event" or "reference")return source.Instance!=null&&SelectedDefinition() is ViewElementPlacement;
            var state=source.Scene.GetState();return state.GetNodeCount()>0&&(state.GetNodeType(0).IsEmpty||ClassDB.IsParentClass(state.GetNodeType(0),"Control"))||source.Instance!=null;
        }
        catch{return false;}
    }
    public void OnReferenceDropped(Variant data,string role,Vector2 position,Control control)=>Try(()=>
    {
        var (scene,instance)=ResolveReference(data);var (page,parent)=DropDestination(role,position,control);referenceCatalog=catalog;referencePage=page;referenceScene=scene;referenceParent=parent;referenceName=instance?.Name.ToString()??"Element";referenceRole=role;referenceOwner=null;referenceLayout=null;
        if(instance==null)
        {
            var root=scene.Instantiate();try{if(root is not Control)throw new InvalidOperationException("Scene 根節點需要 Control。");referenceName=root.Name.ToString();}finally{root.Free();}
            CommitReference(Array.Empty<ViewReferenceDifference>());return;
        }
        bool placement=role is not ("override" or "event" or "reference");
        if(!placement)
        {
            referenceOwner=SelectedDefinition();var selected=referenceOwner switch{ViewEntry definition=>definition.Scene??GD.Load<PackedScene>(definition.ScenePath),ViewElementPlacement item=>item.Scene??GD.Load<PackedScene>(item.ScenePath),_=>null};
            if(selected==null||selected.ResourcePath!=scene.ResourcePath)throw new InvalidOperationException("拖入的 instance 與目前元件來源不同。請拖到元件區新增，或使用相同來源 instance。");
            placement=referenceOwner is ViewElementPlacement;
        }
        referenceLayout=placement?ViewElementLayout.Capture(instance).Data:null;
        referenceRows=ViewReferenceDiff.Compare(instance,scene,referenceRouter!,placement);ShowReferenceDiff();
    });
    private void ShowReferenceDiff()
    {
        if(referenceDialog!=null){DisconnectUi(referenceDialog);referenceDialog.Free();}
        referenceDialog=new AcceptDialog{Name="ReferenceImportDialog",Title=referenceOwner==null?"新增 ViewElement":"匯入 Instance 差異",Exclusive=true,Size=new(900,600)};panel.AddChild(referenceDialog);
        var body=new VBoxContainer();referenceDialog.AddChild(body);body.AddChild(new Label{Text=referenceScene!.ResourcePath+"\n"+(referenceOwner==null?referencePage!.Id+" / "+referenceParent:referenceOwner.GetClass())});
        referenceDifferences=HierarchyTree("ReferenceDifferences",360);referenceDifferences.Columns=3;referenceDifferences.ColumnTitlesVisible=true;referenceDifferences.SetColumnTitle(0,"差異");referenceDifferences.SetColumnTitle(1,"來源");referenceDifferences.SetColumnTitle(2,"Instance");body.AddChild(referenceDifferences);
        var root=referenceDifferences.CreateItem();root.SetText(0,referenceName);root.SetMetadata(0,-1);var groups=new Dictionary<string,TreeItem>();
        ConnectUi(referenceDifferences,Tree.SignalName.ItemEdited,nameof(RefreshReferenceImportCount));
        for(int i=0;i<referenceRows.Count;i++)
        {
            var difference=referenceRows[i];string key=difference.Kind+" / "+difference.Target;if(!groups.TryGetValue(key,out var group)){group=referenceDifferences.CreateItem(root);group.SetText(0,key);group.SetMetadata(0,-1);groups[key]=group;}
            var row=referenceDifferences.CreateItem(group);row.SetText(0,difference.Property);row.SetText(1,difference.Before);row.SetText(2,difference.After);row.SetTooltipText(2,difference.After);row.SetMetadata(0,i);
            if(difference.Importable){row.SetCellMode(0,TreeItem.TreeCellMode.Check);row.SetText(0,difference.Property);row.SetEditable(0,true);row.SetChecked(0,true);}else{row.SetIcon(0,EditorTypeIcon("Warning"));row.SetTooltipText(0,"無法自動匯入，請使用繼承 Scene 或手動指定 Action。");}
        }
        var bar=new HBoxContainer();body.AddChild(bar);Button(bar,"全選","SelectAllReferenceDifferences",()=>SetReferenceChecks(true));Button(bar,"全不選","ClearReferenceDifferences",()=>SetReferenceChecks(false));Button(bar,"取消","CancelReferenceImport",()=>referenceDialog.Hide());
        RefreshReferenceImportCount();ConnectUi(referenceDialog,AcceptDialog.SignalName.Confirmed,nameof(OnConfirmReferenceImport));referenceDialog.PopupCentered(new Vector2I(900,600));
    }
    private IEnumerable<TreeItem> ReferenceDifferenceItems()
    {IEnumerable<TreeItem> Walk(TreeItem? row){for(;row!=null;row=row.GetNext()){yield return row;foreach(var child in Walk(row.GetFirstChild()))yield return child;}}return referenceDifferences==null?Array.Empty<TreeItem>():Walk(referenceDifferences.GetRoot());}
    private void SetReferenceChecks(bool value){foreach(var row in ReferenceDifferenceItems()){int index=row.GetMetadata(0).AsInt32();if(index>=0&&referenceRows[index].Importable)row.SetChecked(0,value);}RefreshReferenceImportCount();}
    private void RefreshReferenceImportCount()
    {if(referenceDialog==null)return;int count=ReferenceDifferenceItems().Count(r=>r.GetMetadata(0).AsInt32()>=0&&r.IsChecked(0));referenceDialog.GetOkButton().Text=referenceOwner==null?$"新增元件並匯入 {count} 項":$"匯入 {count} 項差異";referenceDialog.GetOkButton().Disabled=referenceOwner!=null&&count==0;}
    private void OnConfirmReferenceImport()=>Try(()=>CommitReference(ReferenceDifferenceItems().Where(r=>r.GetMetadata(0).AsInt32()>=0&&r.IsChecked(0)).Select(r=>referenceRows[r.GetMetadata(0).AsInt32()]).Where(r=>r.Importable).ToArray()));
    private void CommitReference(IReadOnlyList<ViewReferenceDifference> selected)
    {
        if(catalog!=referenceCatalog||referencePage==null||!catalog!.Entries.Contains(referencePage))throw new InvalidOperationException("Catalog 已變更，請重新拖入 reference。");
        var overrides=selected.Where(r=>r.Override!=null).Select(r=>(ViewPropertyOverride)r.Override!.Duplicate(true)).ToArray();var events=selected.Where(r=>r.Event!=null).Select(r=>(ViewEventBinding)r.Event!.Duplicate(true)).ToArray();
        if(referenceOwner==null)
        {
            string name=referenceName.Length>0?referenceName:"Element";string id=name;int suffix=2;var preview=ViewComposer.Create(referencePage,new(ViewModels.From(referencePage.PreviewModel),ViewModels.From(catalog.SharedPreviewModel),true,(_,_)=>{}));
            try{var parent=preview.Root.GetNodeOrNull(referenceParent)??throw new InvalidOperationException("目的父節點不存在。");while(referencePage.Elements.Any(e=>e.Id==id)||parent.HasNode(id))id=name+"_"+suffix++;}
            finally{preview.Dispose();}
            var next=new Godot.Collections.Array<ViewElementPlacement>(referencePage.Elements){new(){Id=id,DisplayName=referenceName,NodeName=id,Scene=referenceScene!,ParentPath=referenceParent,LayoutData=selected.Any(r=>r.Kind==ViewReferenceDifferenceKind.Layout)?referenceLayout:null,Overrides=new(overrides),Events=new(events)}};
            Change(referencePage,"Elements",next,referencePage.Elements,"Add ViewElement reference");SelectPlacement(referencePage,next.Last());RememberMutationSelection();
        }
        else
        {
            if(referenceOwner!=referencePage&&(referenceOwner is not ViewElementPlacement ownedPlacement||!referencePage.Elements.Contains(ownedPlacement)))throw new InvalidOperationException("原元件已移除。");
            var previousRules=referenceOwner is ViewEntry page?page.Overrides:((ViewElementPlacement)referenceOwner).Overrides;var previousEvents=referenceOwner is ViewEntry p?p.Events:((ViewElementPlacement)referenceOwner).Events;
            var nextRules=new Godot.Collections.Array<ViewPropertyOverride>(previousRules);foreach(var rule in overrides){int index=nextRules.ToList().FindIndex(r=>r.Target==rule.Target&&r.Property==rule.Property);if(index>=0)nextRules[index]=rule;else nextRules.Add(rule);}
            var nextEvents=new Godot.Collections.Array<ViewEventBinding>(previousEvents);foreach(var binding in events){if(!nextEvents.Any(e=>e.Target==binding.Target&&e.Signal==binding.Signal&&e.Handler==binding.Handler))nextEvents.Add(binding);}
            var before=CaptureEditorLocation();var undo=GetUndoRedo();undo.CreateAction("Import instance differences",customContext:catalog,markUnsaved:false);undo.ForceFixedHistory();undo.AddDoProperty(referenceOwner,"Overrides",nextRules);undo.AddUndoProperty(referenceOwner,"Overrides",previousRules);undo.AddDoProperty(referenceOwner,"Events",nextEvents);undo.AddUndoProperty(referenceOwner,"Events",previousEvents);
            if(referenceOwner is ViewElementPlacement item&&selected.Any(r=>r.Kind==ViewReferenceDifferenceKind.Layout)){undo.AddDoProperty(item,"LayoutData",Variant.From(referenceLayout));undo.AddUndoProperty(item,"LayoutData",Variant.From(item.LayoutData));}
            CommitEditorAction(undo,before);
        }
        referenceDialog?.Hide();referenceRows=Array.Empty<ViewReferenceDifference>();referenceLayout=null;
    }
    private void ReleaseReferenceUi()
    {referenceRouter?.Call("clear");referenceRouter?.Dispose();referenceRouter=null;referenceDialog=null;referenceDifferences=null;referenceRows=Array.Empty<ViewReferenceDifference>();referenceLayout=null;referenceScene=null;referenceOwner=null;referencePage=null;referenceCatalog=null;}
}
#endif
