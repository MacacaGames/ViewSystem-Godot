#if TOOLS
using Godot;
using System.Globalization;
using System.Text;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

[Tool]
public partial class ViewSystemEditor : EditorPlugin, ISerializationListener
{
    private ViewIdInspectorPlugin? idInspector;
    private EditorInspectorPlugin? controlRectInspector;
    private VBoxContainer panel = null!;
    private ItemList entries = null!, rules = null!;
    private OptionButton elements = null!, overlays = null!, kinds = null!, scopes = null!;
    private LineEdit catalogPath = null!, target = null!, property = null!, value = null!, modelKey = null!;
    private HFlowContainer toolsToolbar=null!;
    private Button saveButton=null!;
    private Label status = null!, selection = null!;
    private Control previewArea = null!;
    private SubViewportContainer previewContainer = null!;
    private SubViewport viewport = null!;
    private Control? previewRoot;
    private readonly List<ViewInstance> previews = new();
    private int selectedRule = -1;
    private string lastSignature = "", savedSignature = "";
    private double elapsed;
    private bool rebuilding;
    private RefCounted? nativeBinder;
    private const string ReloadState = "_macaca_viewsystem_editor_reload";
    private readonly Dictionary<string, Action> buttonActions = new();
    private readonly Dictionary<string, (EditorFileDialog Dialog, Action<string> Selected)> dialogs = new();
    private readonly Dictionary<string, (ConfirmationDialog Dialog, Action Continue)> switchDialogs = new();
    public string PreviewError { get; private set; } = "";
    public ViewCatalog? Document => catalog;
    public Control? PreviewRoot => previewRoot;
    public string GetPreviewError() => PreviewError;
    public Error CapturePreviewPng(string path) => viewport.GetTexture().GetImage().SavePng(path);
    public void SetPreviewSize(int pixelWidth, int pixelHeight)
    {
        SelectPreviewSize(new(pixelWidth,pixelHeight));
    }
    public int ValidateAllDefinitions()
    {
        if (catalog == null) throw new InvalidOperationException("No catalog open.");
        catalog.Validate(); int count = 0;
        foreach (var definition in catalog.Entries)
        {
            using var instance = ViewComposer.Create(definition,
                new ViewContext(ViewModels.From(definition.PreviewModel), ViewModels.From(catalog.SharedPreviewModel), true, (_, _) => { }));
            count++;
        }
        return count;
    }
    public override string _GetPluginName() => "ViewSystem";
    public override bool _HasMainScreen() => !IsAutomatedEditorExit();
    public override Texture2D _GetPluginIcon() => EditorInterface.Singleton.GetEditorTheme().GetIcon("Control", "EditorIcons");
    public override void _MakeVisible(bool visible) { if (panel != null) panel.Visible = visible; }
    public override bool _Handles(GodotObject obj) => obj is ViewCatalog;
    public override void _Edit(GodotObject obj) { if (obj is ViewCatalog document && document != catalog) RequestSwitch(() => OpenDocument(document)); }
    public override void _EnterTree()
    {
        if (HasMeta(ReloadState)) CallDeferred(nameof(RestoreEditorAfterReload));
        else CreateEditorUi();
    }
    private static bool IsAutomatedEditorExit()=>OS.GetCmdlineArgs().Any(arg=>arg is "--quit" or "--import" || arg.StartsWith("--export-",StringComparison.Ordinal));
    private void CreateEditorUi()
    {
        // Import/export must register scripts and export plugins, but has no
        // interactive workspace. Never load product previews into its renderer.
        if(IsAutomatedEditorExit())return;
        if (GodotObject.IsInstanceValid(panel)) return;
        AddToGroup("macaca_viewsystem_editor");
        idInspector=new ViewIdInspectorPlugin {OpenedCatalog=()=>catalog};AddInspectorPlugin(idInspector);
        controlRectInspector=(EditorInspectorPlugin)GD.Load<GDScript>("res://addons/macaca_viewsystem/Editor/ControlRect/inspector.gd").New().AsGodotObject();
        controlRectInspector.Set("history",GetUndoRedo());AddInspectorPlugin(controlRectInspector);
        panel = new VBoxContainer { Name = "MacacaViewSystem", SizeFlagsHorizontal=Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        EditorInterface.Singleton.GetEditorMainScreen().AddChild(panel);
        var toolbar = new HFlowContainer(); panel.AddChild(toolbar);
        catalogPath = new LineEdit { Name = "CatalogPath", PlaceholderText = "res://…/ViewCatalog.tres", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        toolbar.AddChild(catalogPath);
        Button(toolbar, "開啟", "Open", () => RequestSwitch(() => OpenCatalog(catalogPath.Text)));
        Button(toolbar, "展開工作區", "FocusWorkspace", () => EditorInterface.Singleton.DistractionFreeMode = !EditorInterface.Singleton.DistractionFreeMode);
        Button(toolbar, "選擇檔案", "Browse", BrowseCatalog);
        string productPath=ProjectSettings.GetSetting("macaca_viewsystem/catalog", "").AsString();
        if(productPath.Length>0)Button(toolbar, "專案 Catalog", "Product", () => RequestSwitch(() => OpenCatalog(productPath)));
        
        Button(toolbar,"套件範例","Demo",()=>RequestSwitch(()=>OpenCatalog("res://addons/macaca_viewsystem/Examples/Catalog.tres")));
        saveButton=Button(toolbar, "儲存", "Save", () => Try(SaveDocument));
        Button(toolbar, "重載", "Reload", () => RequestSwitch(() => OpenCatalog(catalogPath.Text, true)));
        Button(toolbar, "Undo", "Undo", UndoDocument);
        Button(toolbar, "Redo", "Redo", RedoDocument);
        Button(toolbar, "驗證全部", "Validate", ValidateDocument);
        Button(toolbar, "新增 Catalog", "NewCatalog", CreateCatalog);
        toolsToolbar=new HFlowContainer {Name="WorkspaceTools",Visible=false};panel.AddChild(toolsToolbar);
        Button(toolbar,"⋯","Tools",()=>toolsToolbar.Visible=!toolsToolbar.Visible);
        CreateUnityWorkflow();
        BuildParityToolbar(toolsToolbar);
        BuildParityTools(toolsToolbar);
        BuildDiagnosticsTools(toolsToolbar);
        panel.Hide();
        var defaultPath = productPath;
        if (!HasMeta(ReloadState) && defaultPath.Length>0 && ResourceLoader.Exists(defaultPath)) Try(() => OpenCatalog(defaultPath));
        ConnectUi(this, EditorPlugin.SignalName.SceneSaved, nameof(OnSceneSaved));
        SetProcess(true);
    }
    private Button Button(Node parent, string text, string name, Action action)
    {
        var button = new Button { Text = text, Name = name }; parent.AddChild(button);
        string key = button.GetInstanceId().ToString(CultureInfo.InvariantCulture);
        buttonActions.Add(key, action);
        ConnectUi(button, BaseButton.SignalName.Pressed, nameof(OnButtonPressed), key);
        return button;
    }
    // Native method-name callables survive C# reload without serializing captured
    // delegates. Transient UI is still disconnected/freed before assembly unload.
    private void ConnectUi(GodotObject owner, StringName signal, string method, params Variant[] arguments)
    {
        if (nativeBinder == null)
        {
            using var script = ResourceLoader.Load<GDScript>("res://addons/macaca_viewsystem/Editor/native_callable.gd");
            nativeBinder = (RefCounted)script.New().AsGodotObject();
        }
        using var values = new Godot.Collections.Array(arguments);
        var error = (Error)nativeBinder.Call("connect_native", owner, signal, this, method, values).AsInt32();
        if (error != Error.Ok) throw new InvalidOperationException($"Cannot bind editor UI {signal}: {error}");
    }

    private void OnButtonPressed(string key) => buttonActions[key]();
    private void OnEntrySelected(long index) => QueueNavigationChoice(index,false);
    private void OnElementSelected(long index)
    {
        if(entry==null)return;
        if(index>0&&index<=entry.Elements.Count)SelectPlacement(entry,entry.Elements[(int)index-1],false);
        else SetEditingSelection(entry,null,navigationEntry!=null,navigationEntry==null);
    }
    private void OnRuleSelected(long index) => LoadRule((int)index);
    private void OnOverlaySelected(long index) => RefreshPreview();
    private void OnSceneSaved(string path) => RefreshPreview();
    private static LineEdit Field(Node parent, string name, string hint, string text)
    {
        var field = new LineEdit { Name = name, PlaceholderText = hint, TooltipText = hint, Text = text }; parent.AddChild(field); return field;
    }
    private void Try(Action action)
    {
        try { action(); } catch (Exception error) { AuthoringError = error.Message; if (GodotObject.IsInstanceValid(status)) status.Text = "錯誤：" + error.Message; else GD.PushError(error.ToString()); }
    }
    private void RequestSwitch(Action action)
    {
        if (catalog == null || (Signature(catalog) == savedSignature&&!HasPendingDrafts())) { Try(action); return; }
        var dialog = new ConfirmationDialog { Title = "ViewSystem 尚未儲存", DialogText = "儲存目前變更後繼續？", OkButtonText = "儲存並繼續" };
        AddChild(dialog);
        dialog.AddButton("捨棄變更", true, "discard");
        string key = dialog.GetInstanceId().ToString(CultureInfo.InvariantCulture);
        switchDialogs.Add(key, (dialog, action));
        ConnectUi(dialog, ConfirmationDialog.SignalName.Confirmed, nameof(OnSwitchConfirmed), key);
        ConnectUi(dialog, ConfirmationDialog.SignalName.CustomAction, nameof(OnSwitchCustomAction), key);
        ConnectUi(dialog, ConfirmationDialog.SignalName.Canceled, nameof(OnSwitchCanceled), key);
        ConnectUi(dialog, Window.SignalName.CloseRequested, nameof(OnSwitchCanceled), key);
        dialog.PopupCentered();
    }
    private void OnSwitchConfirmed(string key)
    {
        var action = switchDialogs[key].Continue;
        try { Try(() => { SaveDocument(); action(); }); } finally { CloseSwitchDialog(key); }
    }
    private void OnSwitchCustomAction(StringName actionName, string key)
    {
        if (actionName != "discard") return;
        var action = switchDialogs[key].Continue;
        try { Try(() => { OpenCatalog(catalog!.ResourcePath, true); action(); }); } finally { CloseSwitchDialog(key); }
    }
    private void OnSwitchCanceled(string key) => CloseSwitchDialog(key);
    private void CloseSwitchDialog(string key)
    {
        if (!switchDialogs.Remove(key, out var state)) return;
        DisconnectUi(state.Dialog);
        if (GodotObject.IsInstanceValid(state.Dialog)) state.Dialog.QueueFree();
    }
    public void OpenCatalog(string path, bool reload = false)
    {
        try
        {
            if(!ResourceLoader.Exists(path))throw new InvalidOperationException("Catalog not found: "+path);
            var document=ResourceLoader.Load<ViewCatalog>(path,cacheMode:reload?ResourceLoader.CacheMode.ReplaceDeep:ResourceLoader.CacheMode.Reuse)
                ??throw new InvalidOperationException("Resource is not a ViewCatalog.");
            OpenDocument(document);
        }
        catch {catalog=null;ResetNavigation();ClearEditingSelection(false);throw;}
    }
    private void OpenDocument(ViewCatalog document)
    {
        ClearDraftStorage(!HasMeta(ReloadState));
        document.RetainAuthoredGraph();catalog=document;ResetNavigation();catalogPath.Text=document.ResourcePath;savedSignature=Signature(document);
        SetEditingSelection(null,null,false,false);RefreshEntries();
        if(catalog.Entries.Count>0)SelectEntry(0);
    }

    private void RefreshEntries()
    {
        RefreshParityToolbar();
        RefreshNavigation();
        var selectedOverlay=overlays.Selected>0?overlays.GetItemMetadata(overlays.Selected).AsString():"";
        overlays.Clear(); overlays.AddItem("Overlay");
        if (catalog != null)
            foreach (var item in catalog.Entries)
                if (item?.Kind == ViewKind.Overlay) { overlays.AddItem(item.Id); overlays.SetItemMetadata(overlays.ItemCount - 1, item.Id); }
        for(int i=1;i<overlays.ItemCount;i++)if(overlays.GetItemMetadata(i).AsString()==selectedOverlay)overlays.Select(i);
        RebuildGraph();
    }
    public void SelectEntry(int index)
    {
        if(catalog==null||index<0||index>=catalog.Entries.Count)return;
        if(navigationEntry!=null){entrySearch.Text=navigationRootSearch;entryFilter.Select(navigationRootFilter);}
        SetEditingSelection(catalog.Entries[index],null,false);
        if(entries.GetSelectedItems().Length==0){entrySearch.Text="";if(entryFilter.Selected>0&&entryFilter.Selected!=5&&(int)entry!.Kind!=entryFilter.Selected-1||entryFilter.Selected==5&&entry!.Kind is not (ViewKind.Page or ViewKind.State))entryFilter.Select(0);RefreshNavigation();}
        entries.EnsureCurrentIsVisible();
    }
    private global::Godot.Collections.Array<ViewPropertyOverride> SelectedRules() => SelectedDefinition() switch
    {
        ViewEntry definition => definition.Overrides,
        ViewElementPlacement placement => placement.Overrides,
        _ => new()
    };
    private void RefreshRules()
    {
        CaptureCurrentDraft();ReconcileBindingSelection();RefreshInspectorPanels();
        rules.Clear(); foreach (var rule in SelectedRules()) rules.AddItem(rule == null ? "（空白）" : $"{rule.Target}:{rule.Property}" + (rule.ModelKey.Length > 0 ? " ← " + rule.ModelKey : ""));
        rules.FixedIconSize=new Vector2I(16,16);RefreshOverrideIcons();RefreshAuthoringIcons();RefreshBindingTrees();RefreshPersistedForms();RestoreVisibleDrafts();
    }
    private void LoadRule(int index)
    {
        if(autoForm=="override"&&selectedRule!=index)FlushAutoForm(true);
        CaptureCurrentDraft();var list = SelectedRules(); if (index < 0 || index >= list.Count || list[index] == null) return;
        selectedRule = index; var rule = list[index];selectedRuleResource=rule; target.Text = rule.Target.ToString(); property.Text = rule.Property;
        kinds.Select((int)rule.Kind); scopes.Select((int)rule.Scope); modelKey.Text = rule.ModelExpression.Length>0?rule.ModelExpression:rule.ModelKey;
        value.Text = rule.Kind switch
        {
            ViewValueKind.Color => "#" + rule.Color.ToHtml(),
            ViewValueKind.Vector2 => FormattableString.Invariant($"{rule.Vector.X},{rule.Vector.Y}"),
            ViewValueKind.Vector3=>FormattableString.Invariant($"{rule.Vector3.X},{rule.Vector3.Y},{rule.Vector3.Z}"),
            ViewValueKind.Vector2I=>$"{rule.Vector2I.X},{rule.Vector2I.Y}",ViewValueKind.Vector3I=>$"{rule.Vector3I.X},{rule.Vector3I.Y},{rule.Vector3I.Z}",
            ViewValueKind.Resource => rule.Resource?.ResourcePath ?? "",
            _ => rule.Literal.AsString()
        };
        RefreshAuthoringIcons();
        RefreshTypedOverrideEditor(true);RefreshBindingActions();RebaseForm("override");RestoreVisibleDrafts();
    }
    private ViewPropertyOverride BuildRule()
    {
        RefreshTypedOverrideEditor();if(!typedPropertyValid)throw new InvalidOperationException("Invalid Override target/property: "+target.Text+":"+property.Text);
        var owner = SelectedDefinition() ?? throw new InvalidOperationException("請先選擇頁面或元件。");
        var rule = new ViewPropertyOverride { Target = new NodePath(target.Text), Property = property.Text.Trim(), ModelKey = modelKey.Text.Trim().StartsWith("{",StringComparison.Ordinal)?"":modelKey.Text.Trim(),ModelExpression=modelKey.Text.Trim().StartsWith("{",StringComparison.Ordinal)?modelKey.Text.Trim():"", Scope = (ViewInjectScope)scopes.Selected, Kind = (ViewValueKind)kinds.Selected };
        if(rule.ModelExpression.Length>0&&!ViewModelExpression.IsValidExpression(rule.ModelExpression))throw new FormatException("請輸入有效的 Model 來源，例如 {PageModel.Type}。");
        if(!rule.IsModelBinding&&RawOverrideRequested&&rule.Kind==ViewValueKind.Native&&value.Text!=typedMirroredText)throw new FormatException("此型別的固定值請使用原生欄位編輯。");
        if(!rule.IsModelBinding&&TryTypedLiteral(out var literal))AssignTypedLiteral(rule,literal);
        else if (!rule.IsModelBinding)
            switch (rule.Kind)
            {
                case ViewValueKind.Text: rule.Text = value.Text; break;
                case ViewValueKind.Integer: rule.Integer = long.Parse(value.Text, CultureInfo.InvariantCulture); break;
                case ViewValueKind.Number: rule.Number = double.Parse(value.Text, CultureInfo.InvariantCulture); if (!double.IsFinite(rule.Number)) throw new FormatException("數字必須有限。"); break;
                case ViewValueKind.Boolean: rule.Boolean = bool.Parse(value.Text); break;
                case ViewValueKind.Color: if (!Godot.Color.HtmlIsValid(value.Text)) throw new FormatException("請輸入有效的 HTML 色碼。"); rule.Color = Godot.Color.FromHtml(value.Text); break;
                case ViewValueKind.Vector2: var parts = value.Text.Split(','); if (parts.Length != 2) throw new FormatException("Vector2 格式為 x,y。"); rule.Vector = new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture)); break;
                case ViewValueKind.Vector3: var v3=value.Text.Split(',');if(v3.Length!=3)throw new FormatException("Vector3: x,y,z");rule.Vector3=new(float.Parse(v3[0],CultureInfo.InvariantCulture),float.Parse(v3[1],CultureInfo.InvariantCulture),float.Parse(v3[2],CultureInfo.InvariantCulture));break;
                case ViewValueKind.Vector2I:var v2i=value.Text.Split(',');if(v2i.Length!=2)throw new FormatException("Vector2I: x,y");rule.Vector2I=new(int.Parse(v2i[0],CultureInfo.InvariantCulture),int.Parse(v2i[1],CultureInfo.InvariantCulture));break;
                case ViewValueKind.Vector3I:var v3i=value.Text.Split(',');if(v3i.Length!=3)throw new FormatException("Vector3I: x,y,z");rule.Vector3I=new(int.Parse(v3i[0],CultureInfo.InvariantCulture),int.Parse(v3i[1],CultureInfo.InvariantCulture),int.Parse(v3i[2],CultureInfo.InvariantCulture));break;
                case ViewValueKind.Resource: rule.Resource = typedValueEdited?typedOverrideDraft?.NativeValue.AsGodotObject() as Resource:value.Text.Length == 0 ? null : ResourceLoader.Load(value.Text) ?? throw new FormatException("Resource 不存在。"); break;
                case ViewValueKind.Native:rule.NativeValue=typedOverrideDraft?.NativeValue??default;break;
            }
        if(selectedRuleResource is {} source){rule.SourceProperty=source.SourceProperty;rule.SourceComponent=source.SourceComponent;rule.SourceTransformPath=source.SourceTransformPath;rule.SourceValueJson=source.SourceValueJson;}
        return rule;
    }
    private void ApplyRule()
    {
        var rule=BuildRule();var owner=SelectedDefinition()!;
        CopyOverrideMode(owner,selectedRuleResource,rule);
        var old = SelectedRules(); var next = new global::Godot.Collections.Array<ViewPropertyOverride>(old);
        int changedIndex = selectedRule >= 0 && selectedRule < next.Count ? selectedRule : next.Count;
        if (changedIndex < next.Count)
        {
            var source=next[changedIndex];rule.SourceProperty=source.SourceProperty;rule.SourceComponent=source.SourceComponent;rule.SourceTransformPath=source.SourceTransformPath;rule.SourceValueJson=source.SourceValueJson;
            if(referenceRouter!=null&&ViewReferenceDiff.Equivalent(source,rule,referenceRouter)){AcceptCurrentDraft("override");typedValueEdited=false;return;}
            next[changedIndex] = rule;
        }
        else next.Add(rule);
        AcceptCurrentDraft("override");bindingFlow="";authoringPopup.Hide();ChangeSelectedBinding(owner,"Overrides",next,old,rule,changedIndex,false);
        selectedRuleResource=rule;selectedRule = changedIndex; RefreshRules(); rules.Select(changedIndex);RefreshBindingTrees();RefreshBindingActions();RebaseForm("override");RememberMutationSelection();
    }
    private void RemoveRule()
    {
        if (SelectedDefinition() is not Resource owner || selectedRule < 0 || selectedRule >= SelectedRules().Count) return;
        int index=selectedRule;CaptureCurrentDraft();
        var old = SelectedRules(); var next = new global::Godot.Collections.Array<ViewPropertyOverride>(old); next.RemoveAt(index);
        Change(owner, "Overrides", next, old, "Remove ViewSystem override");selectedRuleResource=null;selectedRule=-1;
        if(next.Count>0)LoadRule(Math.Min(index,next.Count-1));else {target.Text=".";property.Text="";value.Text="";modelKey.Text="";typedOverrideDraft=null;typedValueEdited=false;RebaseForm("override");}
        RefreshRules();RememberMutationSelection();
    }
    private void Change(Resource owner, StringName name, Variant next, Variant previous, string title)
    {
        // External Catalog dirty state is owned by _GetUnsavedStatus. Marking
        // Godot's global Resource history dirty leaves an unrelated Save prompt
        // after SaveDocument, since the public API cannot mark that history saved.
        var before=CaptureEditorLocation();var undo = GetUndoRedo(); undo.CreateAction(title, customContext: catalog,markUnsaved:false);
        undo.ForceFixedHistory();undo.AddDoProperty(owner, name, next); undo.ForceFixedHistory();undo.AddUndoProperty(owner, name, previous);
        CommitEditorAction(undo,before);
    }
    public void UndoDocument()
    {
        if (catalog == null) return;
        FlushAutoForm(true);FlushContextSettings();ResetAutoForm();
        GetUndoRedo().GetHistoryUndoRedo(GetUndoRedo().GetObjectHistoryId(catalog)).Undo(); RefreshRules();
    }
    public void RedoDocument()
    {
        if (catalog == null) return;
        ResetAutoForm();
        GetUndoRedo().GetHistoryUndoRedo(GetUndoRedo().GetObjectHistoryId(catalog)).Redo(); RefreshRules();
    }
    private void CreateCatalog()
    {
        var dialog=new EditorFileDialog {FileMode=EditorFileDialog.FileModeEnum.SaveFile,Access=EditorFileDialog.AccessEnum.Resources,Filters=new[]{"*.tres ; ViewCatalog"},CurrentFile="ViewCatalog.tres"};
        AddChild(dialog);string key=dialog.GetInstanceId().ToString(CultureInfo.InvariantCulture);
        dialogs.Add(key,(dialog,path=>RequestSwitch(()=>{var document=new ViewCatalog();var error=ResourceSaver.Save(document,path);if(error!=Error.Ok)throw new IOException(error.ToString());OpenDocument(document);} )));
        ConnectUi(dialog,Godot.FileDialog.SignalName.FileSelected,nameof(OnDialogSelected),key);
        ConnectUi(dialog,Godot.FileDialog.SignalName.Canceled,nameof(OnDialogCanceled),key);dialog.PopupCenteredRatio(.7f);
    }
    private void BrowseCatalog() => FileDialog("*.tres, *.res ; ViewCatalog", path => RequestSwitch(() => OpenCatalog(path)));
    private void FileDialog(string filter, Action<string> selected)
    {
        var dialog = new EditorFileDialog { FileMode = EditorFileDialog.FileModeEnum.OpenFile, Access = EditorFileDialog.AccessEnum.Resources, Filters = new[] { filter } };
        AddChild(dialog);
        string key = dialog.GetInstanceId().ToString(CultureInfo.InvariantCulture);
        dialogs.Add(key, (dialog, selected));
        ConnectUi(dialog, Godot.FileDialog.SignalName.FileSelected, nameof(OnDialogSelected), key);
        ConnectUi(dialog, Window.SignalName.CloseRequested, nameof(OnDialogCanceled), key);
        ConnectUi(dialog, ConfirmationDialog.SignalName.Canceled, nameof(OnDialogCanceled), key);
        dialog.PopupCenteredRatio(.65f);
    }
    private void OnDialogSelected(string path, string key)
    {
        var selected = dialogs[key].Selected;
        try { selected(path); } finally { CloseDialog(key); }
    }
    private void OnDialogCanceled(string key) => CloseDialog(key);
    private void CloseDialog(string key)
    {
        if (!dialogs.Remove(key, out var state)) return;
        DisconnectUi(state.Dialog);
        if (GodotObject.IsInstanceValid(state.Dialog)) state.Dialog.QueueFree();
    }
    private void AddEntry()
        =>PickEntryScene(ViewKind.Page);
    private void OnNewEntryKind(long kind)=>PickEntryScene((ViewKind)kind);
    private void PickEntryScene(ViewKind kind)
    {
        if (catalog == null||!Enum.IsDefined(kind)) return;
        var document=catalog;
        FileDialog("*.tscn ; UI scene", path => Try(() =>
        {
            if(catalog!=document)return;
            var scene=ResourceLoader.Load<PackedScene>(path)??throw new InvalidOperationException("找不到來源 Scene。");var root=scene.Instantiate();try{if(root is not Control)throw new InvalidOperationException("來源 Scene 根節點需要 Control。");}finally{root.Free();}
            var old = document.Entries; var next = new global::Godot.Collections.Array<ViewEntry>(old);
            string id = System.IO.Path.GetFileNameWithoutExtension(path); while (next.Any(item => item.Id == id)) id += "2";
            next.Add(new ViewEntry { Id = id,Kind=kind,State=kind==ViewKind.State?id:"", Scene = scene });
            var before=CaptureEditorLocation();var after=before.Duplicate();after["owner"]=next.Last();after["opened"]=default;after["placement"]=default;after["active"]=true;after["rule"]=default;after["event"]=default;after["search"]="";after["filter"]=kind==ViewKind.Element?4:5;
            var undo=GetUndoRedo();undo.CreateAction("Add ViewSystem "+kind,customContext:document,markUnsaved:false);undo.ForceFixedHistory();undo.AddDoProperty(document,"Entries",next);undo.ForceFixedHistory();undo.AddUndoProperty(document,"Entries",old);CommitEditorAction(undo,before,after);
        }));
    }
    private void RemoveEntry()
    {
        if (catalog == null || entry == null) return;
        var visible=Enumerable.Range(0,entries.ItemCount).Select(i=>catalog.Entries[entries.GetItemMetadata(i).AsInt32()]).ToArray();int visibleIndex=Array.IndexOf(visible,entry);var neighbor=visibleIndex<0?null:visible.Skip(visibleIndex+1).Concat(visible.Take(visibleIndex).Reverse()).FirstOrDefault();
        int index=catalog.Entries.IndexOf(entry);var old=catalog.Entries;var next=new global::Godot.Collections.Array<ViewEntry>(old);next.Remove(entry);
        Change(catalog,"Entries",next,old,"Remove ViewSystem page");
        if(neighbor!=null)SelectEntry(next.IndexOf(neighbor));else ClearEditingSelection(false);RememberMutationSelection();
    }

