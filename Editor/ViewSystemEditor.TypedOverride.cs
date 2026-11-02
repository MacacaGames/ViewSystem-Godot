#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Globalization;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private EditorInspector? overrideValueInspector;
    private Resource? overrideValueProxy;
    private ViewPropertyOverride? typedOverrideDraft;
    private string typedOverrideKey="";
    private bool typedOverrideSync;
    private bool typedValueEdited;
    private bool typedPropertyValid;
    private string typedMirroredText="",typedLookupKey="";
    private Button? chooseOverrideProperty;
    private static ViewValueKind NativeValueKind(Variant input)=>input.VariantType switch {Variant.Type.String=>ViewValueKind.Text,Variant.Type.Int=>ViewValueKind.Integer,Variant.Type.Float=>ViewValueKind.Number,Variant.Type.Bool=>ViewValueKind.Boolean,Variant.Type.Color=>ViewValueKind.Color,Variant.Type.Vector2=>ViewValueKind.Vector2,Variant.Type.Vector3=>ViewValueKind.Vector3,Variant.Type.Vector2I=>ViewValueKind.Vector2I,Variant.Type.Vector3I=>ViewValueKind.Vector3I,Variant.Type.Object when input.AsGodotObject() is Resource=>ViewValueKind.Resource,_=>ViewValueKind.Native};
    private void BuildTypedOverrideEditor()
    {
        var row=value.GetParent();var owner=row.GetParent();
        owner.GetChild<Control>(row.GetIndex()-1).Name="LegacyValueLabel";
        overrideValueInspector=new EditorInspector {Name="OverrideValueInspector",CustomMinimumSize=new Vector2(0,80),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        owner.AddChild(overrideValueInspector);owner.MoveChild(overrideValueInspector,row.GetIndex()-1);
        chooseOverrideProperty=new Button{Name="ChooseOverrideProperty",Text="選擇屬性"};owner.AddChild(chooseOverrideProperty);owner.MoveChild(chooseOverrideProperty,overrideValueInspector.GetIndex());ConnectUi(chooseOverrideProperty,BaseButton.SignalName.Pressed,nameof(ChooseMissingOverrideProperty));
        BuildOverrideMode(owner,overrideValueInspector,chooseOverrideProperty,(Control)row);
        overrideValueProxy=(Resource)GD.Load<GDScript>("res://addons/macaca_viewsystem/Editor/override_value_proxy.gd").New().AsGodotObject();
        ConnectUi(overrideValueProxy,"value_changed",nameof(OnTypedOverrideValueChanged));
        ConnectUi(overrideValueInspector,EditorInspector.SignalName.PropertyEdited,nameof(OnTypedPropertyEdited));
    }
    private void ChooseMissingOverrideProperty()=>Try(()=>OpenAuthoringPicker("Property"));
    private void RefreshTypedOverrideEditor(bool reload=false)
    {
        if(overrideValueInspector==null||overrideValueProxy==null||HasMeta(ReloadState))return;
        typedLookupKey=CurrentTypedLookupKey();
        Godot.Collections.Dictionary? info=null;Variant current=default;
        WithAuthoringIconRoot(root=>{if(root!=null&&OverrideOwner(root,target.Text,property.Text,out _) is {} owner)
        {string leaf=property.Text.Split(':')[^1];info=owner.GetPropertyList().FirstOrDefault(p=>p["name"].AsString()==leaf);if(info!=null)current=owner.Get(leaf);}});
        bool supported=info!=null&&(PropertyUsageFlags)info["usage"].AsInt64() is var usage&&(usage&PropertyUsageFlags.ReadOnly)==0;
        typedPropertyValid=supported;overrideValueInspector.Visible=supported;value.GetParent<Control>().Visible=false;if(chooseOverrideProperty!=null)chooseOverrideProperty.Visible=!supported;
        if(panel.FindChild("LegacyValueLabel",true,false) is Control label)label.Visible=false;
        kinds.Visible=false;
        RefreshOverrideMode();
        if(!supported){overrideValueInspector.Edit(null);typedOverrideKey="";typedOverrideDraft=null;typedValueEdited=false;return;}
        string key=(SelectedDefinition() as Resource)?.GetInstanceId()+"|"+target.Text+"|"+property.Text+"|"+selectedRule;
        if(!reload&&key==typedOverrideKey)return;
        typedOverrideKey=key;
        Variant initial=current;
        if(selectedRule>=0&&selectedRule<SelectedRules().Count&&SelectedRules()[selectedRule] is {} rule&&!rule.IsModelBinding&&rule.Target.ToString()==target.Text&&rule.Property==property.Text)initial=rule.Literal;
        bool retainControls=committingAuto&&referenceRouter!=null&&ViewReferenceDiff.Equivalent(overrideValueProxy.Get("Value"),initial,referenceRouter);
        typedOverrideDraft=new ViewPropertyOverride {Kind=ViewValueKind.Native,NativeValue=initial};
        typedValueEdited=false;MirrorTypedValue(initial);
        if(retainControls){RefreshOverrideMode();return;}
        typedOverrideSync=true;
        try{overrideValueProxy.Call("configure",info!,initial);overrideValueInspector.Edit(overrideValueProxy);}
        finally{typedOverrideSync=false;}RefreshOverrideMode();
    }
    private void OnTypedOverrideValueChanged(Variant input)
    {
        if(typedOverrideSync)return;typedValueEdited=true;typedOverrideDraft=new(){Kind=ViewValueKind.Native,NativeValue=input};
        MirrorTypedValue(input);RefreshAuthoringIcons();
        if(Inside(CurrentInteractionFocus(),overrideValueInspector))ArmAutoForm("override");
    }
    private void OnTypedPropertyEdited(StringName property)
    {if(property.ToString().StartsWith("Value",StringComparison.Ordinal))CaptureNestedTypedEdits();}
    private void CaptureNestedTypedEdits()
    {
        if(RawOverrideRequested||HasOverrideModel)return;
        if(typedOverrideSync||overrideValueProxy==null||typedOverrideDraft==null||referenceRouter==null||!typedPropertyValid)return;
        var current=overrideValueProxy.Get("Value");
        if(!ViewReferenceDiff.Equivalent(typedOverrideDraft.NativeValue,current,referenceRouter))OnTypedOverrideValueChanged(current);
    }
    private void MirrorTypedValue(Variant input)
    {
        var kind=NativeValueKind(input);
        kinds.Select((int)kind);
        value.Text=kind switch {ViewValueKind.Color=>"#"+input.AsColor().ToHtml(),ViewValueKind.Vector2=>FormattableString.Invariant($"{input.AsVector2().X},{input.AsVector2().Y}"),ViewValueKind.Vector3=>FormattableString.Invariant($"{input.AsVector3().X},{input.AsVector3().Y},{input.AsVector3().Z}"),ViewValueKind.Vector2I=>$"{input.AsVector2I().X},{input.AsVector2I().Y}",ViewValueKind.Vector3I=>$"{input.AsVector3I().X},{input.AsVector3I().Y},{input.AsVector3I().Z}",ViewValueKind.Number=>input.AsDouble().ToString(CultureInfo.InvariantCulture),ViewValueKind.Resource=>(input.AsGodotObject() as Resource)?.ResourcePath??"",ViewValueKind.Native=>"",_=>input.AsString()};
        typedMirroredText=value.Text;
    }
    private string CurrentTypedLookupKey()=>SelectedDefinition()?.GetInstanceId()+"|"+AuthoringRoot()?.GetInstanceId()+"|"+target.Text+"|"+property.Text+"|"+selectedRule;
    private bool TryTypedLiteral(out Variant literal)
    {
        if(typedLookupKey!=CurrentTypedLookupKey())RefreshTypedOverrideEditor();literal=typedOverrideDraft?.NativeValue??default;
        // Legacy programmatic text edits remain supported, while the visible UI
        // always edits the native Variant and preserves its complete precision.
        return typedPropertyValid&&typedOverrideDraft!=null&&value.Text==typedMirroredText;
    }
    private static void AssignTypedLiteral(ViewPropertyOverride rule,Variant literal)
    {
        rule.Kind=NativeValueKind(literal);
        switch(rule.Kind)
        {
            case ViewValueKind.Text:rule.Text=literal.AsString();break;
            case ViewValueKind.Integer:rule.Integer=literal.AsInt64();break;
            case ViewValueKind.Number:rule.Number=literal.AsDouble();break;
            case ViewValueKind.Boolean:rule.Boolean=literal.AsBool();break;
            case ViewValueKind.Color:rule.Color=literal.AsColor();break;
            case ViewValueKind.Vector2:rule.Vector=literal.AsVector2();break;
            case ViewValueKind.Vector3:rule.Vector3=literal.AsVector3();break;
            case ViewValueKind.Vector2I:rule.Vector2I=literal.AsVector2I();break;
            case ViewValueKind.Vector3I:rule.Vector3I=literal.AsVector3I();break;
            case ViewValueKind.Resource:rule.Resource=literal.AsGodotObject() as Resource;break;
            default:rule.NativeValue=literal;break;
        }
    }
    private void FitTypedOverrideEditor()
    {
        if(overrideValueInspector?.Visible!=true)return;
        float height=overrideValueInspector.GetChildren(true).OfType<Container>().Where(c=>c.Visible).Select(c=>c.GetCombinedMinimumSize().Y).DefaultIfEmpty(72).Max();
        overrideValueInspector.CustomMinimumSize=new Vector2(0,Math.Clamp(height+8,80,420));
    }
    private void ReleaseTypedOverrideEditor()
    {ReleaseOverrideMode();overrideValueInspector?.Edit(null);overrideValueProxy?.Dispose();overrideValueProxy=null;overrideValueInspector=null;typedOverrideDraft=null;typedOverrideKey="";typedLookupKey="";typedPropertyValid=false;chooseOverrideProperty=null;}
}
#endif
