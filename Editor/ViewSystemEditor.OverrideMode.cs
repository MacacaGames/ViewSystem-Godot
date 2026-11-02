#if TOOLS
using Godot;
using System;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private Button? rawOverrideToggle;
    private Control? rawOverrideContent,legacyModelContent;
    private LineEdit? rawOverrideValue;
    private bool rawOverrideSync;
    private readonly Dictionary<string,bool> overrideRawModes=new();
    private string OverrideModeKey(Resource? owner=null,Resource? binding=null,bool explicitBinding=false)=>
        (owner??SelectedDefinition())?.GetInstanceId()+"|"+(explicitBinding?binding:selectedRuleResource)?.GetInstanceId();
    private bool RawOverrideRequested=>overrideRawModes.GetValueOrDefault(OverrideModeKey());
    private bool HasOverrideModel=>!string.IsNullOrWhiteSpace(modelKey.Text);
    private static bool IsRawModelExpression(string text)
    {
        foreach(string scope in new[]{"PageModel","SharedModel","Model","PageFirst","PageOnly","SharedFirst","SharedOnly"})
            if(text.StartsWith("{"+scope+".",StringComparison.Ordinal))return true;
        return false;
    }
    private void BuildOverrideMode(Node owner,Control nativeInspector,Control chooseProperty,Control legacyValueRow)
    {
        int position=nativeInspector.GetIndex();
        var header=new HBoxContainer{Name="OverrideValueHeader"};owner.AddChild(header);owner.MoveChild(header,position);
        header.AddChild(new Control{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});
        rawOverrideToggle=new Button{Name="RawOverrideToggle",Text="Raw Value",ToggleMode=true};header.AddChild(rawOverrideToggle);
        ConnectUi(rawOverrideToggle,BaseButton.SignalName.Toggled,nameof(OnRawOverrideToggled));
        var raw=new VBoxContainer{Name="RawOverrideContent",Visible=false};owner.AddChild(raw);owner.MoveChild(raw,nativeInspector.GetIndex()+1);rawOverrideContent=raw;
        rawOverrideValue=new LineEdit{Name="RawOverrideValue",TooltipText="固定值，或 {PageModel.Type}／{SharedModel.Type[\"key\"]}。修改此值會取代目前的 Model 來源。"};raw.AddChild(rawOverrideValue);
        ConnectUi(rawOverrideValue,LineEdit.SignalName.TextChanged,nameof(OnRawOverrideChanged));
        ConnectUi(rawOverrideValue,LineEdit.SignalName.TextSubmitted,nameof(OnFormSubmitted),"override");
        ConnectUi(rawOverrideValue,Control.SignalName.GuiInput,nameof(OnFormKey),"override");
        var legacy=new VBoxContainer{Name="LegacyModelSource",Visible=false};raw.AddChild(legacy);legacyModelContent=legacy;
        var label=owner.GetChild<Control>(modelKey.GetIndex()-1);
        foreach(Node node in new Node[]{label,modelKey,scopes}){owner.RemoveChild(node);legacy.AddChild(node);}
        // Keep old programmatic Value edits working without exposing a second editor.
        legacyValueRow.Hide();
    }
    private void RefreshOverrideMode()
    {
        if(rawOverrideToggle==null||rawOverrideContent==null||rawOverrideValue==null||overrideValueInspector==null)return;
        bool model=HasOverrideModel,raw=model||RawOverrideRequested;
        rawOverrideToggle.Visible=SelectedDefinition()!=null;
        rawOverrideToggle.SetPressedNoSignal(raw);rawOverrideToggle.Disabled=model;
        rawOverrideToggle.TooltipText=model?"此項目使用 Model 來源；在 Raw Value 改成固定值後才能返回型別欄位。":"顯示原始值／設定 Model 來源";
        rawOverrideContent.Visible=raw;overrideValueInspector.Visible=typedPropertyValid&&!raw;
        if(legacyModelContent!=null)legacyModelContent.Visible=model&&!modelKey.Text.TrimStart().StartsWith("{",StringComparison.Ordinal);
        string text=model&&modelKey.Text.TrimStart().StartsWith("{",StringComparison.Ordinal)?modelKey.Text:value.Text;
        if(rawOverrideValue.Text!=text){rawOverrideSync=true;try{rawOverrideValue.Text=text;}finally{rawOverrideSync=false;}}
    }
    private void OnRawOverrideChanged(string text)
    {
        if(rawOverrideSync)return;
        overrideRawModes[OverrideModeKey()]=true;
        if(IsRawModelExpression(text.Trim())){modelKey.Text=text;}
        else{modelKey.Text="";value.Text=text;typedValueEdited=false;}
        RefreshOverrideMode();RefreshBindingActions();
    }
    private void OnRawOverrideToggled(bool raw)
    {
        if(HasOverrideModel){RefreshOverrideMode();return;}
        if(raw)CaptureNestedTypedEdits();
        if(!raw&&typedPropertyValid)
        {
            try
            {
                var rule=BuildRule();var literal=rule.Literal;
                typedOverrideSync=true;try{overrideValueProxy!.Set("Value",literal);}finally{typedOverrideSync=false;}
                if(typedOverrideDraft==null||referenceRouter==null||!ViewReferenceDiff.Equivalent(typedOverrideDraft.NativeValue,literal,referenceRouter))OnTypedOverrideValueChanged(literal);
                ClearFormErrors();
            }
            catch(Exception error){ShowFormError("override",error.Message);RefreshOverrideMode();return;}
        }
        overrideRawModes[OverrideModeKey()]=raw;RefreshOverrideMode();
    }
    private void ResetNewOverrideMode()=>overrideRawModes.Remove(OverrideModeKey());
    private void CopyOverrideMode(Resource owner,Resource? oldBinding,Resource newBinding)
    {
        if(overrideRawModes.GetValueOrDefault(OverrideModeKey(owner,oldBinding,true)))overrideRawModes[OverrideModeKey(owner,newBinding,true)]=true;
    }
    private void ReleaseOverrideMode()
    {rawOverrideToggle=null;rawOverrideContent=null;legacyModelContent=null;rawOverrideValue=null;rawOverrideSync=false;}
}
#endif