    private void AddElementUnder(string selectedParent)
    {
        if (entry == null) return;
        var owner = entry;
        var parentNode=PageAuthoringRoot()?.GetNodeOrNull(new NodePath(selectedParent));
        while(parentNode!=null&&parentNode is not Control)parentNode=parentNode.GetParent();
        selectedParent=parentNode!=null&&PageAuthoringRoot() is {} pageRoot?PathFrom(pageRoot,parentNode):".";
        FileDialog("*.tscn ; UI element", path => Try(() =>
        {
            var old = owner.Elements; var next = new global::Godot.Collections.Array<ViewElementPlacement>(old);
            string id = System.IO.Path.GetFileNameWithoutExtension(path); while (next.Any(item => item.Id == id)) id += "2";
            next.Add(new ViewElementPlacement { Id = id, Scene = ResourceLoader.Load<PackedScene>(path),ParentPath=new NodePath(selectedParent) });
            Change(owner, "Elements", next, old, "Add ViewSystem element"); SelectPlacement(owner,next.Last());RememberMutationSelection();
        }));
    }
    private void RemoveElement()
    {
        if (entry == null || elements.Selected <= 0) return;
        var owner=entry;int index=owner.Elements.IndexOf(editing.Placement!);if(index<0)return;
        var visible=Enumerable.Range(0,entries.ItemCount).Select(i=>NavigationChoice(i)?.Choice).OfType<ViewElementPlacement>().ToArray();
        int visibleIndex=Array.IndexOf(visible,editing.Placement!);
        var neighbor=visibleIndex>=0?visible.Skip(visibleIndex+1).Concat(visible.Take(visibleIndex).Reverse()).FirstOrDefault():null;
        var old=owner.Elements;var next=new global::Godot.Collections.Array<ViewElementPlacement>(old);next.RemoveAt(index);
        Change(owner,"Elements",next,old,"Remove ViewSystem element");
        if(neighbor!=null)SelectPlacement(owner,neighbor,false);else SetEditingSelection(owner,null,true,false);RememberMutationSelection();
    }

