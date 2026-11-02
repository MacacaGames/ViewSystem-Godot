#if TOOLS
using Godot;
using System;
using System.Linq;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private string bindingFlow="";
    private Godot.Collections.Dictionary? bindingReturnLocation;
    private Godot.Collections.Dictionary? bindingReturnValues;
    private bool bindingReturnTyped;
    private bool restoringBindingReturn;
    private int bindingRevealFrames,bindingRevealKind;
    private void BeginOverride()
    {RememberBindingReturn("override");PrepareOverrideDraft();bindingFlow="override";OpenAuthoringPicker("Target",true);}
    private void BeginOverrideAtTarget(string path)
    {RememberBindingReturn("override");PrepareOverrideDraft();target.Text=path;property.Text="";RefreshTypedOverrideEditor(true);bindingFlow="override";OpenAuthoringPicker("Property",true);}
    private void PrepareOverrideDraft()
    {
        CaptureCurrentDraft();returnToAuthoringMember="";targetPickerSearch="";
        selectedRule=-1;selectedRuleResource=null;rules.DeselectAll();overrideHierarchy?.DeselectAll();value.Text="";modelKey.Text="";
        ResetNewOverrideMode();
        var root=AuthoringRoot();if(root?.GetNodeOrNull(new NodePath(target.Text))==null)target.Text=".";
        property.Text=root!=null&&OverrideOwner(root,target.Text,"text",out _)!=null?"text":"";
        RefreshTypedOverrideEditor(true);RefreshBindingActions();RebaseForm("override");
    }
    private void BeginEvent(){RememberBindingReturn("event");PrepareEventDraft();bindingFlow="event";OpenAuthoringPicker("EventTarget",true);}
    private void BeginEventAtTarget(string path){RememberBindingReturn("event");PrepareEventDraft();eventTarget.Text=path;bindingFlow="event";OpenAuthoringPicker("EventSignal",true);}
    private void RememberBindingReturn(string kind)
    {CaptureCurrentDraft();ReleaseBindingReturn();bindingReturnLocation=CaptureEditorLocation();bindingReturnValues=FormValues(kind);bindingReturnTyped=typedValueEdited;}
    private void ReleaseBindingReturn()
    {bindingReturnLocation?.Dispose();bindingReturnValues?.Dispose();bindingReturnLocation=null;bindingReturnValues=null;}
    private void CancelAuthoringPicker()
    {
        string kind=bindingFlow;bindingFlow="";returnToAuthoringMember="";authoringPopup.Hide();
        if(kind.Length==0)return;
        restoringBindingReturn=true;
        try
        {
            if(SelectedDefinition() is {} owner&&FindDraft(owner,kind,DraftBinding(kind)) is {} draft)DropDraft(draft);
            if(bindingReturnLocation!=null)RestoreEditorLocation(bindingReturnLocation);
            if(bindingReturnValues!=null){restoringDrafts=true;try{RestoreForm(kind,bindingReturnValues);if(kind=="override")typedValueEdited=bindingReturnTyped;}finally{restoringDrafts=false;}}
        }
        finally{restoringBindingReturn=false;ReleaseBindingReturn();}
    }
    private void OnAuthoringPickerHidden(){if(!choosingAuthoringChoice&&bindingFlow.Length>0)CancelAuthoringPicker();}
    private void PrepareEventDraft()
    {
        CaptureCurrentDraft();returnToAuthoringMember="";targetPickerSearch="";
        selectedEvent=-1;selectedEventResource=null;eventNativeHandler=false;eventRelativeHandler=false;eventReceiverDraft=false;eventBindings.DeselectAll();eventHierarchy?.DeselectAll();var node=AuthoringRoot()?.GetNodeOrNull(new NodePath(eventTarget.Text));
        if(node==null){eventTarget.Text=".";node=AuthoringRoot();}
        eventSignal.Text=node?.HasSignal("pressed")==true?"pressed":"";eventAction.Text="";eventHandler.Text="";forwardEventArguments.ButtonPressed=false;RefreshBindingActions();RebaseForm("event");
    }
    private void ContinueBindingFlow(string key)
    {
        if(bindingFlow=="override"&&key=="Target"){property.Text="";RefreshTypedOverrideEditor(true);OpenAuthoringPicker("Property",true);}
        else if(bindingFlow=="override"&&key=="Property"){bindingFlow="";CreateOverrideFromTree();}
        else if(bindingFlow=="event"&&key=="EventTarget"){eventSignal.Text="";OpenAuthoringPicker("EventSignal",true);}
        else if(bindingFlow=="event"&&key=="EventSignal"){bindingFlow="";CreateEventFromTree();}
        else if(bindingFlow=="event"&&key=="EventAction"){bindingFlow="";RevealBindingEditor(true);}
    }
    private void CreateOverrideFromTree()
    {
        var existing=SelectedRules().ToList().FindIndex(r=>r.Target.ToString()==target.Text&&r.Property==property.Text);
        if(existing>=0){LoadRule(existing);RefreshBindingTrees();}else ApplyRule();RevealBindingEditor(false);
    }
    private void CreateEventFromTree()
    {
        if(SelectedDefinition() is not Resource owner)return;
        AcceptCurrentDraft("event");
        int existing=SelectedEvents().ToList().FindIndex(e=>e.Target.ToString()==eventTarget.Text&&e.Signal==eventSignal.Text);
        if(existing<0){var previous=SelectedEvents();var next=new Godot.Collections.Array<ViewEventBinding>(previous){new(){Target=eventTarget.Text,Signal=eventSignal.Text,Enabled=false}};existing=next.Count-1;Change(owner,"Events",next,previous,"Add ViewSystem event");}
        OnEventSelected(existing);eventBindings.Select(existing);RefreshBindingTrees();RevealBindingEditor(true);eventReceiverButton.GrabFocus();
    }
    private void RevealBindingEditor(bool events)
    {
        if(inspectorSections.TryGetValue(events?"事件":"覆寫",out var section))section.GetNode<Control>("Content").Show();
        bindingRevealKind=events?2:1;bindingRevealFrames=3;
    }
    private void RevealBindingEditorAfterLayout()
    {
        if(bindingRevealFrames<=0||--bindingRevealFrames>0)return;
        var control=bindingRevealKind==2?(Control)eventReceiverButton:overrideValueInspector?.Visible==true?overrideValueInspector:property;
        ((ScrollContainer)panel.FindChild("SelectionInspector",true,false)).EnsureControlVisible(control);
        if(bindingRevealKind==1){var input=control.FindChildren("*","Control",true,false).OfType<Control>().FirstOrDefault(c=>c.IsVisibleInTree()&&c.FocusMode is Control.FocusModeEnum.All or Control.FocusModeEnum.Click&&c is LineEdit or TextEdit or BaseButton&&!(c is BaseButton button&&button.Disabled));input?.GrabFocus();}
    }
    private void RefreshBindingActions()
    {
        if(!GodotObject.IsInstanceValid(panel)||target==null||eventTarget==null||HasMeta(ReloadState))return;
        RefreshOverrideMode();
        RefreshEventMethods();
        foreach(string name in new[]{"NewOverride","NewEvent"})if(panel.FindChild(name,true,false) is Button create)create.Disabled=SelectedDefinition()==null;
        if(typedLookupKey!=CurrentTypedLookupKey())RefreshTypedOverrideEditor();
        bool valid=SelectedDefinition()!=null&&typedPropertyValid;
        if(panel.FindChild("ApplyOverride",true,false) is Button apply){apply.Disabled=!valid;apply.Text=selectedRule>=0?"套用修改":"新增覆寫";}
        if(panel.FindChild("RemoveOverride",true,false) is Button remove)remove.Disabled=selectedRule<0;
        var node=AuthoringRoot()?.GetNodeOrNull(new NodePath(eventTarget.Text));
        if(panel.FindChild("ApplyEvent",true,false) is Button eventApply){eventApply.Disabled=EventFormError().Length>0;eventApply.Text=selectedEvent>=0?"套用修改":"新增事件";}
        if(panel.FindChild("RemoveEvent",true,false) is Button eventRemove)eventRemove.Disabled=selectedEvent<0;
    }
}
#endif
