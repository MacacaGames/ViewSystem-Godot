#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;

public partial class ViewSystemEditor
{
    private OptionButton breakpointPreview=null!;
    private readonly ViewAuthoringClipboard authoringClipboard=new();
    private readonly Dictionary<ViewLayoutFlags,CheckBox> layoutFlags=new();
    private bool syncingFlags;
    private OptionButton safeAreaPreview=null!;
    private Rect2 PreviewSafeArea()=>safeAreaPreview?.Selected==1?new Rect2(0,.045f,1,.925f):safeAreaPreview?.Selected==2?new Rect2(.045f,0,.925f,1):new Rect2(0,0,1,1);
    private IReadOnlySet<string> PreviewBreakpoints() => new HashSet<string>(
        breakpointPreview!=null && breakpointPreview.Selected>0 ? new[]{breakpointPreview.GetItemText(breakpointPreview.Selected)} : Array.Empty<string>(),StringComparer.Ordinal);
    private ViewContext LayoutContext()=>ViewContext.Empty with {ActiveBreakpoints=PreviewBreakpoints()};
    private ViewElementLayout? SelectedLayout()=>(SelectedDefinition() as ViewElementPlacement)?.ResolveLayout(LayoutContext());
    private Resource? SelectedLayoutOwner()=>SelectedDefinition() is ViewElementPlacement item ? item.ResolveBreakpoint(LayoutContext()) as Resource ?? item : null;
    private void BuildParityToolbar(Node toolbar)
    {
        Button(toolbar,"全域設定","GlobalSettings",()=>{if(catalog!=null)EditorInterface.Singleton.EditResource(catalog);});
        breakpointPreview=new OptionButton {Name="BreakpointPreview",TooltipText="Preview breakpoint; names are configured in Catalog.Breakpoints"};previewToolbar.AddChild(breakpointPreview);
        safeAreaPreview=new OptionButton {Name="SafeAreaPreview",TooltipText="Safe Area simulation; enable global or per-page SafeArea settings to apply."};previewToolbar.AddChild(safeAreaPreview);
        foreach(string name in new[]{"Safe Area: Full","Safe Area: Portrait","Safe Area: Landscape"})safeAreaPreview.AddItem(name);
        ConnectUi(safeAreaPreview,OptionButton.SignalName.ItemSelected,nameof(OnBreakpointSelected));
        breakpointPreview.AddItem("Default");ConnectUi(breakpointPreview,OptionButton.SignalName.ItemSelected,nameof(OnBreakpointSelected));
    }
    private void RefreshParityToolbar()
    {
        if(breakpointPreview==null)return;
        string selected=breakpointPreview.Selected>0?breakpointPreview.GetItemText(breakpointPreview.Selected):"";
        breakpointPreview.Clear();breakpointPreview.AddItem("Default");
        if(catalog!=null)foreach(string name in catalog.Breakpoints)breakpointPreview.AddItem(name);
        for(int i=1;i<breakpointPreview.ItemCount;i++)if(breakpointPreview.GetItemText(i)==selected)breakpointPreview.Select(i);
    }
    private void OnBreakpointSelected(long index){RefreshRules();RefreshPreview();RefreshPlacementLayout();}
    private void BuildParityInspector(Node layout)
    {
        var row=new HFlowContainer();layout.AddChild(row);
        Button(row,"擷取斷點版面","CaptureBreakpoint",()=>Try(CaptureBreakpoint));
        Button(row,"移除斷點版面","RemoveBreakpoint",()=>Try(RemoveBreakpoint));
        var advanced=layout.FindChild("RectAdvanced",true,false)!;
        var flags=new HFlowContainer {Name="LayoutFlags"};advanced.AddChild(flags);
        foreach(var flag in Enum.GetValues<ViewLayoutFlags>().Where(f=>f!=ViewLayoutFlags.All&&f!=ViewLayoutFlags.None))
        {
            var input=new CheckBox {Name="Flag"+flag,Text=flag.ToString()};flags.AddChild(input);layoutFlags.Add(flag,input);
            ConnectUi(input,BaseButton.SignalName.Toggled,nameof(OnLayoutFlagChanged),(int)flag);
        }
        Button(toolsToolbar,"產生 ID 常數","BakeIds",()=>Try(BakeIds));
    }
    private void RefreshParityInspector()
    {
        syncingFlags=true;var layout=SelectedLayout();
        foreach(var pair in layoutFlags){pair.Value.Disabled=layout==null;pair.Value.SetPressedNoSignal(layout!=null&&(layout.Flags&pair.Key)!=0);}
        if(panel.FindChild("CaptureBreakpoint",true,false) is Button capture)capture.Disabled=SelectedDefinition() is not ViewElementPlacement||breakpointPreview.Selected<=0||SelectedControl()==null||SelectedControl()?.GetParent() is Container;
        if(panel.FindChild("RemoveBreakpoint",true,false) is Button remove)remove.Disabled=SelectedDefinition() is not ViewElementPlacement p||breakpointPreview.Selected<=0||!p.Breakpoints.Any(b=>b.Name==breakpointPreview.GetItemText(breakpointPreview.Selected));
        syncingFlags=false;
    }
    private void OnLayoutFlagChanged(bool enabled,int flag)
    {
        if(syncingFlags||SelectedLayout() is not {} layout)return;
        var next=enabled?layout.Flags|(ViewLayoutFlags)flag:layout.Flags&~(ViewLayoutFlags)flag;
        Change(layout.Data,"metadata/Flags",(int)next,(int)layout.Flags,"ViewSystem transform flags");
    }
    private void CaptureBreakpoint()
    {
        if(catalog==null||SelectedDefinition() is not ViewElementPlacement item||breakpointPreview.Selected<=0)throw new InvalidOperationException("先在工具列選擇命名斷點，再選擇元件。");
        string name=breakpointPreview.GetItemText(breakpointPreview.Selected);
        var existing=item.Breakpoints.FirstOrDefault(b=>b.Name==name);
        var live=SelectedControl()??throw new InvalidOperationException("先修正預覽。");
        if(live.GetParent() is Container)throw new InvalidOperationException("Container owns layout.");
        var data=ViewElementLayout.Capture(live).Data;
        if(existing!=null)Change(existing,"LayoutData",data,Variant.From(existing.LayoutData),"Capture breakpoint layout");
        else
        {
            var next=new Godot.Collections.Array<ViewBreakpointLayout>(item.Breakpoints){new ViewBreakpointLayout {Name=name,LayoutData=data}};
            Change(item,"Breakpoints",next,item.Breakpoints,"Add breakpoint layout");
        }
    }
    private void RemoveBreakpoint()
    {
        if(SelectedDefinition() is not ViewElementPlacement item||breakpointPreview.Selected<=0)return;
        var next=new Godot.Collections.Array<ViewBreakpointLayout>(item.Breakpoints.Where(b=>b.Name!=breakpointPreview.GetItemText(breakpointPreview.Selected)));
        Change(item,"Breakpoints",next,item.Breakpoints,"Remove breakpoint layout");
    }
    private void BuildOverridesClipboard(Node parent)
    {
        var row=new HFlowContainer();parent.AddChild(row);
        Button(row,"Copy All","CopyAllOverrides",()=>Try(()=>authoringClipboard.CopyOverrides(SelectedRules())));
        Button(row,"Import","ImportOverrides",()=>FileDialog("*.tres ; ViewOverrideSet",path=>Try(()=>{if(SelectedDefinition() is not Resource owner)return;var source=GD.Load<ViewOverrideSet>(path)??throw new InvalidOperationException("Not a ViewOverrideSet.");var clipboard=new ViewAuthoringClipboard();clipboard.CopyOverrides(source.Values);Change(owner,"Overrides",clipboard.PasteOverrides(),SelectedRules(),"Import override asset");})));
        Button(row,"Export","ExportOverrides",ExportOverrides);
        Button(row,"Paste All","PasteAllOverrides",()=>Try(()=>{if(SelectedDefinition() is Resource owner)Change(owner,"Overrides",authoringClipboard.PasteOverrides(),SelectedRules(),"Paste all overrides");}));
    }
    private void ExportOverrides()
    {
        var clipboard=new ViewAuthoringClipboard();clipboard.CopyOverrides(SelectedRules());var source=new ViewOverrideSet {Values=clipboard.PasteOverrides()};
        var dialog=new EditorFileDialog {FileMode=EditorFileDialog.FileModeEnum.SaveFile,Access=EditorFileDialog.AccessEnum.Resources,Filters=new[]{"*.tres ; ViewOverrideSet"},CurrentFile="Overrides.tres"};AddChild(dialog);
        string key=dialog.GetInstanceId().ToString(System.Globalization.CultureInfo.InvariantCulture);dialogs.Add(key,(dialog,path=>Try(()=>{var error=ResourceSaver.Save(source,path);if(error!=Error.Ok)throw new InvalidOperationException("Cannot save override asset: "+error);EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(path);} )));
        ConnectUi(dialog,Godot.FileDialog.SignalName.FileSelected,nameof(OnDialogSelected),key);ConnectUi(dialog,Godot.FileDialog.SignalName.Canceled,nameof(OnDialogCanceled),key);dialog.PopupCenteredRatio(.65f);
    }
    private void BuildEventsClipboard(Node parent)
    {
        var row=new HFlowContainer();parent.AddChild(row);
        Button(row,"Copy All","CopyAllEvents",()=>Try(()=>authoringClipboard.CopyEvents(SelectedEvents())));
        Button(row,"Paste All","PasteAllEvents",()=>Try(()=>{if(SelectedDefinition() is Resource owner)Change(owner,"Events",authoringClipboard.PasteEvents(),SelectedEvents(),"Paste all events");}));
    }
    private void CopyElement()
    {
        if(entry==null||SelectedDefinition() is not ViewElementPlacement item)return;
        if(item.PoolPolicy?.Unique==true)throw new InvalidOperationException("Unique 元件在同一個 View 只能放置一次；請複製到其他 Page。");
        var clone=(ViewElementPlacement)item.Duplicate(true);clone.Scene=item.Scene;clone.PoolPolicy=item.PoolPolicy;clone.Id=Guid.NewGuid().ToString("N");clone.NodeName="";
        var next=new Godot.Collections.Array<ViewElementPlacement>(entry.Elements){clone};
        Change(entry,"Elements",next,entry.Elements,"Duplicate element");SelectPlacement(entry,clone);RememberMutationSelection();
    }
    private void DuplicateEntry()
    {
        if(catalog==null||entry==null)return;
        var clone=(ViewEntry)entry.Duplicate(true);clone.Scene=entry.Scene;clone.PoolPolicy=entry.PoolPolicy;
        for(int i=0;i<clone.Elements.Count;i++){clone.Elements[i].Scene=entry.Elements[i].Scene;clone.Elements[i].PoolPolicy=entry.Elements[i].PoolPolicy;}
        clone.Id=entry.Id+" Copy";
        while(catalog.Entries.Any(e=>e.Id==clone.Id))clone.Id+="2";
        var next=new Godot.Collections.Array<ViewEntry>(catalog.Entries){clone};
        Change(catalog,"Entries",next,catalog.Entries,"Duplicate view");SelectEntry(next.Count-1);RememberMutationSelection();
    }
    private void BakeIds()
    {
        if(catalog==null)return;
        string path=System.IO.Path.ChangeExtension(catalog.ResourcePath,null)+"Ids.cs";
        var source=ViewIdBaker.Generate(catalog,System.IO.Path.GetFileNameWithoutExtension(path));
        string absolute=ProjectSettings.GlobalizePath(path);
        if(System.IO.File.Exists(absolute)&&!System.IO.File.ReadAllText(absolute).StartsWith("// Generated by ViewSystem.",StringComparison.Ordinal))throw new InvalidOperationException("Refusing to overwrite an authored C# file: "+path);
        System.IO.File.WriteAllText(absolute,source);
        EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(path);status.Text=path;
    }
    private void ReleaseParityWorkflow(){breakpointPreview=null!;safeAreaPreview=null!;layoutFlags.Clear();}
}
#endif
