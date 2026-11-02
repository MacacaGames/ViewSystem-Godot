#if TOOLS
using Godot;
using System;
using System.Linq;
namespace MacacaGames.ViewSystem;

public partial class ViewSystemEditor
{
    private Button navigationBack=null!,navigationRoot=null!;
    private Label navigationTitle=null!;
    private Control navigationPages=null!,navigationElements=null!;
    private string navigationRootSearch="";
    private int navigationRootFilter=5,navigationGeneration;
    private double navigationRootScroll;
    private bool navigationSync;

    private void BuildNavigationHeader(Node parent)
    {
        var bar=new HBoxContainer();parent.AddChild(bar);
        navigationBack=Button(bar,"← 返回","NavigationBack",BackToViews);
        navigationTitle=new Label {Text="Views"};bar.AddChild(navigationTitle);
        navigationRoot=Button(bar,"","NavigationRoot",SelectNavigationRoot);
        navigationRoot.Flat=true;navigationRoot.ClipText=true;navigationRoot.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        navigationRoot.Disabled=true;navigationRoot.MouseFilter=Control.MouseFilterEnum.Ignore;navigationRoot.FocusMode=Control.FocusModeEnum.None;
    }
    private void BuildNavigationActions(Node parent)
    {
        navigationPages=new HFlowContainer();parent.AddChild(navigationPages);
        var add=new MenuButton{Name="AddEntry",Text="＋ 新增",Flat=false};navigationPages.AddChild(add);
        add.Icon=add.GetThemeIcon("arrow","OptionButton");add.IconAlignment=HorizontalAlignment.Right;
        foreach(var kind in Enum.GetValues<ViewKind>())add.GetPopup().AddItem(kind.ToString(),(int)kind);
        ConnectUi(add.GetPopup(),PopupMenu.SignalName.IdPressed,nameof(OnNewEntryKind));Button(navigationPages,"移除","RemoveEntry",RemoveEntry);
        Button(navigationPages,"複製頁面","DuplicateEntry",()=>Try(DuplicateEntry));
        navigationElements=new VBoxContainer();parent.AddChild(navigationElements);
        var actions=new HBoxContainer();navigationElements.AddChild(actions);
        Button(actions,"＋ Element","AddNavigationElement",()=>AddElementUnder("."));
        Button(actions,"移除","RemoveNavigationElement",RemoveElement);
        Button(actions,"↑","NavigationElementUp",()=>MoveElement(-1));
        Button(actions,"↓","NavigationElementDown",()=>MoveElement(1));
        var references=new HFlowContainer();navigationElements.AddChild(references);
        Button(references,"複製元件","CopyElement",()=>Try(CopyElement));
        Button(references,"使用場景選取","AddSelectedReference",()=>Try(()=>ImportSelectedReference("elements",entries)));

    }
    private void ResetNavigation()
    {
        navigationEntry=null;navigationPlacement=null;navigationGeneration++;
        navigationRootSearch="";navigationRootFilter=5;entrySearch.Text="";entryFilter.Select(5);
    }
    private (ViewEntry Owner,Resource Choice)? NavigationChoice(int row)
    {
        if(catalog==null||row<0||row>=entries.ItemCount)return null;
        int index=entries.GetItemMetadata(row).AsInt32();
        if(navigationEntry is {} owner)
            return index>=0&&index<owner.Elements.Count?(owner,owner.Elements[index]):null;
        return index>=0&&index<catalog.Entries.Count?(catalog.Entries[index],catalog.Entries[index]):null;
    }
    private void QueueNavigationChoice(long row,bool activate)
    {
        if(navigationSync||NavigationChoice((int)row) is not {} choice)return;
        // Scope changes rebuild the same ItemList; finish native input first.
        CallDeferred(nameof(ChooseNavigationItem),choice.Owner,choice.Choice,activate&&navigationEntry==null,navigationGeneration,panel.GetInstanceId());
    }
    private void OnNavigationActivated(long row)=>QueueNavigationChoice(row,true);
    private void ChooseNavigationItem(Resource ownerResource,Resource choice,bool enter,int generation,ulong ui)
    {
        if(!GodotObject.IsInstanceValid(panel)||panel.GetInstanceId()!=ui||generation!=navigationGeneration||catalog==null||ownerResource is not ViewEntry owner||!catalog.Entries.Contains(owner))return;
        if(choice is ViewElementPlacement placement)
        {
            if(navigationEntry!=owner||!owner.Elements.Contains(placement))return;
            SelectNavigationPlacement(owner.Elements.IndexOf(placement));return;
        }
        if(enter)
        {
            navigationRootSearch=entrySearch.Text;navigationRootFilter=entryFilter.Selected;navigationRootScroll=entries.GetVScrollBar().Value;entrySearch.Text="";
            SetEditingSelection(owner,null,true,false);
        }
        else SelectEntry(catalog.Entries.IndexOf(owner));
    }
    private void SelectNavigationPlacement(int index)
    {if(entry!=null&&index>=0&&index<entry.Elements.Count)SelectPlacement(entry,entry.Elements[index],false);}
    private void SelectNavigationRoot()
    {if(navigationEntry!=null)SetEditingSelection(navigationEntry,null,true,false);}
    private void BackToViews()
    {
        if(navigationEntry==null)return;var owner=navigationEntry;
        entrySearch.Text=navigationRootSearch;entryFilter.Select(navigationRootFilter);
        SetEditingSelection(owner,null,false);CallDeferred(nameof(RestoreNavigationScroll),navigationRootScroll,navigationGeneration);
    }
    private void RestoreNavigationScroll(double value,int generation)
    {if(generation!=navigationGeneration||!GodotObject.IsInstanceValid(entries))return;entries.GetVScrollBar().Value=value;entries.EnsureCurrentIsVisible();}
    private void RefreshNavigationScope()
    {
        bool inside=navigationEntry!=null;
        navigationBack.Visible=inside;navigationRoot.Visible=inside;navigationTitle.Visible=!inside;
        navigationRoot.Text=navigationEntry?.Id??"";entryFilter.Visible=!inside;
        entrySearch.PlaceholderText=inside?"搜尋 ViewElement":"搜尋名稱或 State 群組";
        navigationPages.Visible=!inside;navigationElements.Visible=inside;
        saveButton.Disabled=catalog==null;
        if(panel.FindChild("AddEntry",true,false) is Button addPage)addPage.Disabled=catalog==null;
        entries.TooltipText=inside?"拖入 Scene 或 instance 新增元件":"雙擊 Page／State 查看元件；拖入 Scene 或 instance 到項目新增元件";
        if(panel.FindChild("RemoveEntry",true,false) is Button removePage)removePage.Disabled=SelectedDefinition() is not ViewEntry;
        if(panel.FindChild("CopyElement",true,false) is Button copy)copy.Disabled=SelectedDefinition() is not ViewElementPlacement||editing.Placement?.PoolPolicy?.Unique==true;
        if(panel.FindChild("CopyElement",true,false) is Button uniqueCopy)uniqueCopy.TooltipText=editing.Placement?.PoolPolicy?.Unique==true?"Unique elements cannot be duplicated in the same view":"";
        if(panel.FindChild("DuplicateEntry",true,false) is Button duplicate)duplicate.Disabled=SelectedDefinition() is not ViewEntry;
        if(panel.FindChild("RemoveNavigationElement",true,false) is Button remove)remove.Disabled=SelectedDefinition() is not ViewElementPlacement;
        if(panel.FindChild("NavigationElementUp",true,false) is Button up)up.Disabled=SelectedDefinition() is not ViewElementPlacement||elements.Selected<=1;
        if(panel.FindChild("NavigationElementDown",true,false) is Button down)down.Disabled=SelectedDefinition() is not ViewElementPlacement||entry==null||elements.Selected>=entry.Elements.Count;
    }
    private void FillNavigationElements()
    {
        if(navigationEntry==null)return;
        for(int i=0;i<navigationEntry.Elements.Count;i++)
        {
            var item=navigationEntry.Elements[i];if(item==null)continue;
            string name=item.DisplayName.Length>0?item.DisplayName:item.Id;
            if(!(name+" "+item.Id+" "+item.NodeName).Contains(entrySearch.Text,StringComparison.OrdinalIgnoreCase))continue;
            if(navigationEntry.Elements.Count(p=>p!=null&&(p.DisplayName.Length>0?p.DisplayName:p.Id)==name)>1)name+=" · "+item.Id;
            string type="Control";var state=item.Scene?.GetState();if(state!=null&&state.GetNodeCount()>0&&!state.GetNodeType(0).IsEmpty)type=state.GetNodeType(0).ToString();
            int row=entries.AddItem(name,EditorTypeIcon(type));entries.SetItemMetadata(row,i);
            entries.SetItemTooltip(row,item.Id+"\n"+(item.Scene?.ResourcePath??item.ScenePath)+"\nParent: "+item.ParentPath);
        }
    }
    private void ReleaseNavigation()
    {
        navigationEntry=null;navigationPlacement=null;navigationBack=null!;navigationRoot=null!;navigationTitle=null!;
        navigationPages=null!;navigationElements=null!;navigationSync=false;navigationGeneration++;
    }
}
#endif
