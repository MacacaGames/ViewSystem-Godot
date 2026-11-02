#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Globalization;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private ViewSystemDebuggerPlugin? viewDebugger;
    public int GetRuntimeSnapshotCount()=>viewDebugger?.Snapshots.Count??0;
    public string GetLastRuntimeSnapshotJson()=>viewDebugger?.Snapshots.LastOrDefault() is {} snapshot?JsonSerializer.Serialize(snapshot,ViewDiagnosticsJsonContext.Default.ViewObjectGraphSnapshot):"";
    private AcceptDialog advisorDialog=null!;
    private ItemList advisorResults=null!;
    private OptionButton captureTag=null!;
    private ViewPolicyAdvice[] advice=Array.Empty<ViewPolicyAdvice>();
    private void BuildDiagnosticsTools(Node toolbar)
    {
        viewDebugger=new();AddDebuggerPlugin(viewDebugger);
        Button(toolbar,"Pool Advisor","PoolAdvisor",()=>{AnalyzePoolPolicy();advisorDialog.PopupCentered();});
        advisorDialog=new AcceptDialog {Name="PoolAdvisorDialog",Title="Pool Policy Advisor"};panel.AddChild(advisorDialog);
        var body=new VBoxContainer();advisorDialog.AddChild(body);
        advisorResults=new ItemList {Name="PoolAdvice",CustomMinimumSize=new Vector2(700,320)};body.AddChild(advisorResults);
        var bar=new HFlowContainer();body.AddChild(bar);
        captureTag=new OptionButton {Name="SnapshotTag"};bar.AddChild(captureTag);foreach(string tag in new[]{"baseline","open","returned"})captureTag.AddItem(tag);
        Button(bar,"Capture Runtime","CaptureObjectGraph",()=>
        {status.Text=viewDebugger.Capture(captureTag.GetItemText(captureTag.Selected))?"已向執行中的主 ViewHost 要求 snapshot":"先執行啟用 IsPrimaryHost 的場景並連接 Godot debugger。";});
        Button(bar,"Add Runtime","AddRuntimeSnapshot",()=>FileDialog("*.json ; ViewSystem Object Graph",path=>Try(()=>
        {
            var snapshot=JsonSerializer.Deserialize(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath(path)),ViewDiagnosticsJsonContext.Default.ViewObjectGraphSnapshot)??throw new InvalidOperationException("Snapshot is empty.");
            if(snapshot.SchemaVersion!=1)throw new InvalidOperationException("Unsupported snapshot schema.");viewDebugger.Snapshots.Add(snapshot);AnalyzePoolPolicy();
        })));
        Button(bar,"Analyze","AnalyzePoolPolicy",AnalyzePoolPolicy);
        Button(bar,"Export Results","ExportPoolAdvice",()=>SaveDiagnostics(JsonSerializer.Serialize(advice,ViewDiagnosticsJsonContext.Default.ViewPolicyAdviceArray),"viewsystem-policy-advisor.json"));
        Button(bar,"Export Snapshot","ExportObjectGraph",()=>{if(viewDebugger.Snapshots.LastOrDefault() is {} snapshot)SaveDiagnostics(JsonSerializer.Serialize(snapshot,ViewDiagnosticsJsonContext.Default.ViewObjectGraphSnapshot),"viewsystem-object-graph.json");});
    }
    private void AnalyzePoolPolicy()
    {
        if(catalog==null)return;advice=ViewPoolPolicyAdvisor.Analyze(catalog,viewDebugger?.Snapshots);advisorResults.Clear();
        foreach(var row in advice)
        {
            int index=advisorResults.AddItem($"{row.Scene} → {row.TargetPolicy}{(row.TargetPolicy=="KeepN"?" "+row.TargetKeepCount:"")}");
            advisorResults.SetItemTooltip(index,$"{row.SerializedNodes} serialized nodes · {row.AuthoredUses} uses · queued peak {row.QueuedPeak}\n{row.MigrationStatus}\n"+string.Join("\n",row.SafetyBlockers.Concat(row.StaticHints))+"\n"+row.NextAction);
        }
        status.Text=$"{advice.Length} scenes · {viewDebugger?.Snapshots.Count??0} runtime snapshots";
    }
    private void SaveDiagnostics(string text,string name)
    {
        var dialog=new EditorFileDialog {FileMode=EditorFileDialog.FileModeEnum.SaveFile,Access=EditorFileDialog.AccessEnum.Resources,Filters=new[]{"*.json ; JSON"},CurrentFile=name};AddChild(dialog);
        string key=dialog.GetInstanceId().ToString(CultureInfo.InvariantCulture);dialogs.Add(key,(dialog,path=>System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(path),text)));
        ConnectUi(dialog,Godot.FileDialog.SignalName.FileSelected,nameof(OnDialogSelected),key);ConnectUi(dialog,Godot.FileDialog.SignalName.Canceled,nameof(OnDialogCanceled),key);dialog.PopupCenteredRatio(.65f);
    }
    private void ReleaseDiagnosticsTools()
    {
        if(viewDebugger!=null){viewDebugger.ReleaseSessions();RemoveDebuggerPlugin(viewDebugger);viewDebugger.Dispose();viewDebugger=null;}
        advisorDialog=null!;advisorResults=null!;captureTag=null!;advice=Array.Empty<ViewPolicyAdvice>();
    }
}
#endif
