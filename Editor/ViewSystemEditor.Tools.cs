#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private AcceptDialog verifierDialog=null!,orderDialog=null!;
    private ItemList verifierItems=null!,orderItems=null!;
    private readonly List<ViewIssue> verifierIssues=new();
    private readonly List<ViewEntry> orderedOverlays=new();
    private PopupMenu graphMenu=null!;
    private void BuildParityTools(Node toolbar)
    {
        Button(toolbar,"匯入 Unity","ImportUnityAssets",BrowseUnityBundle);
        Button(toolbar,"建立 ViewHost","GenerateHost",()=>Try(GenerateHostScene));
        Button(toolbar,"排序","OverlayOrder",OpenOverlayOrder);
        Button(toolbar,"清除預覽","ClearPreview",()=>{ClearPreview();});
        verifierDialog=new AcceptDialog {Name="ViewVerifier",Title="ViewSystem"};panel.AddChild(verifierDialog);
        var body=new VBoxContainer();verifierDialog.AddChild(body);
        verifierItems=new ItemList {Name="VerifierIssues",CustomMinimumSize=new Vector2(650,280)};body.AddChild(verifierItems);
        var bar=new HFlowContainer();body.AddChild(bar);
        Button(bar,"定位","LocateIssue",LocateIssue);Button(bar,"移除失效項目","RemoveIssue",()=>Try(RemoveIssue));Button(bar,"重新驗證","RefreshIssues",ShowIssues);
        orderDialog=new AcceptDialog {Name="OverlayOrderDialog",Title="Overlay Order"};panel.AddChild(orderDialog);
        var order=new VBoxContainer();orderDialog.AddChild(order);
        orderItems=(ItemList)GD.Load<GDScript>("res://addons/macaca_viewsystem/Editor/overlay_order_list.gd").New().AsGodotObject();orderItems.Name="OverlayOrderItems";orderItems.CustomMinimumSize=new Vector2(350,260);orderItems.TooltipText="上方顯示在前；同時套用繪製與輸入順序。";ConnectUi(orderItems,"item_moved",nameof(OnOverlayDragged));order.AddChild(orderItems);
        var actions=new HFlowContainer();order.AddChild(actions);Button(actions,"↑","OverlayUp",()=>MoveOverlay(-1));Button(actions,"↓","OverlayDown",()=>MoveOverlay(1));Button(actions,"套用","ApplyOverlayOrder",()=>Try(ApplyOverlayOrder));
        graphMenu=new PopupMenu {Name="GraphMenu"};panel.AddChild(graphMenu);
        foreach(var name in new[]{"Add FullPage","Add OverlayPage","Add ViewState","Add Element"})graphMenu.AddItem(name);
        graphMenu.AddSeparator();foreach(var pair in new[]{(100,"重新命名"),(101,"複製"),(102,"移除"),(103,"開啟來源 Scene")})graphMenu.AddItem(pair.Item2,pair.Item1);
        ConnectUi(graphMenu,PopupMenu.SignalName.IdPressed,nameof(OnGraphCreate));ConnectUi(pageGraph,Control.SignalName.GuiInput,nameof(OnGraphContextInput));
    }
    private void GenerateHostScene()
    {
        if(catalog==null)return;var document=catalog;
        var dialog=new EditorFileDialog {Name="HostSceneDialog",FileMode=EditorFileDialog.FileModeEnum.SaveFile,Access=EditorFileDialog.AccessEnum.Resources,Filters=new[]{"*.tscn ; ViewHost"},CurrentFile="ViewHost.tscn"};AddChild(dialog);
        string key=dialog.GetInstanceId().ToString(System.Globalization.CultureInfo.InvariantCulture);
        dialogs.Add(key,(dialog,path=>Try(()=>
        {
            var root=new ViewHost {Name="ViewHost",Catalog=document,IsPrimaryHost=true};root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var scene=new PackedScene();try{if(scene.Pack(root)!=Error.Ok)throw new InvalidOperationException("Cannot pack ViewHost scene.");}finally{root.Free();}
            var error=ResourceSaver.Save(scene,path);if(error!=Error.Ok)throw new InvalidOperationException("Cannot save ViewHost: "+error);
            EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(path);EditorInterface.Singleton.OpenSceneFromPath(path);
        })));
        ConnectUi(dialog,Godot.FileDialog.SignalName.FileSelected,nameof(OnDialogSelected),key);ConnectUi(dialog,Godot.FileDialog.SignalName.Canceled,nameof(OnDialogCanceled),key);dialog.PopupCenteredRatio(.65f);
    }
    private void OnGraphContextInput(InputEvent input)
    {if(input is InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Right} mouse){OnGraphPopup(mouse.Position);pageGraph.AcceptEvent();}}
    private void OnGraphPopup(Vector2 position){foreach(int id in new[]{100,101,102,103})graphMenu.SetItemDisabled(graphMenu.GetItemIndex(id),SelectedDefinition() is not ViewEntry);graphMenu.PopupOnParent(new Rect2I((Vector2I)(pageGraph.GlobalPosition+position),Vector2I.One));}
    private void OnGraphCreate(long index)
    {
        if(index>=100){OnNavigationCommand(index);return;}
        PickEntryScene(index switch {0=>ViewKind.Page,1=>ViewKind.Overlay,2=>ViewKind.State,_=>ViewKind.Element});
    }
    private void ShowIssues()
    {
        if(catalog==null)return;verifierIssues.Clear();verifierIssues.AddRange(ViewVerifier.Verify(catalog));verifierItems.Clear();
        foreach(var issue in verifierIssues)verifierItems.AddItem($"{issue.View}{(issue.Element.Length>0?" / "+issue.Element:"")} · {issue.Field}: {issue.Message}");
        if(verifierIssues.Count==0){status.Text="驗證通過";verifierDialog.Hide();return;}
        verifierDialog.PopupCentered();
    }
    private ViewIssue? SelectedIssue()=>verifierItems.GetSelectedItems() is {Length:>0} selected?verifierIssues[selected[0]]:null;
    private void LocateIssue()
    {
        if(catalog==null||SelectedIssue() is not {} issue)return;
        int index=catalog.Entries.ToList().FindIndex(e=>e.Id==issue.View);
        if(index>=0)
        {
            SelectEntry(index);if(issue.Element.Length>0&&entry!.Elements.FirstOrDefault(e=>e.Id==issue.Element) is {} placement)SelectPlacement(entry,placement);
            if(issue.Item is ViewPropertyOverride rule&&SelectedRules().IndexOf(rule) is int r&&r>=0)LoadRule(r);
            if(issue.Item is ViewEventBinding binding&&SelectedEvents().IndexOf(binding) is int e&&e>=0)OnEventSelected(e);
        }
        EditorInterface.Singleton.EditResource(issue.Item??issue.Owner);
    }
    private void RemoveIssue()
    {
        if(SelectedIssue() is not {} issue)return;
        if(issue.Field=="Overrides"&&issue.Owner.Get("Overrides").AsGodotArray() is {} values)
        {var old=new Godot.Collections.Array<ViewPropertyOverride>(values);var next=new Godot.Collections.Array<ViewPropertyOverride>(old);next.Remove((ViewPropertyOverride)issue.Item!);Change(issue.Owner,"Overrides",next,old,"Remove broken override");}
        else if(issue.Field=="Events"&&issue.Owner.Get("Events").AsGodotArray() is {} bindings)
        {var old=new Godot.Collections.Array<ViewEventBinding>(bindings);var next=new Godot.Collections.Array<ViewEventBinding>(old);next.Remove((ViewEventBinding)issue.Item!);Change(issue.Owner,"Events",next,old,"Remove broken event");}
        ShowIssues();
    }
    private void OpenOverlayOrder()
    {
        if(catalog==null)return;orderedOverlays.Clear();orderedOverlays.AddRange(catalog.Entries.Where(e=>e.Kind==ViewKind.Overlay).OrderByDescending(e=>e.SortOrder));RefreshOverlayOrder();orderDialog.PopupCentered();
    }
    private void RefreshOverlayOrder(){orderItems.Clear();foreach(var item in orderedOverlays)orderItems.AddItem(item.Id);}
    private void MoveOverlay(int direction)
    {
        var selected=orderItems.GetSelectedItems();if(selected.Length==0)return;int index=selected[0],next=index+direction;if(next<0||next>=orderedOverlays.Count)return;
        (orderedOverlays[index],orderedOverlays[next])=(orderedOverlays[next],orderedOverlays[index]);RefreshOverlayOrder();orderItems.Select(next);
    }
    private void OnOverlayDragged(long from,long to)
    {if(from<0||to<0||from>=orderedOverlays.Count||to>=orderedOverlays.Count)return;var item=orderedOverlays[(int)from];orderedOverlays.RemoveAt((int)from);orderedOverlays.Insert((int)to,item);RefreshOverlayOrder();orderItems.Select((int)to);}
    private void ApplyOverlayOrder()
    {
        if(catalog==null)return;var before=CaptureEditorLocation();var undo=GetUndoRedo();undo.CreateAction("ViewSystem Overlay Order",customContext:catalog,markUnsaved:false);
        for(int i=0;i<orderedOverlays.Count;i++){undo.ForceFixedHistory();undo.AddDoProperty(orderedOverlays[i],"SortOrder",orderedOverlays.Count-1-i);undo.ForceFixedHistory();undo.AddUndoProperty(orderedOverlays[i],"SortOrder",orderedOverlays[i].SortOrder);}
        CommitEditorAction(undo,before);orderDialog.Hide();
    }
    private void ReleaseParityTools(){verifierDialog=null!;orderDialog=null!;verifierItems=null!;orderItems=null!;graphMenu=null!;verifierIssues.Clear();orderedOverlays.Clear();}
}
#endif
