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
    private VBoxContainer panel = null!;
    private ItemList entries = null!, rules = null!;
    private OptionButton elements = null!, overlays = null!, kinds = null!, scopes = null!;
    private LineEdit catalogPath = null!, target = null!, property = null!, value = null!, modelKey = null!;
    private SpinBox width = null!, height = null!;
    private Label status = null!, selection = null!;
    private Control previewArea = null!;
    private SubViewportContainer previewContainer = null!;
    private SubViewport viewport = null!;
    private Control? previewRoot;
    private readonly List<ViewInstance> previews = new();
    private ViewCatalog? catalog;
    private ViewEntry? entry;
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
        if (pixelWidth < 64 || pixelHeight < 64 || pixelWidth > 4096 || pixelHeight > 4096)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        width.Value = pixelWidth; height.Value = pixelHeight; RefreshPreview();
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
        Button(toolbar, "儲存", "Save", () => Try(SaveDocument));
        Button(toolbar, "重載", "Reload", () => RequestSwitch(() => OpenCatalog(catalogPath.Text, true)));
        Button(toolbar, "Undo", "Undo", UndoDocument);
        Button(toolbar, "Redo", "Redo", RedoDocument);
        Button(toolbar, "驗證全部", "Validate", ValidateDocument);
        Button(toolbar, "新增 Catalog", "NewCatalog", CreateCatalog);
        CreateUnityWorkflow();
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
    private void OnEntrySelected(long index) => SelectEntry(entries.GetItemMetadata((int)index).AsInt32());
    private void OnElementSelected(long index) { selectedRule = -1; RefreshRules(); }
    private void OnRuleSelected(long index) => LoadRule((int)index);
    private void OnOverlaySelected(long index) => RefreshPreview();
    private void OnDimensionChanged(double value) => RefreshPreview();
    private void OnSceneSaved(string path) => RefreshPreview();
    private static LineEdit Field(Node parent, string name, string hint, string text)
    {
        var field = new LineEdit { Name = name, PlaceholderText = hint, TooltipText = hint, Text = text }; parent.AddChild(field); return field;
    }
    private SpinBox Dimension(Node parent, string name, int initial)
    {
        var box = new SpinBox { Name = name, MinValue = 240, MaxValue = 4096, Step = 1, Value = initial, CustomMinimumSize = new Vector2(90, 0) };
        parent.AddChild(box); ConnectUi(box, Godot.Range.SignalName.ValueChanged, nameof(OnDimensionChanged)); return box;
    }
    private void Try(Action action)
    {
        try { action(); } catch (Exception error) { PreviewError = error.Message; if (GodotObject.IsInstanceValid(status)) status.Text = "錯誤：" + error.Message; else GD.PushError(error.ToString()); }
    }
    private void RequestSwitch(Action action)
    {
        if (catalog == null || Signature(catalog) == savedSignature) { Try(action); return; }
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
        if (!ResourceLoader.Exists(path)) throw new InvalidOperationException("找不到 catalog：" + path);
        var document = ResourceLoader.Load<ViewCatalog>(path, cacheMode: reload ? ResourceLoader.CacheMode.ReplaceDeep : ResourceLoader.CacheMode.Reuse)
            ?? throw new InvalidOperationException("這個 Resource 不是 ViewCatalog。");
        OpenDocument(document);
    }
    private void OpenDocument(ViewCatalog document)
    {
        document.RetainAuthoredGraph();
        catalog = document; catalogPath.Text = document.ResourcePath; savedSignature = Signature(document);
        RefreshEntries(); if (catalog.Entries.Count > 0) SelectEntry(0); else { entry = null; ClearPreview(); }
    }
    private void RefreshEntries()
    {
        RefreshNavigation();
        overlays.Clear(); overlays.AddItem("Overlay");
        if (catalog != null)
            foreach (var item in catalog.Entries)
                if (item?.Kind == ViewKind.Overlay) { overlays.AddItem(item.Id); overlays.SetItemMetadata(overlays.ItemCount - 1, item.Id); }
        RebuildGraph();
    }
    public void SelectEntry(int index)
    {
        if (catalog == null || index < 0 || index >= catalog.Entries.Count) return;
        entry = catalog.Entries[index]; SyncNavigationSelection(index); elements.Clear(); elements.AddItem("Root");
        if (entry != null) foreach (var element in entry.Elements) elements.AddItem(element == null ? "（空白）" : (element.DisplayName.Length>0 ? element.DisplayName+" · "+element.Id : element.Id));
        selectedRule = -1; RefreshRules(); RefreshPreview(); SyncGraphSelection();
    }
    private Resource? SelectedDefinition() => entry == null ? null : elements.Selected <= 0 ? entry : entry.Elements.ElementAtOrDefault(elements.Selected - 1);
    private global::Godot.Collections.Array<ViewPropertyOverride> SelectedRules() => SelectedDefinition() switch
    {
        ViewEntry definition => definition.Overrides,
        ViewElementPlacement placement => placement.Overrides,
        _ => new()
    };
    private void RefreshRules()
    {
        RefreshInspectorPanels();
        rules.Clear(); foreach (var rule in SelectedRules()) rules.AddItem(rule == null ? "（空白）" : $"{rule.Target}:{rule.Property}" + (rule.ModelKey.Length > 0 ? " ← " + rule.ModelKey : ""));
    }
    private void LoadRule(int index)
    {
        var list = SelectedRules(); if (index < 0 || index >= list.Count || list[index] == null) return;
        selectedRule = index; var rule = list[index]; target.Text = rule.Target.ToString(); property.Text = rule.Property;
        kinds.Select((int)rule.Kind); scopes.Select((int)rule.Scope); modelKey.Text = rule.ModelKey;
        value.Text = rule.Kind switch
        {
            ViewValueKind.Color => "#" + rule.Color.ToHtml(),
            ViewValueKind.Vector2 => FormattableString.Invariant($"{rule.Vector.X},{rule.Vector.Y}"),
            ViewValueKind.Resource => rule.Resource?.ResourcePath ?? "",
            _ => rule.Literal.AsString()
        };
    }
    private void ApplyRule()
    {
        var owner = SelectedDefinition() ?? throw new InvalidOperationException("請先選擇頁面或元件。");
        var rule = new ViewPropertyOverride { Target = new NodePath(target.Text), Property = property.Text.Trim(), ModelKey = modelKey.Text.Trim(), Scope = (ViewInjectScope)scopes.Selected, Kind = (ViewValueKind)kinds.Selected };
        if (rule.ModelKey.Length == 0)
            switch (rule.Kind)
            {
                case ViewValueKind.Text: rule.Text = value.Text; break;
                case ViewValueKind.Integer: rule.Integer = long.Parse(value.Text, CultureInfo.InvariantCulture); break;
                case ViewValueKind.Number: rule.Number = double.Parse(value.Text, CultureInfo.InvariantCulture); if (!double.IsFinite(rule.Number)) throw new FormatException("數字必須有限。"); break;
                case ViewValueKind.Boolean: rule.Boolean = bool.Parse(value.Text); break;
                case ViewValueKind.Color: if (!Godot.Color.HtmlIsValid(value.Text)) throw new FormatException("請輸入有效的 HTML 色碼。"); rule.Color = Godot.Color.FromHtml(value.Text); break;
                case ViewValueKind.Vector2: var parts = value.Text.Split(','); if (parts.Length != 2) throw new FormatException("Vector2 格式為 x,y。"); rule.Vector = new Vector2(float.Parse(parts[0], CultureInfo.InvariantCulture), float.Parse(parts[1], CultureInfo.InvariantCulture)); break;
                case ViewValueKind.Resource: rule.Resource = value.Text.Length == 0 ? null : ResourceLoader.Load(value.Text) ?? throw new FormatException("Resource 不存在。"); break;
            }
        var old = SelectedRules(); var next = new global::Godot.Collections.Array<ViewPropertyOverride>(old);
        int changedIndex = selectedRule >= 0 && selectedRule < next.Count ? selectedRule : next.Count;
        if (changedIndex < next.Count)
        {
            var source=next[changedIndex];rule.SourceProperty=source.SourceProperty;rule.SourceComponent=source.SourceComponent;rule.SourceTransformPath=source.SourceTransformPath;rule.SourceValueJson=source.SourceValueJson;
            next[changedIndex] = rule;
        }
        else next.Add(rule);
        Change(owner, "Overrides", next, old, "ViewSystem override");
        selectedRule = changedIndex; RefreshRules(); rules.Select(changedIndex);
    }
    private void RemoveRule()
    {
        if (SelectedDefinition() is not Resource owner || selectedRule < 0 || selectedRule >= SelectedRules().Count) return;
        var old = SelectedRules(); var next = new global::Godot.Collections.Array<ViewPropertyOverride>(old); next.RemoveAt(selectedRule);
        Change(owner, "Overrides", next, old, "Remove ViewSystem override"); selectedRule = -1; RefreshRules();
    }
    private void Change(Resource owner, StringName name, Variant next, Variant previous, string title)
    {
        var undo = GetUndoRedo(); undo.CreateAction(title, customContext: catalog);
        undo.ForceFixedHistory();undo.AddDoProperty(owner, name, next); undo.ForceFixedHistory();undo.AddUndoProperty(owner, name, previous);
        undo.AddDoMethod(this, MethodName.RefreshDocument); undo.AddUndoMethod(this, MethodName.RefreshDocument);
        undo.CommitAction();
    }
    public void UndoDocument()
    {
        if (catalog == null) return;
        GetUndoRedo().GetHistoryUndoRedo(GetUndoRedo().GetObjectHistoryId(catalog)).Undo(); RefreshRules();
    }
    public void RedoDocument()
    {
        if (catalog == null) return;
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
    {
        if (catalog == null) return;
        FileDialog("*.tscn ; UI scene", path => Try(() =>
        {
            var old = catalog.Entries; var next = new global::Godot.Collections.Array<ViewEntry>(old);
            string id = System.IO.Path.GetFileNameWithoutExtension(path); while (next.Any(item => item.Id == id)) id += "2";
            next.Add(new ViewEntry { Id = id, Scene = ResourceLoader.Load<PackedScene>(path) });
            Change(catalog, "Entries", next, old, "Add ViewSystem page"); RefreshEntries(); SelectEntry(next.Count - 1);
        }));
    }
    private void RemoveEntry()
    {
        if (catalog == null || entry == null) return;
        var old = catalog.Entries; var next = new global::Godot.Collections.Array<ViewEntry>(old); next.Remove(entry);
        Change(catalog, "Entries", next, old, "Remove ViewSystem page"); RefreshEntries(); entry = null; if (next.Count > 0) SelectEntry(0); else ClearPreview();
    }
    private void AddElement()
    {
        if (entry == null) return;
        var owner = entry;
        FileDialog("*.tscn ; UI element", path => Try(() =>
        {
            var old = owner.Elements; var next = new global::Godot.Collections.Array<ViewElementPlacement>(old);
            string id = System.IO.Path.GetFileNameWithoutExtension(path); while (next.Any(item => item.Id == id)) id += "2";
            next.Add(new ViewElementPlacement { Id = id, Scene = ResourceLoader.Load<PackedScene>(path) });
            Change(owner, "Elements", next, old, "Add ViewSystem element"); SelectEntry(catalog!.Entries.IndexOf(owner)); elements.Select(next.Count); RefreshRules();
        }));
    }
    private void RemoveElement()
    {
        if (entry == null || elements.Selected <= 0) return;
        var old = entry.Elements; var next = new global::Godot.Collections.Array<ViewElementPlacement>(old); next.RemoveAt(elements.Selected - 1);
        Change(entry, "Elements", next, old, "Remove ViewSystem element"); SelectEntry(catalog!.Entries.IndexOf(entry));
    }
    private void OpenSource()
    {
        var scene = SelectedDefinition() switch { ViewEntry page => page.Scene, ViewElementPlacement placement => placement.Scene, _ => null };
        if (scene != null) EditorInterface.Singleton.OpenSceneFromPath(scene.ResourcePath);
    }
    public void RefreshPreview()
    {
        if (rebuilding || viewport == null) return;
        rebuilding = true;
        try
        {
            ClearPreview(); PreviewError = "";
            if (catalog == null || entry == null) return;
            catalog.Validate();
            viewport.Size = new Vector2I((int)width.Value, (int)height.Value);
            previewRoot = new Control { Name = "PreviewRoot", Size = viewport.Size, MouseFilter = Control.MouseFilterEnum.Ignore }; viewport.AddChild(previewRoot);
            if (catalog.ReferenceHeight > 0)
            {
                previewRoot.Size = new Vector2((float)viewport.Size.X / viewport.Size.Y * catalog.ReferenceHeight, catalog.ReferenceHeight);
                previewRoot.Scale = Vector2.One * ((float)viewport.Size.Y / catalog.ReferenceHeight);
            }
            var context = new ViewContext(ViewModels.From(entry.PreviewModel), ViewModels.From(catalog.SharedPreviewModel), true,
                (action, _) => status.Text = "預覽事件：" + action);
            MountPreview(entry, context);
            if (entry.Kind == ViewKind.Page && entry.State.Length > 0)
                foreach (var state in catalog.Entries.Where(item => item.Kind == ViewKind.State && item.State == entry.State)) MountPreview(state, context);
            if (overlays.Selected > 0)
            {
                string id = overlays.GetItemMetadata(overlays.Selected).AsString(); var overlay = catalog.Entries.First(item => item.Id == id);
                MountPreview(overlay, context with { Page = ViewModels.From(overlay.PreviewModel) });
            }
            selection.Text = ""; selection.Visible=false;
            status.Text = "";
        }
        catch (Exception error) { PreviewError = error.Message; status.Text = "預覽失敗：" + error.Message; ClearPreview(); }
        finally { lastSignature = catalog == null ? "" : Signature(catalog); rebuilding = false; FitPreview(); }
    }
    private void MountPreview(ViewEntry definition, ViewContext context)
    {
        var instance = ViewComposer.Create(definition, context); previews.Add(instance); previewRoot!.AddChild(instance.Root);
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
        FitPreview(); elapsed += delta; if (elapsed < .25 || catalog == null) return; elapsed = 0;
        if (Signature(catalog) != lastSignature) RefreshDocument();
    }
    public void RefreshDocument()
    {
        if (catalog == null) return;
        int selected = entry == null ? 0 : catalog.Entries.IndexOf(entry);
        int element = elements.Selected, overlay = overlays.Selected;
        RefreshEntries();
        if (catalog.Entries.Count == 0) { entry = null; elements.Clear(); RefreshRules(); RefreshPreview(); return; }
        SelectEntry(Math.Clamp(selected, 0, catalog.Entries.Count - 1));
        if (element >= 0 && element < elements.ItemCount) elements.Select(element);
        if (overlay >= 0 && overlay < overlays.ItemCount) overlays.Select(overlay);
        RefreshRules(); RefreshPreview();
    }
    private void ValidateDocument()
    {
        Try(() =>
        {
            int count = ValidateAllDefinitions();
            status.Text = $"{count} 個定義通過 Scene／目標／型別／Model／事件檢查。";
        });
    }
    public void SaveDocument()
    {
        if (catalog == null || catalog.ResourcePath.Length == 0) throw new InvalidOperationException("請先開啟已有路徑的 catalog。");
        foreach (var resource in AuthoredResources(catalog).Distinct())
            if (!string.IsNullOrEmpty(resource.ResourcePath) && !resource.IsBuiltIn())
            {
                var error = ResourceSaver.Save(resource); if (error != Error.Ok) throw new IOException($"無法儲存 {resource.ResourcePath}: {error}");
            }
        var saved = ResourceSaver.Save(catalog); if (saved != Error.Ok) throw new IOException("儲存失敗：" + saved);
        savedSignature = Signature(catalog); EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(catalog.ResourcePath); if (GodotObject.IsInstanceValid(status)){status.Text = "Saved";status.TooltipText=catalog.ResourcePath;}
    }
    public override void _SaveExternalData() { if (catalog != null && Signature(catalog) != savedSignature) Try(SaveDocument); }
    public override string _GetUnsavedStatus(string forScene) => catalog != null && Signature(catalog) != savedSignature ? "ViewSystem：" + catalog.ResourcePath : "";
    private static IEnumerable<Resource> AuthoredResources(ViewCatalog document)
    {
        yield return document.SharedPreviewModel;
        foreach (var page in document.Entries)
        {
            if (page == null) continue;
            yield return page.PreviewModel;
            foreach (var item in page.Overrides) if (item != null) yield return item;
            foreach (var item in page.Events) if (item != null) yield return item;
            foreach (var element in page.Elements)
            {
                if (element == null) continue;
                if (element.Layout != null) yield return element.Layout.Data;
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
        if (nativeBinder != null) nativeBinder.Call("disconnect_native", only);
    }
    private void ReleaseEditorUi()
    {
        SetProcess(false);
        DisconnectUi();
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
        panel = null!; entries = null!; rules = null!;
        elements = null!; overlays = null!; kinds = null!; scopes = null!;
        catalogPath = null!; target = null!; property = null!; value = null!; modelKey = null!;
        width = null!; height = null!; status = null!; selection = null!;
        previewArea = null!; previewContainer = null!; viewport = null!;
        ReleaseUnityWorkflow();
    }
    public void OnBeforeSerialize()
    {
        if (!GodotObject.IsInstanceValid(panel)) return;
        using var state = new Godot.Collections.Dictionary
        {
            ["saved_signature"] = savedSignature, ["entry"] = catalog == null || entry == null ? -1 : catalog.Entries.IndexOf(entry),
            ["element"] = elements.Selected, ["overlay"] = overlays.Selected, ["rule"] = selectedRule,
            ["kind"] = kinds.Selected, ["scope"] = scopes.Selected,
            ["width"] = width.Value, ["height"] = height.Value, ["visible"] = panel.Visible,
            ["status"] = status.Text, ["preview_error"] = PreviewError,
            ["workspace_tab"] = workspaceTabs.CurrentTab, ["inspector_tab"] = inspectorTabs.CurrentTab,
            ["search"] = entrySearch.Text, ["filter"] = entryFilter.Selected,
            ["event_index"] = selectedEvent, ["definition_kind"] = definitionKind.Selected, ["definition_order"] = definitionOrder.Value,
            ["graph_scroll"] = pageGraph.ScrollOffset, ["graph_zoom"] = pageGraph.Zoom
        };
        if (catalog != null) state["catalog"] = catalog;
        if(rectClipboard!=null)state["rect_clipboard"]=rectClipboard;
        foreach (var field in layoutFields) state["layout_draft_"+field.Key] = field.Value.Value;
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
        if(state.TryGetValue("filter",out var filter)) entryFilter.Select(filter.AsInt32());
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
        width.Value = state["width"].AsDouble(); height.Value = state["height"].AsDouble();
        RefreshPreview();
        foreach (var field in layoutFields)
            if(state.TryGetValue("layout_draft_"+field.Key,out var draft)) field.Value.Value=draft.AsDouble();
        foreach (var field in EditorFormFields())
        {
            field.Text = state[field.Name].AsString();
            field.CaretColumn = state[field.Name + "_caret"].AsInt32();
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
    }
    public override void _ExitTree() => ReleaseEditorUi();
}
#endif