    private void OpenSource()
    {
        var scene = SelectedDefinition() switch { ViewEntry page => page.Scene??(page.ScenePath.Length>0?GD.Load<PackedScene>(page.ScenePath):null), ViewElementPlacement placement => placement.Scene??(placement.ScenePath.Length>0?GD.Load<PackedScene>(placement.ScenePath):null), _ => null };
        if (scene != null) EditorInterface.Singleton.OpenSceneFromPath(scene.ResourcePath);
    }
    public void RefreshPreview()
    {
        if (rebuilding || viewport == null) return;
        CaptureCurrentDraft();
        rebuilding = true;
        try
        {
            ClearPreview(); PreviewError = "";
            if (catalog == null || entry == null || (!editing.Active&&navigationEntry==null)) return;
            catalog.Validate();
            viewport.Size = previewSize;
            previewRoot = new Control { Name = "PreviewRoot", Size = viewport.Size, MouseFilter = Control.MouseFilterEnum.Ignore }; viewport.AddChild(previewRoot);
            if (catalog.ReferenceHeight > 0)
            {
                previewRoot.Size = new Vector2((float)viewport.Size.X / viewport.Size.Y * catalog.ReferenceHeight, catalog.ReferenceHeight);
                previewRoot.Scale = Vector2.One * ((float)viewport.Size.Y / catalog.ReferenceHeight);
            }
            var context = new ViewContext(ViewModels.From(entry.PreviewModel), ViewModels.From(catalog.SharedPreviewModel), true,
                (action, _) => status.Text = "預覽事件：" + action, PreviewBreakpoints(), NormalizedSafeArea: PreviewSafeArea(), GlobalSafeArea: catalog.SafeArea);
            MountPreview(entry, context);
            if (entry.Kind is ViewKind.Page or ViewKind.Overlay && entry.State.Length > 0)
                foreach (var state in catalog.Entries.Where(item => item.Kind == ViewKind.State && item.State == entry.State)) MountPreview(state, context);
            if (overlays.Selected > 0)
            {
                string id = overlays.GetItemMetadata(overlays.Selected).AsString(); var overlay = catalog.Entries.First(item => item.Id == id);
                var overlayContext=context with { Page = ViewModels.From(overlay.PreviewModel) };
                MountPreview(overlay, overlayContext);
                foreach(var state in catalog.Entries.Where(s=>s.Kind==ViewKind.State&&overlay.State.Length>0&&s.State==overlay.State))
                {var existing=previews.FirstOrDefault(p=>p.Definition==state);if(existing==null)MountPreview(state,overlayContext);else existing.Rebind(overlayContext);}
            }
            selection.Text = ""; selection.Visible=false;
            status.Text = "";
        }
        catch (Exception error) { PreviewError = error.Message; status.Text = "預覽失敗：" + error.Message; ClearPreview(); }
        finally { lastSignature = catalog == null ? "" : Signature(catalog); rebuilding = false; FitPreview();RefreshValuePicker();RefreshOverrideIcons();RefreshAuthoringIcons();RefreshTypedOverrideEditor();RefreshPersistedForms();RestoreVisibleDrafts();RefreshBindingTrees(); }
    }
    private void MountPreview(ViewEntry definition, ViewContext context)
    {
        var instance = ViewComposer.Create(definition, context); previews.Add(instance); previewRoot!.AddChild(instance.Root); instance.MountLayout();
    }
    private void ClearPreview()
    {
        foreach (var preview in previews) preview.Dispose(); previews.Clear();
        if (GodotObject.IsInstanceValid(previewRoot)) previewRoot!.Free(); previewRoot = null;
    }
    private void FitPreview()
    {
        if (viewport == null || previewArea.Size.X <= 0 || previewArea.Size.Y <= 0) return;
        float scale = Math.Min(previewArea.Size.X / viewport.Size.X, previewArea.Size.Y / viewport.Size.Y);
        previewContainer.CustomMinimumSize=Vector2.Zero;
        previewContainer.Size = viewport.Size; previewContainer.Scale = Vector2.One * scale;
        previewContainer.Position = (previewArea.Size - (Vector2)viewport.Size * scale) / 2;
        previewScale.Text=$"適合視窗 {scale:P0}";
    }
    public override void _Process(double delta)
    {
        // Godot may deliver an already-queued process callback while script Resources
        // are being restored. Only resume document reflection after our reload barrier.
        if (HasMeta(ReloadState)) return;
        PollInteractions();FitEditorVisibleArea(); FitPreview(); FitTypedOverrideEditor(); FitContextSettings(); SyncNestedInspectors(); RevealBindingEditorAfterLayout(); RefreshBindingActions(); elapsed += delta; if (elapsed < .25 || catalog == null) return; elapsed = 0;
        if (Signature(catalog) != lastSignature) RefreshDocument();
        saveButton.Text=Signature(catalog)==savedSignature&&!HasPendingDrafts()?"儲存":"儲存 *";
    }
    private void FitEditorVisibleArea()
    {
        if(!GodotObject.IsInstanceValid(panel)||!panel.IsVisibleInTree())return;
        // Other editor docks can force the main screen beyond the native window.
        // Keep our own scrolling surface within the actual viewport so its last
        // fields remain reachable even in that editor layout.
        var visible=EditorInterface.Singleton.GetEditorMainScreen().GetGlobalRect().Intersection(panel.GetViewportRect());
        if(visible.Size.Y>0&&!Mathf.IsEqualApprox(panel.Size.Y,visible.Size.Y))panel.Size=new Vector2(panel.Size.X,visible.Size.Y);
    }
    public void RefreshDocument()
    {
        if (catalog == null) return;
        CaptureCurrentDraft();ReconcileBindingSelection();
        if(entry!=null&&!catalog.Entries.Contains(entry)){entry=null;navigationPlacement=null;editing.Active=false;}
        if(entry!=null&&editing.Placement!=null&&!entry.Elements.Contains(editing.Placement)){navigationPlacement=null;editing.Active=false;}
        RefreshEntries();
        if(catalog.Entries.Count==0){entry=null;navigationEntry=null;editing.Active=false;ResetBindingSelection();}
        elements.Clear();elements.AddItem("Root");
        if(entry!=null)foreach(var item in entry.Elements)elements.AddItem(item.DisplayName.Length>0?item.DisplayName+" · "+item.Id:item.Id);
        elements.Select(editing.Placement!=null&&entry!=null?entry.Elements.IndexOf(editing.Placement)+1:0);
        RefreshRules();RefreshPreview();
    }

