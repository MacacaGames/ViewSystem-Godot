#if TOOLS
using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

// Native Godot controls; workflow informed by the source NodeInspector and
// Page/State visual editor. No Unity editor implementation is copied.
public partial class ViewSystemEditor
{
    private GraphEdit pageGraph = null!;
    private TabContainer workspaceTabs = null!, inspectorTabs = null!;
    private LineEdit entrySearch = null!, definitionId = null!, definitionState = null!;
    private OptionButton entryFilter = null!, definitionKind = null!;
    private SpinBox definitionOrder = null!;
    private ItemList viewItems = null!, eventBindings = null!;
    private LineEdit eventTarget = null!, eventSignal = null!, eventAction = null!;
    private Label definitionSummary = null!, placementSource = null!, layoutResponsibility = null!;
    private readonly Dictionary<string,SpinBox> layoutFields = new();
    private int selectedEvent = -1;
    private bool graphSync, syncingLayout;
    private VBoxContainer inspectorContent = null!;
    private readonly Dictionary<string,VBoxContainer> inspectorSections = new();
    private Control anchorDiagram = null!;
    private Label previewScale = null!;
    private Control navigationPanel=null!, inspectorPanel=null!;
    private readonly Dictionary<GraphNode,int> graphEntries = new();

    private void CreateUnityWorkflow()
    {
        var outer = new HSplitContainer { SizeFlagsVertical=Control.SizeFlags.ExpandFill, Name="WorkspaceSplit", SplitOffset=215 }; panel.AddChild(outer);
        var navigation = new VBoxContainer { CustomMinimumSize=new Vector2(190,0) }; outer.AddChild(navigation);navigationPanel=navigation;
        navigation.AddChild(new Label { Text="Views" });
        entrySearch=Field(navigation,"EntrySearch","搜尋名稱或 State 群組","");
        ConnectUi(entrySearch,LineEdit.SignalName.TextChanged,nameof(OnEntrySearch));
        entryFilter=new OptionButton { Name="EntryFilter" };navigation.AddChild(entryFilter);
        foreach(var name in new[]{"全部","Page","State","Overlay","Element"}) entryFilter.AddItem(name);
        ConnectUi(entryFilter,OptionButton.SignalName.ItemSelected,nameof(OnEntryFilter));
        entries=new ItemList { Name="Entries",SizeFlagsVertical=Control.SizeFlags.ExpandFill };navigation.AddChild(entries);
        ConnectUi(entries,ItemList.SignalName.ItemSelected,nameof(OnEntrySelected));
        var buttons=new HBoxContainer();navigation.AddChild(buttons);
        Button(buttons,"＋ Scene","AddEntry",AddEntry);Button(buttons,"移除","RemoveEntry",RemoveEntry);
        var body=new HSplitContainer { SizeFlagsHorizontal=Control.SizeFlags.ExpandFill, SplitOffset=850 };outer.AddChild(body);
        workspaceTabs=new TabContainer { Name="WorkspaceTabs",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill };body.AddChild(workspaceTabs);
        var relation=new VBoxContainer { Name="Page・State 關係" };workspaceTabs.AddChild(relation);
        var graphBar=new HBoxContainer();relation.AddChild(graphBar);
        Button(graphBar,"整理節點","ArrangeGraph",ResetGraphLayout);
        pageGraph=new GraphEdit { Name="PageGraph",SizeFlagsVertical=Control.SizeFlags.ExpandFill,RightDisconnects=true };relation.AddChild(pageGraph);
        ConnectUi(pageGraph,GraphEdit.SignalName.NodeSelected,nameof(OnGraphSelected));
        ConnectUi(pageGraph,GraphEdit.SignalName.ConnectionRequest,nameof(OnGraphConnection));
        ConnectUi(pageGraph,GraphEdit.SignalName.DisconnectionRequest,nameof(OnGraphDisconnection));
        ConnectUi(pageGraph,GraphEdit.SignalName.EndNodeMove,nameof(SaveGraphLayout));
        var canvas=new VBoxContainer { Name="畫面預覽" };workspaceTabs.AddChild(canvas);
        var previewBar=new HFlowContainer();canvas.AddChild(previewBar);
        width=Dimension(previewBar,"Width",1080);height=Dimension(previewBar,"Height",1920);
        Button(previewBar,"直式","Portrait",()=>{width.Value=1080;height.Value=1920;});
        Button(previewBar,"橫式","Landscape",()=>{width.Value=1920;height.Value=1080;});
        overlays=new OptionButton { Name="Overlay",CustomMinimumSize=new Vector2(150,0) };previewBar.AddChild(overlays);
        ConnectUi(overlays,OptionButton.SignalName.ItemSelected,nameof(OnOverlaySelected));Button(previewBar,"更新預覽","Refresh",RefreshPreview);
        previewScale=new Label { Name="PreviewScale" };previewBar.AddChild(previewScale);
        Button(previewBar,"專注預覽","FocusPreview",()=>{navigationPanel.Visible=!navigationPanel.Visible;inspectorPanel.Visible=navigationPanel.Visible;FitPreview();});
        selection=new Label { Text="選擇一個 ViewCatalog",AutowrapMode=TextServer.AutowrapMode.WordSmart };canvas.AddChild(selection);
        previewArea=new Control { Name="PreviewArea",SizeFlagsVertical=Control.SizeFlags.ExpandFill,ClipContents=true };canvas.AddChild(previewArea);
        previewContainer=new SubViewportContainer { Name="Preview",Stretch=false };previewArea.AddChild(previewContainer);
        viewport=new SubViewport { Name="Viewport",TransparentBg=false,RenderTargetUpdateMode=SubViewport.UpdateMode.Always,OwnWorld3D=true };previewContainer.AddChild(viewport);
        var inspector=new VBoxContainer { CustomMinimumSize=new Vector2(300,0) };body.AddChild(inspector);inspectorPanel=inspector;
        definitionSummary=new Label { Name="DefinitionSummary",Text="Inspector",AutowrapMode=TextServer.AutowrapMode.WordSmart };inspector.AddChild(definitionSummary);
        inspectorTabs=new TabContainer { Name="InspectorTabs",TabsVisible=false,SizeFlagsVertical=Control.SizeFlags.ExpandFill };inspector.AddChild(inspectorTabs);
        var inspectorScroll=new ScrollContainer { Name="SelectionInspector",HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled };inspectorTabs.AddChild(inspectorScroll);
        inspectorContent=new VBoxContainer {SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};inspectorScroll.AddChild(inspectorContent);
        var basic=InspectorPage("設定");
        definitionId=LabeledField(basic,"ID","DefinitionId","唯一 ID","");
        basic.AddChild(new Label { Text="類型" });
        definitionKind=new OptionButton { Name="DefinitionKind" };foreach(var kind in Enum.GetNames<ViewKind>())definitionKind.AddItem(kind);basic.AddChild(definitionKind);
        basic.AddChild(new Label { Text="State 群組" });definitionState=Field(basic,"DefinitionState","相同群組共用 State","");
        basic.AddChild(new Label { Text="排序（SortOrder）" });definitionOrder=new SpinBox { Name="DefinitionOrder",MinValue=-4096,MaxValue=4096,Step=1 };basic.AddChild(definitionOrder);
        Button(basic,"套用設定","ApplyDefinition",()=>Try(ApplyDefinition));
        Button(basic,"完整定義／動畫（Inspector）","Definition",()=>{if(SelectedDefinition() is Resource resource)EditorInterface.Singleton.EditResource(resource);});
        Button(basic,"來源 Scene（2D 版面）","Source",OpenSource);
        var modelBar=new HBoxContainer();basic.AddChild(modelBar);
        Button(modelBar,"Page Model","PageModel",()=>{if(entry!=null)EditorInterface.Singleton.EditResource(entry.PreviewModel);});
        Button(modelBar,"Shared Model","SharedModel",()=>{if(catalog!=null)EditorInterface.Singleton.EditResource(catalog.SharedPreviewModel);});
        var items=InspectorPage("元件");
        inspectorContent.MoveChild(items.GetParent(),0);
        viewItems=new ItemList { Name="ViewItems",CustomMinimumSize=new Vector2(0,140) };items.AddChild(viewItems);
        ConnectUi(viewItems,ItemList.SignalName.ItemSelected,nameof(OnViewItemSelected));
        elements=new OptionButton { Name="Elements",Visible=false };items.AddChild(elements);
        ConnectUi(elements,OptionButton.SignalName.ItemSelected,nameof(OnElementSelected));
        var itemBar=new HBoxContainer();items.AddChild(itemBar);
        Button(itemBar,"＋ Element","AddElement",AddElement);Button(itemBar,"移除","RemoveElement",RemoveElement);
        Button(itemBar,"↑","ElementUp",()=>MoveElement(-1));Button(itemBar,"↓","ElementDown",()=>MoveElement(1));
        Button(items,"Inspector","InspectElement",()=>{if(SelectedDefinition() is Resource resource)EditorInterface.Singleton.EditResource(resource);});
        placementSource=new Label { Visible=false, Name="PlacementSource",AutowrapMode=TextServer.AutowrapMode.WordSmart };items.AddChild(placementSource);Button(items,"Source","ToggleSource",()=>placementSource.Visible=!placementSource.Visible);
        var layout=InspectorPage("版面");
        var ownership=new HBoxContainer();layout.AddChild(ownership);
        Button(ownership,"獨立版面","CreatePlacementLayout",()=>Try(CreatePlacementLayout));
        Button(ownership,"沿用子場景","ResetPlacementLayout",()=>{if(SelectedDefinition() is ViewElementPlacement item)Change(item,"LayoutData",default,item.LayoutData,"Use native scene layout");});
        layoutResponsibility=new Label { Name="LayoutResponsibility",AutowrapMode=TextServer.AutowrapMode.WordSmart };layout.AddChild(layoutResponsibility);
        BuildRectInspector(layout);
        var raw=new VBoxContainer {Name="RawLayout",Visible=false};layout.AddChild(raw);
        foreach(var field in new[]{"AnchorLeft","AnchorTop","AnchorRight","AnchorBottom","OffsetLeft","OffsetTop","OffsetRight","OffsetBottom","PivotX","PivotY","ScaleX","ScaleY","Rotation","SortOrder","SiblingIndex"})
        {
            var row=new HBoxContainer();raw.AddChild(row);row.AddChild(new Label {Text=field switch {"OffsetLeft"=>"OffsetMin X","OffsetTop"=>"OffsetMin Y","OffsetRight"=>"OffsetMax X","OffsetBottom"=>"OffsetMax Y",_=>field},CustomMinimumSize=new Vector2(135,0)});
            var input=new SpinBox {UpdateOnTextChanged=true,Name="Placement"+field,MinValue=-100000,MaxValue=100000,Step=field is "SortOrder" or "SiblingIndex"?1:0,CustomArrowStep=field is "SortOrder" or "SiblingIndex"?1:.01,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};if(field=="SortOrder"){input.MinValue=RenderingServer.CanvasItemZMin;input.MaxValue=RenderingServer.CanvasItemZMax;}
            if(field=="SiblingIndex")input.MinValue=-1;
            row.AddChild(input);layoutFields.Add(field,input);
            ConnectUi(input,Godot.Range.SignalName.ValueChanged,nameof(OnLiveLayoutChanged));
        }
        Button(raw,"套用","ApplyPlacementLayout",()=>Try(ApplyPlacementLayout));
        Button(layout,"編輯子場景","PlacementScene",OpenSource);
        var overrides=InspectorPage("覆寫");
        rules=new ItemList { Name="Overrides",CustomMinimumSize=new Vector2(0,120) };overrides.AddChild(rules);ConnectUi(rules,ItemList.SignalName.ItemSelected,nameof(OnRuleSelected));
        target=LabeledField(overrides,"Target 節點","Target","節點路徑（. 代表選取項目根節點）",".");
        property=LabeledField(overrides,"Property 屬性","Property","Godot 屬性，例如 text、modulate","text");
        kinds=new OptionButton { Name="ValueKind" };foreach(var kind in Enum.GetNames<ViewValueKind>())kinds.AddItem(kind);overrides.AddChild(kinds);
        value=LabeledField(overrides,"Value 固定值","Value","值；Color 用 #RRGGBBAA；Vector2 用 x,y","");
        modelKey=LabeledField(overrides,"Model key 資料綁定","ModelKey","Model key；留空代表固定值","");
        scopes=new OptionButton { Name="Scope" };foreach(var scope in Enum.GetNames<ViewInjectScope>())scopes.AddItem(scope);overrides.AddChild(scopes);
        var ruleBar=new HBoxContainer();overrides.AddChild(ruleBar);
        Button(ruleBar,"新增","NewOverride",()=>{selectedRule=-1;rules.DeselectAll();target.Text=".";property.Text="text";value.Text="";modelKey.Text="";});
        Button(ruleBar,"套用","ApplyOverride",()=>Try(ApplyRule));Button(ruleBar,"移除","RemoveOverride",RemoveRule);
        var events=InspectorPage("事件");
        eventBindings=new ItemList { Name="EventBindings",CustomMinimumSize=new Vector2(0,140) };events.AddChild(eventBindings);ConnectUi(eventBindings,ItemList.SignalName.ItemSelected,nameof(OnEventSelected));
        eventTarget=LabeledField(events,"Target 節點","EventTarget","Button 相對路徑",".");eventSignal=LabeledField(events,"Signal","EventSignal","零參數 signal，例如 pressed","pressed");eventAction=LabeledField(events,"Action","EventAction","Action，例如 OnPlayButtonClick","");
        var eventBar=new HBoxContainer();events.AddChild(eventBar);
        Button(eventBar,"新增","NewEvent",()=>{selectedEvent=-1;eventBindings.DeselectAll();eventTarget.Text=".";eventSignal.Text="pressed";eventAction.Text="";});
        Button(eventBar,"套用","ApplyEvent",()=>Try(ApplyEvent));Button(eventBar,"移除","RemoveEvent",RemoveEvent);
        status=new Label { Name="Status",AutowrapMode=TextServer.AutowrapMode.WordSmart };panel.AddChild(status);
        // Preview remains the first-use default; the relationship graph is one tab away.
        workspaceTabs.CurrentTab=1;
    }
    private LineEdit LabeledField(Node parent,string label,string name,string hint,string text)
    { parent.AddChild(new Label { Text=label });return Field(parent,name,hint,text); }
    private VBoxContainer InspectorPage(string name)
    {
        var section=new VBoxContainer {Name="Section"+name};inspectorContent.AddChild(section);
        var content=new VBoxContainer {Name="Content",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        Button(section,"▾ "+name,"Fold"+name,()=>content.Visible=!content.Visible);section.AddChild(content);
        inspectorSections[name]=section;return content;
    }

    private void OnEntrySearch(string text)=>RefreshNavigation();
    private void OnEntryFilter(long index)=>RefreshNavigation();
    private void RefreshNavigation()
    {
        entries.Clear();if(catalog==null)return;
        var order=new List<int>();
        var grouped=new HashSet<int>();
        foreach(var state in catalog.Entries.Where(e=>e!=null&&e.Kind==ViewKind.State))
        {
            int si=catalog.Entries.IndexOf(state);order.Add(si);grouped.Add(si);
            foreach(var page in catalog.Entries.Where(e=>e!=null&&e.Kind==ViewKind.Page&&e.State.Length>0&&e.State==state.State))
            {int pi=catalog.Entries.IndexOf(page);if(grouped.Add(pi))order.Add(pi);}
        }
        for(int i=0;i<catalog.Entries.Count;i++)if(grouped.Add(i))order.Add(i);
        foreach(int i in order)
        {
            var item=catalog.Entries[i];if(item==null)continue;
            if(entryFilter.Selected>0 && (int)item.Kind!=entryFilter.Selected-1)continue;
            if(entrySearch.Text.Length>0 && !(item.Id+" "+item.State).Contains(entrySearch.Text,StringComparison.OrdinalIgnoreCase))continue;
            bool child=item.Kind==ViewKind.Page&&item.State.Length>0;
            string group=item.Kind==ViewKind.Page?(child?$"  [State: {item.State}]":"  [無 State]"):item.Kind==ViewKind.State?$"  [{item.State}]":"";
            entries.AddItem($"{(child?"    ↳ ":"")}{item.Kind} · {item.Id}{group}");entries.SetItemMetadata(entries.ItemCount-1,i);
            entries.SetItemTooltip(entries.ItemCount-1,$"{item.Kind}: {item.Id}\nState: {item.State}");
        }
        if(entry!=null)SyncNavigationSelection(catalog.Entries.IndexOf(entry));
    }
    private void SyncNavigationSelection(int index)
    {
        entries.DeselectAll();for(int i=0;i<entries.ItemCount;i++)if(entries.GetItemMetadata(i).AsInt32()==index){entries.Select(i);break;}
    }
    private void OnViewItemSelected(long index){elements.Select((int)index);OnElementSelected(index);}
    private void RefreshInspectorPanels()
    {
        if(definitionSummary==null)return;
        definitionSummary.Text=entry==null?"未選取對象":$"{entry.Id}  /  {(elements.Selected>0?(SelectedDefinition() is ViewElementPlacement selected?(selected.DisplayName.Length>0?selected.DisplayName:selected.Id):"Root"):"Root")}";
        if(inspectorSections.TryGetValue("設定",out var definitionSection))definitionSection.Visible=elements.Selected<=0;
        definitionId.Text=entry?.Id??"";definitionKind.Select((int)(entry?.Kind??ViewKind.Page));definitionState.Text=entry?.State??"";definitionOrder.Value=entry?.SortOrder??0;
        viewItems.Clear();viewItems.AddItem("Root");
        if(entry!=null)foreach(var item in entry.Elements)viewItems.AddItem(item.DisplayName.Length>0?item.DisplayName:item.Id);
        viewItems.CustomMinimumSize=new Vector2(0,Math.Min(140,viewItems.ItemCount*26+8));
        if(elements.Selected>=0 && elements.Selected<viewItems.ItemCount)viewItems.Select(elements.Selected);
        RefreshPlacementLayout();
        RefreshEvents();
    }
    private void RefreshPlacementLayout()
    {
        var placement=SelectedDefinition() as ViewElementPlacement;
        placementSource.Text=placement==null?"":$"來源：{placement.SourceAsset}\nGUID：{placement.SourceGuid}\nfileID：{placement.SourceLocalFileId}\nViewPageItem：{placement.SourceItemId}\n{placement.SourcePageAsset}";
        var layout=placement?.Layout;
        layoutResponsibility.TooltipText=placement==null?"先在「元件」選取放置項目。":layout==null?"此項目的幾何由原生 Container／子場景管理；請在 2D 編輯其原生版面設定。":"此 placement 擁有根節點的 anchors／offsets／pivot。來源項目 PivotX／Y 為 0–1 相對尺寸；進階 Inspector 可切換像素 pivot。子場景內部由 Control／Container 管理。Rotation 使用弧度。";
        syncingLayout=true;
        foreach(var pair in layoutFields)
        {
            pair.Value.Editable=layout!=null;
            pair.Value.SetValueNoSignal(layout==null?0:pair.Key switch
            { "PivotX"=>layout.UseNormalizedPivot?layout.NormalizedPivot.X:layout.PivotOffset.X,"PivotY"=>layout.UseNormalizedPivot?layout.NormalizedPivot.Y:layout.PivotOffset.Y,"ScaleX"=>layout.Scale.X,"ScaleY"=>layout.Scale.Y,"SiblingIndex"=>placement!.SiblingIndex,_=>layout.Get(pair.Key).AsDouble() });
        }
        syncingLayout=false;
        RefreshSmartRect();
        anchorDiagram.Call("set_layout_data",layout?.Data);
        var live=placement==null?null:previews.FirstOrDefault(p=>p.Definition==entry)?.Elements.GetValueOrDefault(placement.Id);
        ((Button)layoutResponsibility.GetParent().FindChild("CreatePlacementLayout",true,false)).Disabled=placement==null||layout!=null||live==null||live.GetParent() is Container;
        ((Button)layoutResponsibility.GetParent().FindChild("ResetPlacementLayout",true,false)).Disabled=layout==null;
        foreach(var preset in anchorPresets.GetChildren().OfType<BaseButton>())preset.Disabled=layout==null;
    }
    private void CreatePlacementLayout()
    {
        if(SelectedDefinition() is not ViewElementPlacement item || item.Layout!=null)return;
        var control=previews.FirstOrDefault(p=>p.Definition==entry)?.Elements.GetValueOrDefault(item.Id)
            ??throw new InvalidOperationException("先修正場景／路徑，使預覽可正常建立。");
        if(control.GetParent() is Container)throw new InvalidOperationException("父 Container 負責排版，請編輯其原生版面。");
        var layout=new ViewElementLayout(new Resource()) {AnchorLeft=control.AnchorLeft,AnchorTop=control.AnchorTop,AnchorRight=control.AnchorRight,AnchorBottom=control.AnchorBottom,
            OffsetLeft=control.OffsetLeft,OffsetTop=control.OffsetTop,OffsetRight=control.OffsetRight,OffsetBottom=control.OffsetBottom,
            PivotOffset=control.PivotOffset,Scale=control.Scale,Rotation=control.Rotation,SortOrder=control.ZIndex};
        Change(item,"LayoutData",layout.Data,default,"Create independent placement layout");
    }
    private void OnAnchorDragged(Vector2 minimum,Vector2 maximum)
    {
        syncingLayout=true;
        layoutFields["AnchorLeft"].SetValueNoSignal(minimum.X);layoutFields["AnchorTop"].SetValueNoSignal(minimum.Y);
        layoutFields["AnchorRight"].SetValueNoSignal(maximum.X);layoutFields["AnchorBottom"].SetValueNoSignal(maximum.Y);
        syncingLayout=false;Try(ApplyPlacementLayout);
    }
    private void OnLiveLayoutChanged(double value)
    {if(!syncingLayout && !HasMeta(ReloadState) && SelectedDefinition() is ViewElementPlacement {Layout:not null})Try(ApplyPlacementLayout);}
    private void ApplyAnchorPreset(int x,int y)
    {
        if(SelectedDefinition() is not ViewElementPlacement {Layout:not null})return;
        var oldMin=new Vector2((float)layoutFields["AnchorLeft"].Value,(float)layoutFields["AnchorTop"].Value);
        var oldMax=new Vector2((float)layoutFields["AnchorRight"].Value,(float)layoutFields["AnchorBottom"].Value);
        var parentSize=SelectedControl()?.GetParentControl()?.Size??new Vector2((float)width.Value,(float)height.Value);
        syncingLayout=true;
        layoutFields["AnchorLeft"].SetValueNoSignal(x==3?0:x*.5);
        layoutFields["AnchorRight"].SetValueNoSignal(x==3?1:x*.5);
        layoutFields["AnchorTop"].SetValueNoSignal(y==3?0:y*.5);
        layoutFields["AnchorBottom"].SetValueNoSignal(y==3?1:y*.5);
        layoutFields["OffsetLeft"].SetValueNoSignal(layoutFields["OffsetLeft"].Value+(oldMin.X-layoutFields["AnchorLeft"].Value)*parentSize.X);
        layoutFields["OffsetRight"].SetValueNoSignal(layoutFields["OffsetRight"].Value+(oldMax.X-layoutFields["AnchorRight"].Value)*parentSize.X);
        layoutFields["OffsetTop"].SetValueNoSignal(layoutFields["OffsetTop"].Value+(oldMin.Y-layoutFields["AnchorTop"].Value)*parentSize.Y);
        layoutFields["OffsetBottom"].SetValueNoSignal(layoutFields["OffsetBottom"].Value+(oldMax.Y-layoutFields["AnchorBottom"].Value)*parentSize.Y);
        syncingLayout=false;ApplyPlacementLayout();anchorPopup.Hide();
    }
    private void ApplyPlacementLayout()
    {
        if(SelectedDefinition() is not ViewElementPlacement placement || placement.Layout is not {} layout || catalog==null)throw new InvalidOperationException("此項目的版面由原生 Container／子場景管理，沒有 placement Layout。");
        bool changed=rectNormalizePending || layoutFields.Any(p=>(float)p.Value.Value!=(float)(p.Key switch {"PivotX"=>layout.UseNormalizedPivot?layout.NormalizedPivot.X:layout.PivotOffset.X,"PivotY"=>layout.UseNormalizedPivot?layout.NormalizedPivot.Y:layout.PivotOffset.Y,"ScaleX"=>layout.Scale.X,"ScaleY"=>layout.Scale.Y,"SiblingIndex"=>placement.SiblingIndex,_=>layout.Get(p.Key).AsDouble()}));
        if(!changed)return;
        var undo=GetUndoRedo();undo.CreateAction("ViewSystem placement layout",customContext:catalog);
        if(rectNormalizePending){undo.ForceFixedHistory();undo.AddDoProperty(layout.Data,"metadata/UseNormalizedPivot",true);undo.ForceFixedHistory();undo.AddUndoProperty(layout.Data,"metadata/UseNormalizedPivot",layout.UseNormalizedPivot);}
        foreach(var pair in layoutFields.Where(p=>p.Key is not "PivotX" and not "PivotY" and not "ScaleX" and not "ScaleY" and not "SiblingIndex"))
        {var value=pair.Key=="SortOrder"?Variant.From((int)pair.Value.Value):Variant.From((float)pair.Value.Value);if((float)pair.Value.Value==(float)layout.Get(pair.Key).AsDouble())continue;undo.ForceFixedHistory();undo.AddDoProperty(layout.Data,"metadata/"+pair.Key,value);undo.ForceFixedHistory();undo.AddUndoProperty(layout.Data,"metadata/"+pair.Key,layout.Get(pair.Key));}
        foreach(var pair in new[]{(layout.UseNormalizedPivot||rectNormalizePending?"NormalizedPivot":"PivotOffset",new Vector2((float)layoutFields["PivotX"].Value,(float)layoutFields["PivotY"].Value)),("Scale",new Vector2((float)layoutFields["ScaleX"].Value,(float)layoutFields["ScaleY"].Value))})
        {if(pair.Item2==layout.Get(pair.Item1).AsVector2())continue;undo.ForceFixedHistory();undo.AddDoProperty(layout.Data,"metadata/"+pair.Item1,pair.Item2);undo.ForceFixedHistory();undo.AddUndoProperty(layout.Data,"metadata/"+pair.Item1,layout.Get(pair.Item1));}
        if((int)layoutFields["SiblingIndex"].Value!=placement.SiblingIndex){undo.ForceFixedHistory();undo.AddDoProperty(placement,"SiblingIndex",(int)layoutFields["SiblingIndex"].Value);undo.ForceFixedHistory();undo.AddUndoProperty(placement,"SiblingIndex",placement.SiblingIndex);}
        undo.AddDoMethod(this,nameof(RefreshLayoutPreview));undo.AddUndoMethod(this,nameof(RefreshLayoutPreview));undo.CommitAction();
    }
    private void RefreshLayoutPreview()
    {
        RefreshPlacementLayout();
        if(SelectedDefinition() is not ViewElementPlacement placement || placement.Layout is not {} layout)return;
        bool applied=false;
        foreach(var instance in previews.Where(p=>p.Definition==entry))
            if(instance.Elements.TryGetValue(placement.Id,out var control))
            {
                layout.Apply(control);
                if(placement.SiblingIndex>=0)control.GetParent().MoveChild(control,Math.Min(placement.SiblingIndex,control.GetParent().GetChildCount()-1));
                applied=true;
            }
        if(!applied)RefreshPreview();
        else lastSignature=catalog==null?"":Signature(catalog);
    }
    private void ApplyDefinition()
    {
        if(entry==null||catalog==null)return;
        string id=definitionId.Text.Trim();if(id.Length==0 || catalog.Entries.Any(e=>e!=entry&&e.Id==id))throw new InvalidOperationException("ID 必須非空且唯一。");
        var undo=GetUndoRedo();undo.CreateAction("ViewSystem definition",customContext:catalog);
        foreach(var change in new[]{("Id",Variant.From(id),Variant.From(entry.Id)),("Kind",Variant.From(definitionKind.Selected),Variant.From((int)entry.Kind)),("State",Variant.From(definitionState.Text.Trim()),Variant.From(entry.State)),("SortOrder",Variant.From((int)definitionOrder.Value),Variant.From(entry.SortOrder))})
        {undo.ForceFixedHistory();undo.AddDoProperty(entry,change.Item1,change.Item2);undo.ForceFixedHistory();undo.AddUndoProperty(entry,change.Item1,change.Item3);}
        undo.AddDoMethod(this,MethodName.RefreshDocument);undo.AddUndoMethod(this,MethodName.RefreshDocument);undo.CommitAction();
    }
    private void MoveElement(int direction)
    {
        if(entry==null)return;int index=elements.Selected-1,nextIndex=index+direction;if(index<0||nextIndex<0||nextIndex>=entry.Elements.Count)return;
        var old=entry.Elements;var next=new Godot.Collections.Array<ViewElementPlacement>(old);(next[index],next[nextIndex])=(next[nextIndex],next[index]);
        var first=old[index];var second=old[nextIndex];
        if(first.ParentPath==second.ParentPath && first.SiblingIndex>=0 && second.SiblingIndex>=0)
        {
            var undo=GetUndoRedo();undo.CreateAction("Reorder ViewSystem element",customContext:catalog);
            undo.ForceFixedHistory();undo.AddDoProperty(entry,"Elements",next);undo.ForceFixedHistory();undo.AddUndoProperty(entry,"Elements",old);
            undo.ForceFixedHistory();undo.AddDoProperty(first,"SiblingIndex",second.SiblingIndex);undo.ForceFixedHistory();undo.AddUndoProperty(first,"SiblingIndex",first.SiblingIndex);
            undo.ForceFixedHistory();undo.AddDoProperty(second,"SiblingIndex",first.SiblingIndex);undo.ForceFixedHistory();undo.AddUndoProperty(second,"SiblingIndex",second.SiblingIndex);
            undo.AddDoMethod(this,MethodName.RefreshDocument);undo.AddUndoMethod(this,MethodName.RefreshDocument);undo.CommitAction();
        }
        else Change(entry,"Elements",next,old,"Reorder ViewSystem element");
        elements.Select(nextIndex+1);RefreshRules();RefreshPreview();
    }
    private Godot.Collections.Array<ViewEventBinding> SelectedEvents()=>SelectedDefinition() switch
    {ViewEntry page=>page.Events,ViewElementPlacement item=>item.Events,_=>new()};
    private void RefreshEvents()
    {
        eventBindings.Clear();foreach(var binding in SelectedEvents())eventBindings.AddItem($"{binding.Target} · {binding.Signal} → {binding.Action}");selectedEvent=-1;
    }
    private void OnEventSelected(long index)
    {
        if(index<0||index>=SelectedEvents().Count)return;selectedEvent=(int)index;var binding=SelectedEvents()[selectedEvent];eventTarget.Text=binding.Target;eventSignal.Text=binding.Signal;eventAction.Text=binding.Action;
    }
    private void ApplyEvent()
    {
        if(SelectedDefinition() is not Resource owner)return;
        if(string.IsNullOrWhiteSpace(eventSignal.Text)||string.IsNullOrWhiteSpace(eventAction.Text))throw new InvalidOperationException("Signal 和 Action 不可留空。");
        var binding=new ViewEventBinding { Target=new NodePath(eventTarget.Text),Signal=eventSignal.Text.Trim(),Action=eventAction.Text.Trim() };
        var old=SelectedEvents();var next=new Godot.Collections.Array<ViewEventBinding>(old);int index=selectedEvent>=0&&selectedEvent<next.Count?selectedEvent:next.Count;
        if(index<next.Count){var source=next[index];binding.SourceScript=source.SourceScript;binding.SourceMethod=source.SourceMethod;binding.SourceTransformPath=source.SourceTransformPath;next[index]=binding;}else next.Add(binding);
        Change(owner,"Events",next,old,"ViewSystem event");selectedEvent=index;eventBindings.Select(index);
    }
    private void RemoveEvent()
    {
        if(SelectedDefinition() is not Resource owner || selectedEvent<0 || selectedEvent>=SelectedEvents().Count)return;
        var old=SelectedEvents();var next=new Godot.Collections.Array<ViewEventBinding>(old);next.RemoveAt(selectedEvent);Change(owner,"Events",next,old,"Remove ViewSystem event");
    }
    private void RebuildGraph()
    {
        if(pageGraph==null)return;graphSync=true;
        try
        {
            pageGraph.ClearConnections();
            foreach(var node in graphEntries.Keys.ToArray()){DisconnectUi(node);node.GetParent()?.RemoveChild(node);node.QueueFree();}graphEntries.Clear();
            if(catalog==null)return;var rows=new int[4];var saved=GraphPositions();
            for(int i=0;i<catalog.Entries.Count;i++)
            {
                var item=catalog.Entries[i];if(item==null)continue;
                int column=item.Kind switch {ViewKind.State=>0,ViewKind.Page=>1,ViewKind.Overlay=>2,_=>3};
                var node=new GraphNode { Name="View_"+i,Title=$"{item.Kind} · {item.Id}",CustomMinimumSize=new Vector2(220,0),PositionOffset=saved.TryGetValue(item.Id,out var position)?position.AsVector2():new Vector2(30+column*290,90+rows[column]*175) };rows[column]++;
                pageGraph.AddChild(node);graphEntries.Add(node,i);
                node.AddChild(new Label { Text=item.State.Length==0?"State：無":("State："+item.State) });
                node.SetSlot(0,item.Kind==ViewKind.Page,0,Colors.CornflowerBlue,item.Kind==ViewKind.State,0,Colors.CornflowerBlue);
                node.AddChild(new Label { Text=$"{item.Elements.Count} 元件 · {item.Events.Count} 事件 · z{item.SortOrder}" });
            }
            foreach(var state in graphEntries.Where(p=>catalog.Entries[p.Value].Kind==ViewKind.State))
                foreach(var page in graphEntries.Where(p=>catalog.Entries[p.Value].Kind==ViewKind.Page))
                    if(catalog.Entries[state.Value].State.Length>0 && catalog.Entries[state.Value].State==catalog.Entries[page.Value].State)
                        pageGraph.ConnectNode(state.Key.Name,0,page.Key.Name,0);
            SyncGraphSelection();
        }
        finally{graphSync=false;}
    }
    private void SyncGraphSelection()
    {
        bool previous=graphSync;graphSync=true;
        try { foreach(var pair in graphEntries)pair.Key.Selected=catalog!=null&&catalog.Entries[pair.Value]==entry; }
        finally { graphSync=previous; }
    }
    private void OnGraphSelected(Node node)
    {if(!graphSync && node is GraphNode graph && graphEntries.TryGetValue(graph,out int index))SelectEntry(index);}
    private (ViewEntry State,ViewEntry Page) GraphPair(StringName from,long fromPort,StringName to,long toPort)
    {
        if(catalog==null||fromPort!=0||toPort!=0)throw new InvalidOperationException("僅支援 State → Page 群組連線。");
        var source=pageGraph.GetNodeOrNull<GraphNode>(new NodePath(from));var destination=pageGraph.GetNodeOrNull<GraphNode>(new NodePath(to));
        if(source==null||destination==null||!graphEntries.TryGetValue(source,out int s)||!graphEntries.TryGetValue(destination,out int p))throw new InvalidOperationException("找不到連線節點。");
        var state=catalog.Entries[s];var page=catalog.Entries[p];if(state.Kind!=ViewKind.State||page.Kind!=ViewKind.Page||state.State.Length==0)throw new InvalidOperationException("請先設定 State 的非空群組，再連到 Page。");
        return(state,page);
    }
    private void OnGraphConnection(StringName from,long fromPort,StringName to,long toPort)=>Try(()=>{var pair=GraphPair(from,fromPort,to,toPort);Change(pair.Page,"State",pair.State.State,pair.Page.State,"Connect ViewSystem state");});
    private void OnGraphDisconnection(StringName from,long fromPort,StringName to,long toPort)=>Try(()=>{var pair=GraphPair(from,fromPort,to,toPort);if(pair.Page.State==pair.State.State)Change(pair.Page,"State","",pair.Page.State,"Disconnect ViewSystem state");});
    private Godot.Collections.Dictionary GraphPositions()
    {
        if(catalog==null)return new();var value=EditorInterface.Singleton.GetEditorSettings().GetProjectMetadata("macaca_viewsystem",catalog.ResourcePath,new Godot.Collections.Dictionary());return value.AsGodotDictionary();
    }
    private void SaveGraphLayout()
    {
        if(catalog==null)return;var positions=new Godot.Collections.Dictionary();foreach(var pair in graphEntries)positions[catalog.Entries[pair.Value].Id]=pair.Key.PositionOffset;
        EditorInterface.Singleton.GetEditorSettings().SetProjectMetadata("macaca_viewsystem",catalog.ResourcePath,positions);
    }
    private void ResetGraphLayout()
    {if(catalog==null)return;EditorInterface.Singleton.GetEditorSettings().SetProjectMetadata("macaca_viewsystem",catalog.ResourcePath,new Godot.Collections.Dictionary());RebuildGraph();}
    private IEnumerable<LineEdit> EditorFormFields()=>new[]{catalogPath,target,property,value,modelKey,definitionId,definitionState,eventTarget,eventSignal,eventAction};
    private void ReleaseUnityWorkflow()
    {
        ReleaseRectInspector();graphEntries.Clear();layoutFields.Clear();inspectorSections.Clear();inspectorContent=null!;anchorDiagram=null!;previewScale=null!;navigationPanel=null!;inspectorPanel=null!;pageGraph=null!;workspaceTabs=null!;inspectorTabs=null!;
        placementSource=null!;layoutResponsibility=null!;
        entrySearch=null!;definitionId=null!;definitionState=null!;entryFilter=null!;definitionKind=null!;definitionOrder=null!;
        viewItems=null!;eventBindings=null!;eventTarget=null!;eventSignal=null!;eventAction=null!;definitionSummary=null!;
    }
}
#endif
