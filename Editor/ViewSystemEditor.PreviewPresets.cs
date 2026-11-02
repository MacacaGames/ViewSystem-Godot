#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;

public partial class ViewSystemEditor
{
    private sealed record PreviewPreset(string Id,string Name,Vector2I Size,bool Custom=false)
    {public string Caption=>Name.Length==0?$"{Size.X} × {Size.Y}":$"{Name} · {Size.X} × {Size.Y}";}
    private const string PreviewSettings="macaca_viewsystem_preview";
    private const int AddPreviewPreset=10000,EditPreviewPreset=10001,RemovePreviewPreset=10002;
    private readonly List<PreviewPreset> previewPresets=new();
    private Vector2I previewSize=new(1080,1920);
    private string previewPresetId="",editingPreviewPresetId="";
    private MenuButton previewPresetMenu=null!;
    private ConfirmationDialog previewPresetDialog=null!;
    private LineEdit previewPresetName=null!;
    private SpinBox previewPresetWidth=null!,previewPresetHeight=null!;
    private Label previewPresetError=null!;

    private void BuildPreviewPresets(Node toolbar)
    {
        LoadPreviewPresets();
        previewPresetMenu=new MenuButton{Name="PreviewPreset",Flat=false,TooltipText="預覽尺寸",CustomMinimumSize=new Vector2(210,0)};toolbar.AddChild(previewPresetMenu);
        previewPresetMenu.Icon=previewPresetMenu.GetThemeIcon("arrow","OptionButton");previewPresetMenu.IconAlignment=HorizontalAlignment.Right;
        ConnectUi(previewPresetMenu.GetPopup(),PopupMenu.SignalName.IdPressed,nameof(OnPreviewPresetChosen));
        Button(toolbar,"⇄","RotatePreview",RotatePreviewPreset).TooltipText="旋轉目前尺寸";
        previewPresetDialog=new ConfirmationDialog{Name="PreviewPresetDialog",Title="新增預覽尺寸",DialogHideOnOk=false,MinSize=new Vector2I(360,0)};panel.AddChild(previewPresetDialog);
        previewPresetDialog.GetOkButton().Text="儲存";previewPresetDialog.GetCancelButton().Text="取消";
        var fields=new VBoxContainer();previewPresetDialog.AddChild(fields);
        previewPresetName=LabeledField(fields,"名稱","PreviewPresetName","可留空","");previewPresetName.MaxLength=64;
        var dimensions=new HBoxContainer();fields.AddChild(dimensions);
        dimensions.AddChild(new Label{Text="寬"});previewPresetWidth=PreviewDimension(dimensions,"PreviewPresetWidth");
        dimensions.AddChild(new Label{Text="高"});previewPresetHeight=PreviewDimension(dimensions,"PreviewPresetHeight");
        previewPresetError=new Label{Name="PreviewPresetError",Visible=false,AutowrapMode=TextServer.AutowrapMode.WordSmart};fields.AddChild(previewPresetError);
        ConnectUi(previewPresetDialog,ConfirmationDialog.SignalName.Confirmed,nameof(SavePreviewPreset));
        RefreshPreviewPresetMenu();
    }
    private static SpinBox PreviewDimension(Node owner,string name)
    {var field=new SpinBox{Name=name,MinValue=64,MaxValue=4096,Step=1,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(115,0)};owner.AddChild(field);return field;}
    private void LoadPreviewPresets()
    {
        previewPresets.Clear();
        foreach(var size in new[]{new Vector2I(1080,1920),new(1920,1080),new(720,1280),new(1280,720),new(1080,2340),new(2340,1080),new(1170,2532),new(2532,1170),new(1536,2048),new(2048,1536),new(2560,1440),new(3840,2160)})
            previewPresets.Add(new("builtin:"+size.X+"x"+size.Y,"",size));
        var settings=EditorInterface.Singleton.GetEditorSettings();
        using var state=settings.GetProjectMetadata(PreviewSettings,"presets",new Godot.Collections.Dictionary()).AsGodotDictionary();
        if(state.TryGetValue("custom",out var saved)&&saved.VariantType==Variant.Type.Array)
        {
            using var items=saved.AsGodotArray();
            foreach(var item in items)
            {
                if(item.VariantType!=Variant.Type.Dictionary)continue;
                using var data=item.AsGodotDictionary();
                if(!data.TryGetValue("id",out var id)||id.VariantType!=Variant.Type.String||!data.TryGetValue("size",out var size)||size.VariantType!=Variant.Type.Vector2I)continue;
                var pixels=size.AsVector2I();string key=id.AsString();
                if(!ValidPreviewSize(pixels)||!key.StartsWith("custom:",StringComparison.Ordinal)||previewPresets.Any(p=>p.Id==key||p.Size==pixels))continue;
                previewPresets.Add(new(key,data.TryGetValue("name",out var name)?name.AsString():"",pixels,true));
            }
        }
        previewPresetId=state.TryGetValue("selected",out var selected)?selected.AsString():previewPresets[0].Id;
        var preset=previewPresets.FirstOrDefault(p=>p.Id==previewPresetId)??previewPresets[0];previewPresetId=preset.Id;previewSize=preset.Size;
    }
    private void PersistPreviewPresets()
    {
        using var items=new Godot.Collections.Array();
        foreach(var preset in previewPresets.Where(p=>p.Custom))
        {using var data=new Godot.Collections.Dictionary{{"id",preset.Id},{"name",preset.Name},{"size",preset.Size}};items.Add(data);}
        using var state=new Godot.Collections.Dictionary{{"selected",previewPresetId},{"custom",items}};
        EditorInterface.Singleton.GetEditorSettings().SetProjectMetadata(PreviewSettings,"presets",state);
    }
    private void RefreshPreviewPresetMenu()
    {
        if(previewPresetMenu==null)return;
        var menu=previewPresetMenu.GetPopup();menu.Clear();bool custom=false;
        for(int i=0;i<previewPresets.Count;i++)
        {
            var preset=previewPresets[i];if(preset.Custom&&!custom){menu.AddSeparator();custom=true;}
            menu.AddRadioCheckItem(preset.Caption,i);menu.SetItemChecked(menu.ItemCount-1,preset.Id==previewPresetId);
        }
        menu.AddSeparator();menu.AddItem("＋ 自訂尺寸…",AddPreviewPreset);menu.AddItem("編輯目前尺寸…",EditPreviewPreset);menu.AddItem("移除目前尺寸",RemovePreviewPreset);
        bool editable=previewPresets.Any(p=>p.Id==previewPresetId&&p.Custom);
        menu.SetItemDisabled(menu.GetItemIndex(EditPreviewPreset),!editable);menu.SetItemDisabled(menu.GetItemIndex(RemovePreviewPreset),!editable);
        previewPresetMenu.Text=previewPresets.FirstOrDefault(p=>p.Id==previewPresetId)?.Caption??$"{previewSize.X} × {previewSize.Y}";
    }
    private void OnPreviewPresetChosen(long id)
    {
        if(id==AddPreviewPreset){OpenPreviewPresetDialog(false);return;}
        if(id==EditPreviewPreset){OpenPreviewPresetDialog(true);return;}
        if(id==RemovePreviewPreset)
        {if(previewPresets.Any(p=>p.Id==previewPresetId&&p.Custom)){previewPresets.RemoveAll(p=>p.Id==previewPresetId);ApplyPreviewPreset(previewPresets[0]);}return;}
        if(id>=0&&id<previewPresets.Count)ApplyPreviewPreset(previewPresets[(int)id]);
    }
    private void ApplyPreviewPreset(PreviewPreset preset)
    {previewPresetId=preset.Id;previewSize=preset.Size;PersistPreviewPresets();RefreshPreviewPresetMenu();if(viewport!=null)RefreshPreview();}
    private void OpenPreviewPresetDialog(bool edit)
    {
        var preset=previewPresets.FirstOrDefault(p=>p.Id==previewPresetId);
        if(edit&&preset?.Custom!=true)return;
        editingPreviewPresetId=edit?previewPresetId:"";previewPresetDialog.Title=edit?"編輯預覽尺寸":"新增預覽尺寸";
        previewPresetName.Text=edit?preset!.Name:"";previewPresetWidth.Value=previewSize.X;previewPresetHeight.Value=previewSize.Y;
        previewPresetError.Hide();previewPresetDialog.PopupCentered();previewPresetName.GrabFocus();
    }
    private void SavePreviewPreset()
    {
        previewPresetWidth.Apply();previewPresetHeight.Apply();
        var size=new Vector2I((int)previewPresetWidth.Value,(int)previewPresetHeight.Value);
        if(!ValidPreviewSize(size)){PreviewPresetFailure("寬、高須介於 64 與 4096。");return;}
        if(previewPresets.Any(p=>p.Size==size&&p.Id!=editingPreviewPresetId)){PreviewPresetFailure("已有相同尺寸的 preset，請直接選用。");return;}
        string id=editingPreviewPresetId.Length>0?editingPreviewPresetId:"custom:"+Guid.NewGuid().ToString("N");
        var preset=new PreviewPreset(id,previewPresetName.Text.Trim(),size,true);int index=previewPresets.FindIndex(p=>p.Id==id);
        if(index>=0)previewPresets[index]=preset;else previewPresets.Add(preset);
        previewPresetDialog.Hide();ApplyPreviewPreset(preset);
    }
    private void PreviewPresetFailure(string error){previewPresetError.Text=error;previewPresetError.Show();}
    private static bool ValidPreviewSize(Vector2I size)=>size.X>=64&&size.Y>=64&&size.X<=4096&&size.Y<=4096;
    private void SelectPreviewSize(Vector2I size,bool persist=true)
    {
        if(!ValidPreviewSize(size))throw new ArgumentOutOfRangeException(nameof(size),"Preview dimensions must be between 64 and 4096.");
        var preset=previewPresets.FirstOrDefault(p=>p.Size==size);
        if(preset==null){preset=new("custom:"+Guid.NewGuid().ToString("N"),"",size,true);previewPresets.Add(preset);}
        if(persist)ApplyPreviewPreset(preset);else{previewPresetId=preset.Id;previewSize=size;RefreshPreviewPresetMenu();}
    }
    private void RotatePreviewPreset()=>SelectPreviewSize(new(previewSize.Y,previewSize.X));
    private void ReleasePreviewPresets()
    {previewPresetMenu=null!;previewPresetDialog=null!;previewPresetName=null!;previewPresetWidth=null!;previewPresetHeight=null!;previewPresetError=null!;editingPreviewPresetId="";}
}
#endif