    private void ValidateDocument()
    {
        Try(() =>
        {
            ShowIssues();
        });
    }
    public void SaveDocument()
    {
        if (catalog == null || catalog.ResourcePath.Length == 0) throw new InvalidOperationException("請先開啟已有路徑的 catalog。");
        FlushContextSettings();ApplyPendingDrafts();
        foreach (var resource in AuthoredResources(catalog).Distinct())
            if (!string.IsNullOrEmpty(resource.ResourcePath) && !resource.IsBuiltIn())
            {
                var error = ResourceSaver.Save(resource); if (error != Error.Ok) throw new IOException($"無法儲存 {resource.ResourcePath}: {error}");
            }
        var saved = ResourceSaver.Save(catalog); if (saved != Error.Ok) throw new IOException("儲存失敗：" + saved);
        savedSignature = Signature(catalog); EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(catalog.ResourcePath); if (GodotObject.IsInstanceValid(status)){status.Text = "Saved";status.TooltipText=catalog.ResourcePath;}
    }
    public override void _SaveExternalData() { if (GodotObject.IsInstanceValid(panel)&&catalog != null && (Signature(catalog) != savedSignature||HasPendingDrafts())) Try(SaveDocument); }
    public override string _GetUnsavedStatus(string forScene) => forScene.Length==0&&catalog != null && (Signature(catalog) != savedSignature||HasPendingDrafts()) ? "ViewSystem：" + catalog.ResourcePath : "";
    private static IEnumerable<Resource> AuthoredResources(ViewCatalog document)
    {
        yield return document.SharedPreviewModel;
        yield return document.SafeArea;
        foreach (var page in document.Entries)
        {
            if (page == null) continue;
            yield return page.PreviewModel;
            yield return page.SafeArea;
            if(page.PoolPolicy!=null)yield return page.PoolPolicy;
            if(page.ShowTransition!=null)yield return page.ShowTransition;
            if(page.HideTransition!=null)yield return page.HideTransition;
            foreach(var navigation in page.StateNavigation){if(navigation==null)continue;yield return navigation;foreach(var link in navigation.Links)if(link!=null)yield return link;}
            foreach(var focus in page.FocusLinks)if(focus!=null)yield return focus;
            foreach (var item in page.Overrides) if (item != null) yield return item;
            foreach (var item in page.Events) if (item != null) yield return item;
            foreach (var element in page.Elements)
            {
                if (element == null) continue;
                if(element.ShowTransition!=null)yield return element.ShowTransition;
                if(element.HideTransition!=null)yield return element.HideTransition;
                if(element.PoolPolicy!=null)yield return element.PoolPolicy;
                if (element.Layout != null) yield return element.Layout.Data;
                foreach(var breakpoint in element.Breakpoints){if(breakpoint==null)continue;yield return breakpoint;if(breakpoint.LayoutData!=null)yield return breakpoint.LayoutData;}
                foreach (var item in element.Overrides) if (item != null) yield return item;
                foreach (var item in element.Events) if (item != null) yield return item;
                yield return element;
            }
            yield return page;
        }
    }
    private static string Signature(ViewCatalog document)
    {
        // Native instance IDs and Variant.ToString() for Resource arrays change
        // across C# reload; dirty state must reflect authored values instead.
        document.RetainAuthoredGraph();
        var result = new StringBuilder();
        foreach(var resource in AuthoredResources(document).Append(document))
            AppendResourceSignature(result,resource,new HashSet<ulong>(),true);
        return result.ToString();
    }
    private static void AppendResourceSignature(StringBuilder text,Resource resource,HashSet<ulong> visiting,bool authored=false)
    {
        text.Append(resource.GetType().FullName).Append('{');
        if(!authored && !resource.IsBuiltIn() && resource.ResourcePath.Length>0)
        {text.Append(resource.ResourcePath).Append('}');return;}
        ulong id=resource.GetInstanceId();
        if(!visiting.Add(id)){text.Append("cycle}");return;}
        if(resource.GetType().Assembly==typeof(ViewCatalog).Assembly)
        {
            // Authored C# exports already have a typed managed schema. Poll it
            // directly rather than re-entering a temporarily reloading script's
            // native virtual GetPropertyList on every editor frame.
            foreach(var property in resource.GetType().GetProperties(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance)
                .Where(p=>Attribute.IsDefined(p,typeof(ExportAttribute))).OrderBy(p=>p.Name,StringComparer.Ordinal))
            {text.Append(property.Name).Append('=');AppendManagedValueSignature(text,property.GetValue(resource),visiting);text.Append(';');}
            text.Append("resource_name=").Append(resource.ResourceName).Append(";local=").Append(resource.ResourceLocalToScene);
            foreach(var key in resource.GetMetaList().OrderBy(key=>key.ToString(),StringComparer.Ordinal))
                if(key!="_macaca_viewsystem_resource_ownership")
                {text.Append("metadata/").Append(key).Append('=');AppendValueSignature(text,resource.GetMeta(key),visiting);}
        }
        else foreach(var property in resource.GetPropertyList())
        {
            if((property["usage"].AsInt64() & (long)PropertyUsageFlags.Storage)==0)continue;
            string name=property["name"].AsString();
            if(name is "script" or "resource_scene_unique_id" || name=="metadata/_macaca_viewsystem_resource_ownership")continue;
            text.Append(name).Append('=');AppendValueSignature(text,resource.Get(name),visiting);text.Append(';');
        }
        visiting.Remove(id);text.Append('}');
        // Native calls re-enter the managed Resource script. Keep the wrapper
        // alive through that round trip, including the final exported getter.
        GC.KeepAlive(resource);
    }
    private static void AppendManagedValueSignature(StringBuilder text,object? value,HashSet<ulong> visiting)
    {
        if(value==null){text.Append("null;");return;}
        if(value is Resource resource){AppendResourceSignature(text,resource,visiting);return;}
        if(value is Variant variant){AppendValueSignature(text,variant,visiting);return;}
        if(value is Godot.Collections.Dictionary<string,Variant> dictionary)
        {foreach(var key in dictionary.Keys.OrderBy(key=>key,StringComparer.Ordinal)){text.Append(key.Length).Append(':').Append(key);AppendValueSignature(text,dictionary[key],visiting);}return;}
        if(value is System.Collections.IEnumerable array && value is not string)
        {text.Append('[');foreach(var item in array)AppendManagedValueSignature(text,item,visiting);text.Append(']');return;}
        string literal=value is IFormattable formatted?formatted.ToString(null,CultureInfo.InvariantCulture):value.ToString()!;
        text.Append(value.GetType().FullName).Append(':').Append(literal.Length).Append(':').Append(literal);
    }
    private static void AppendValueSignature(StringBuilder text,Variant value,HashSet<ulong> visiting)
    {
        text.Append((int)value.VariantType).Append(':');
        if(value.VariantType==Variant.Type.Object && value.AsGodotObject() is Resource resource)
            AppendResourceSignature(text,resource,visiting);
        else if(value.VariantType==Variant.Type.Array)
        {text.Append('[');foreach(var item in value.AsGodotArray())AppendValueSignature(text,item,visiting);text.Append(']');}
        else if(value.VariantType==Variant.Type.Dictionary)
        {
            text.Append('[');var dictionary=value.AsGodotDictionary();
            foreach(var key in dictionary.Keys.OrderBy(key=>key.ToString(),StringComparer.Ordinal))
            {AppendValueSignature(text,key,visiting);AppendValueSignature(text,dictionary[key],visiting);}
            text.Append(']');
        }
        else{string literal=value.ToString();text.Append(literal.Length).Append(':').Append(literal);}
    }
    private void DisconnectUi(GodotObject? only = null)
    {
        if (nativeBinder != null) nativeBinder.Call("disconnect_native", Variant.From(only));
    }
    private void ReleaseEditorUi()
    {
        SetProcess(false);
        ReleaseInteractions();
        ReleaseEventMethods();
        if(idInspector!=null){idInspector.OpenedCatalog=null;RemoveInspectorPlugin(idInspector);idInspector.Dispose();idInspector=null;}
        if(controlRectInspector!=null){RemoveInspectorPlugin(controlRectInspector);controlRectInspector.Dispose();controlRectInspector=null;}
        DisconnectUi();
        ReleaseReferenceUi();
        ReleaseBindingReturn();lastMutationAfter?.Dispose();lastMutationAfter=null;
        ReleaseNestedInspectors();ReleaseAdvancedSettings();ReleaseContextSettings();ReleaseTypedOverrideEditor();
        ReleaseAuthoringTrees();
        ClearPreview();
        foreach (var state in dialogs.Values)
            if (GodotObject.IsInstanceValid(state.Dialog)) state.Dialog.Free();
        foreach (var state in switchDialogs.Values)
            if (GodotObject.IsInstanceValid(state.Dialog)) state.Dialog.Free();
        dialogs.Clear(); switchDialogs.Clear(); buttonActions.Clear();
        nativeBinder?.Dispose(); nativeBinder = null;
        if (GodotObject.IsInstanceValid(panel)) { panel.GetParent()?.RemoveChild(panel); panel.Free(); }
        // Do not serialize native Controls that were just freed. Godot preserves
        // authored Resources; the editor widgets are reconstructed after reload.
        panel = null!; entries = null!; rules = null!;saveButton=null!;toolsToolbar=null!;
        elements = null!; overlays = null!; kinds = null!; scopes = null!;
        catalogPath = null!; target = null!; property = null!; value = null!; modelKey = null!;
        ReleasePreviewPresets();status = null!; selection = null!;
        previewArea = null!; previewContainer = null!; viewport = null!;
        ReleaseDiagnosticsTools();
        ReleaseParityTools();
        ReleaseParityWorkflow();
        ReleaseUnityWorkflow();
    }
    public void OnBeforeSerialize()
    {
        if (!GodotObject.IsInstanceValid(panel)) return;
        CaptureCurrentDraft();
        using var state = new Godot.Collections.Dictionary
        {
            ["saved_signature"] = savedSignature, ["entry"] = catalog == null || entry == null ? -1 : catalog.Entries.IndexOf(entry),
            ["element"] = elements.Selected, ["overlay"] = overlays.Selected, ["rule"] = selectedRule,
            ["kind"] = kinds.Selected, ["scope"] = scopes.Selected,
            ["width"] = previewSize.X, ["height"] = previewSize.Y, ["visible"] = panel.Visible,
            ["status"] = status.Text, ["preview_error"] = PreviewError,
            ["workspace_tab"] = workspaceTabs.CurrentTab, ["inspector_tab"] = inspectorTabs.CurrentTab,
            ["search"] = entrySearch.Text, ["filter"] = entryFilter.Selected,
            ["event_index"] = selectedEvent, ["definition_kind"] = definitionKind.Selected, ["definition_order"] = definitionOrder.Value,
            ["graph_scroll"] = pageGraph.ScrollOffset, ["graph_zoom"] = pageGraph.Zoom
        };
        if (catalog != null) state["catalog"] = catalog;
        if(advancedOwner!=null&&advancedInspector?.GetEditedObject()==advancedProxy)state["advanced_owner"]=advancedOwner;
        state["editor_location"]=CaptureEditorLocation();
        state["event_receiver"]=eventReceiverPath;state["event_receiver_draft"]=eventReceiverDraft;state["event_advanced"]=eventAdvanced.Visible;
        state["form_drafts"]=new Godot.Collections.Array<Resource>(formDrafts);
        state["owner_sessions"]=new Godot.Collections.Array<Resource>(ownerSessions);
        state["pending_settings"]=new Godot.Collections.Array<Resource>(pendingSettings);
        var rawModes=new Godot.Collections.Dictionary<string,bool>();foreach(var mode in overrideRawModes)rawModes[mode.Key]=mode.Value;state["override_raw_modes"]=rawModes;
        SaveAuthoringTreeState(state);
        if(navigationEntry!=null)state["navigation_owner"]=navigationEntry;
        state["navigation_root_search"]=navigationRootSearch;state["navigation_root_filter"]=navigationRootFilter;
        if(rectClipboard!=null)state["rect_clipboard"]=rectClipboard;
        foreach (var field in layoutFields) state["layout_draft_"+field.Key] = field.Value.Value;
        if(typedOverrideDraft!=null)state["typed_override_draft"]=typedOverrideDraft;
        state["typed_value_edited"]=typedValueEdited;
        foreach (var field in EditorFormFields())
        {
            state[field.Name] = field.Text;
            state[field.Name + "_caret"] = field.CaretColumn;
        }
        var focus = panel.GetViewport().GuiGetFocusOwner();
        if (focus != null && panel.IsAncestorOf(focus)) state["focus"] = panel.GetPathTo(focus);
        SetMeta(ReloadState, state);
        ReleaseEditorUi();
    }
    public void OnAfterDeserialize() => CallDeferred(nameof(RestoreEditorAfterReload));
    private void RestoreEditorAfterReload()
    {
        if (!IsInsideTree() || IsAutomatedEditorExit()) return;
        CreateEditorUi();
        if (!HasMeta(ReloadState)) return;
        using var state = GetMeta(ReloadState).AsGodotDictionary();
        // Keep the same unsaved Resource identity and EditorUndoRedoManager history.
        // Resource script state may restore after this plugin, so this method is
        // deferred until Godot has finished deserializing the whole assembly.
        if (state.TryGetValue("catalog", out var document) && document.AsGodotObject() is ViewCatalog restored)
        {
            OpenDocument(restored);
            int index = state["entry"].AsInt32();
            if (index >= 0 && index < restored.Entries.Count) SelectEntry(index);
        }
        if(state.TryGetValue("rect_clipboard",out var clipboard))rectClipboard=clipboard.AsGodotObject() as Resource;
        savedSignature = state["saved_signature"].AsString();
        if(state.TryGetValue("search",out var search)) entrySearch.Text=search.AsString();
        if(state.TryGetValue("filter",out var filter)&&state.ContainsKey("navigation_root_filter")) entryFilter.Select(filter.AsInt32());
        if(state.TryGetValue("navigation_owner",out var navigationOwner)&&navigationOwner.AsGodotObject() is ViewEntry owner&&catalog?.Entries.Contains(owner)==true)navigationEntry=owner;
        if(state.TryGetValue("navigation_root_search",out var rootSearch))navigationRootSearch=rootSearch.AsString();
        if(state.TryGetValue("navigation_root_filter",out var rootFilter))navigationRootFilter=rootFilter.AsInt32();
        RefreshNavigation();
        if(state.TryGetValue("workspace_tab",out var tab)) workspaceTabs.CurrentTab=tab.AsInt32();
        if(state.TryGetValue("inspector_tab",out var inspectorTab)) inspectorTabs.CurrentTab=Math.Clamp(inspectorTab.AsInt32(),0,inspectorTabs.GetTabCount()-1);
        int element = state["element"].AsInt32(), overlay = state["overlay"].AsInt32();
        if (element >= 0 && element < elements.ItemCount) elements.Select(element);
        if (overlay >= 0 && overlay < overlays.ItemCount) overlays.Select(overlay);
        RefreshRules();
        selectedRule = state["rule"].AsInt32();
        if (selectedRule >= 0 && selectedRule < rules.ItemCount) rules.Select(selectedRule);
        kinds.Select(state["kind"].AsInt32()); scopes.Select(state["scope"].AsInt32());
        SelectPreviewSize(new(state["width"].AsInt32(),state["height"].AsInt32()),false);
        RefreshPreview();
        foreach (var field in layoutFields)
            if(state.TryGetValue("layout_draft_"+field.Key,out var draft)) field.Value.Value=draft.AsDouble();
        foreach (var field in EditorFormFields())
        {
            if(state.TryGetValue(field.Name,out var text))field.Text=text.AsString();
            if(state.TryGetValue(field.Name+"_caret",out var caret))field.CaretColumn=caret.AsInt32();
        }
        if(state.TryGetValue("event_index",out var eventIndex) && eventIndex.AsInt32()>=0 && eventIndex.AsInt32()<eventBindings.ItemCount)
        { selectedEvent=eventIndex.AsInt32(); eventBindings.Select(selectedEvent); }
        if(state.TryGetValue("definition_kind",out var definitionType)) definitionKind.Select(definitionType.AsInt32());
        if(state.TryGetValue("definition_order",out var order)) definitionOrder.Value=order.AsDouble();
        if(state.TryGetValue("graph_zoom",out var zoom)) pageGraph.Zoom=zoom.AsSingle();
        if(state.TryGetValue("graph_scroll",out var scroll)) pageGraph.ScrollOffset=scroll.AsVector2();
        panel.Visible = state["visible"].AsBool();
        status.Text = state["status"].AsString(); PreviewError = state["preview_error"].AsString();
        if (state.TryGetValue("focus", out var path)) panel.GetNodeOrNull<Control>(path.AsNodePath())?.GrabFocus();
        lastSignature = catalog == null ? "" : Signature(catalog);
        RemoveMeta(ReloadState);
        if(state.TryGetValue("override_raw_modes",out var modes)){using var savedModes=modes.AsGodotDictionary();overrideRawModes.Clear();foreach(var mode in savedModes)overrideRawModes[mode.Key.AsString()]=mode.Value.AsBool();}
        if(state.TryGetValue("editor_location",out var location))
        {
            restoringDrafts=true;
            if(state.TryGetValue("form_drafts",out var drafts)){formDrafts.Clear();formDrafts.AddRange(drafts.AsGodotArray<Resource>());}
            if(state.TryGetValue("owner_sessions",out var sessions)){ownerSessions.Clear();ownerSessions.AddRange(sessions.AsGodotArray<Resource>());}
            if(state.TryGetValue("pending_settings",out var settings)){pendingSettings.Clear();pendingSettings.AddRange(settings.AsGodotArray<Resource>());}
            RestoreEditorLocation(location.AsGodotDictionary());restoringDrafts=false;RestoreVisibleDrafts();
        }
        else if(navigationEntry!=null)
        {
            var legacyOwner=navigationEntry;
            if(element>0&&element<=legacyOwner.Elements.Count)SetEditingSelection(legacyOwner,legacyOwner.Elements[element-1],true);
            else SetEditingSelection(legacyOwner,null,true,false);
        }
        RefreshTypedOverrideEditor(true);
        if(state.TryGetValue("typed_override_draft",out var typedDraft)&&typedDraft.AsGodotObject() is ViewPropertyOverride restoredDraft)
        {typedOverrideDraft=restoredDraft;typedValueEdited=state.TryGetValue("typed_value_edited",out var edited)&&edited.AsBool();if(overrideValueProxy!=null){typedOverrideSync=true;overrideValueProxy.Set("Value",restoredDraft.NativeValue);typedOverrideSync=false;}}
        if(state.TryGetValue("Value",out var rawDraft))value.Text=rawDraft.AsString();
        RefreshBindingTrees();RestoreAuthoringTreeState(state);RefreshBindingActions();
        if(state.TryGetValue("event_receiver",out var receiver)){eventReceiverPath=receiver.AsString();eventReceiverDraft=state.TryGetValue("event_receiver_draft",out var receiverDraft)&&receiverDraft.AsBool();RefreshEventMethods();}
        if(state.TryGetValue("event_advanced",out var eventDetails))eventAdvanced.Visible=eventDetails.AsBool();
        if(state.TryGetValue("advanced_owner",out var advanced)&&advanced.AsGodotObject() is Resource advancedResource&&BelongsToDocument(advancedResource))InspectAdvancedOwner(advancedResource);
    }
    public override void _ExitTree()
    {
        ReleaseEditorUi();if(!HasMeta(ReloadState))ClearDraftStorage(true);
    }
}
#endif
