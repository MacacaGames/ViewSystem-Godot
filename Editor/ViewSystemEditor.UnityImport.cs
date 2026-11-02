#if TOOLS
using Godot;
using System;
using System.Linq;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private void BrowseUnityBundle()
    {
        var dialog=new EditorFileDialog{Name="UnityBundleDialog",Title="Unity → Godot",FileMode=EditorFileDialog.FileModeEnum.OpenFile,Access=EditorFileDialog.AccessEnum.Filesystem,Filters=new[]{"manifest.json ; Unity asset bundle"}};AddChild(dialog);
        string key=dialog.GetInstanceId().ToString(System.Globalization.CultureInfo.InvariantCulture);
        dialogs.Add(key,(dialog,path=>Try(()=>
        {
            string output="res://UnityImports/Import_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff",System.Globalization.CultureInfo.InvariantCulture);
            var result=new UnityAssetConverter().Import(path,output);EditorInterface.Singleton.GetResourceFilesystem().Scan();
            var report=new AcceptDialog{Title="Unity → Godot",DialogText=$"{result.Converted} assets\n{result.Issues.Count} adapter items\n{output}/conversion-report.json",Exclusive=false};panel.AddChild(report);report.PopupCentered();
            status.Text=result.Complete?"匯入完成":"匯入完成；請查看 conversion-report.json";
        })));
        ConnectUi(dialog,Godot.FileDialog.SignalName.FileSelected,nameof(OnDialogSelected),key);ConnectUi(dialog,Godot.FileDialog.SignalName.Canceled,nameof(OnDialogCanceled),key);dialog.PopupCenteredRatio(.7f);
    }
}
#endif
