#if TOOLS
using Godot;
using System;
using System.Linq;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private EditorInspector? advancedInspector;
    private Resource? advancedOwner,advancedProxy;
    private string advancedRevision="";
    private string[] advancedFields=Array.Empty<string>();
    private void OpenAdvancedSettings()
    {
        if(SelectedDefinition() is not Resource owner)return;
        InspectAdvancedOwner(owner);
    }
    private void InspectAdvancedOwner(Resource owner)
    {
        ReleaseAdvancedSettings();advancedOwner=owner;
        advancedProxy=(Resource)GD.Load<GDScript>("res://addons/macaca_viewsystem/Editor/selection_settings_proxy.gd").New().AsGodotObject();
        advancedProxy.ResourceName=owner is ViewEntry page?page.Id:((ViewElementPlacement)owner).DisplayName;
        ConnectUi(advancedProxy,"setting_changed",nameof(OnAdvancedSettingChanged));
        advancedInspector=EditorInterface.Singleton.GetInspector();
        ConnectUi(advancedInspector,EditorInspector.SignalName.PropertyEdited,nameof(OnAdvancedPropertyEdited));
        ConfigureAdvancedSettings();EditorInterface.Singleton.EditResource(advancedProxy);
    }
    private void ConfigureAdvancedSettings()
    {
        if(advancedOwner==null||advancedProxy==null)return;
        advancedFields=advancedOwner.GetPropertyList().Where(p=>
        {string name=p["name"].AsString();var usage=(PropertyUsageFlags)p["usage"].AsInt64();return name!="script"&&!name.StartsWith("resource_",StringComparison.Ordinal)&&(usage&(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage))==(PropertyUsageFlags.Editor|PropertyUsageFlags.Storage)&&(usage&PropertyUsageFlags.ReadOnly)==0;}).Select(p=>p["name"].AsString()).ToArray();
        advancedProxy.Call("configure",advancedOwner,advancedFields);advancedRevision=catalog==null?"":Signature(catalog);
    }
    private void RefreshAdvancedSettings()
    {
        if(advancedOwner==null||advancedProxy==null||advancedInspector?.GetEditedObject()!=advancedProxy)return;
        if(!BelongsToDocument(advancedOwner)){ReleaseAdvancedSettings();return;}
        string revision=Signature(catalog!);if(revision==advancedRevision)return;
        if(preservingContextControls&&ContextValuesMatch(advancedOwner,advancedProxy,advancedFields)){advancedRevision=revision;return;}
        advancedInspector.Edit(null);ConfigureAdvancedSettings();advancedInspector.Edit(advancedProxy);
    }
    private void OnAdvancedSettingChanged(StringName property,Variant next)
    {if(advancedOwner!=null&&BelongsToDocument(advancedOwner))QueueContextSetting(advancedOwner,property,next);}
    private void OnAdvancedPropertyEdited(StringName path)
    {
        if(advancedOwner==null||advancedProxy==null||referenceRouter==null)return;
        string field=path.ToString().Split('/',':')[0];
        if(advancedOwner.GetPropertyList().Any(p=>p["name"].AsString()==field)&&!ViewReferenceDiff.Equivalent(advancedOwner.Get(field),advancedProxy.Get(field),referenceRouter))OnAdvancedSettingChanged(field,advancedProxy.Get(field));
    }
    private void ReleaseAdvancedSettings()
    {
        if(GodotObject.IsInstanceValid(advancedInspector))
        {if(advancedInspector!.GetEditedObject()==advancedProxy)advancedInspector.Edit(null);DisconnectUi(advancedInspector);}
        if(advancedProxy!=null){DisconnectUi(advancedProxy);advancedProxy.Dispose();}
        advancedInspector=null;advancedProxy=null;advancedOwner=null;advancedRevision="";advancedFields=Array.Empty<string>();
    }
}
#endif
