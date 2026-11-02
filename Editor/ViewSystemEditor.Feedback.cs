#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private readonly Dictionary<string,Label> formErrors=new();
    public string AuthoringError {get;private set;}="";
    private void BuildFormFeedback()
    {
        foreach(var pair in new[]{("definition","設定"),("name","元件"),("parent","元件"),("override","覆寫"),("event","事件")})
        {
            var label=new Label{Name="FormError"+pair.Item1,Visible=false,AutowrapMode=TextServer.AutowrapMode.WordSmart};label.AddThemeColorOverride("font_color",new Color(1,.4f,.4f));inspectorSections[pair.Item2].GetNode("Content").AddChild(label);formErrors[pair.Item1]=label;
        }
        foreach(var pair in new[]{(definitionId,"definition"),(definitionState,"definition"),(target,"override"),(property,"override"),(modelKey,"override"),(eventTarget,"event"),(eventSignal,"event"),(eventAction,"event"),(eventHandler,"event"),(parentPath,"parent")})
        {ConnectUi(pair.Item1,LineEdit.SignalName.TextSubmitted,nameof(OnFormSubmitted),pair.Item2);ConnectUi(pair.Item1,Control.SignalName.GuiInput,nameof(OnFormKey),pair.Item2);}
        if(panel.FindChild("ElementDisplayName",true,false) is LineEdit name)ConnectUi(name,Control.SignalName.GuiInput,nameof(OnFormKey),"name");
        ConnectUi(entries,Control.SignalName.GuiInput,nameof(OnNavigationKey));
        BuildInteractions();
    }
    private void ClearFormErrors()
    {AuthoringError="";foreach(var label in formErrors.Values)if(GodotObject.IsInstanceValid(label)){label.Text="";label.Hide();}}
    private void ShowFormError(string kind,string message)
    {
        AuthoringError=message;
        if(formErrors.TryGetValue(kind,out var label)&&GodotObject.IsInstanceValid(label)){label.Text=message;label.Show();var content=label.GetParent<Control>();content.Show();((ScrollContainer)panel.FindChild("SelectionInspector",true,false)).EnsureControlVisible(label);}
        else if(GodotObject.IsInstanceValid(status))status.Text=message;
    }
    private void RunFormAction(string kind,Action action)
    {try{ClearFormErrors();action();}catch(Exception error){ShowFormError(kind,error.Message);}}
    private void ToggleInspectorSection(string name)
    {
        if(!inspectorSections.TryGetValue(name,out var section))return;var content=section.GetNode<Control>("Content");content.Visible=!content.Visible;
        section.GetChild<Button>(0).Text=(content.Visible?"▾ ":"▸ ")+name;
    }
    private void OnFormSubmitted(string text,string kind)=>RunFormAction(kind,()=>
    {switch(kind){case "definition":ApplyDefinition();break;case "parent":ApplyParentPath();break;case "override":ApplyRule();break;case "event":ApplyEvent();break;}});
    private void OnFormKey(InputEvent input,string kind)
    {
        if(input is not InputEventKey {Pressed:true,Echo:false,Keycode:Key.Escape})return;
        if(SelectedDefinition() is {} owner&&FindDraft(owner,kind,DraftBinding(kind)) is {} draft)DropDraft(draft);
        if(formBaselines.TryGetValue(kind,out var baseline)){restoringDrafts=true;try{RestoreForm(kind,baseline);if(kind=="override")typedValueEdited=false;}finally{restoringDrafts=false;}}
        ClearFormErrors();panel.GetViewport().SetInputAsHandled();
    }
    private void OnNavigationKey(InputEvent input)
    {
        if(HandleNavigationShortcut(input))return;
        if(input is InputEventKey {Pressed:true,Echo:false,Keycode:Key.Escape}&&navigationEntry!=null)
        {CallDeferred(nameof(BackToViews));entries.AcceptEvent();}
    }
    private void FitContextSettings()
    {
        foreach(var inspector in new[]{entrySettings,elementSettings})if(GodotObject.IsInstanceValid(inspector)&&inspector.IsVisibleInTree())
        {float height=inspector.GetChildren(true).OfType<Container>().Where(c=>c.Visible).Select(c=>c.GetCombinedMinimumSize().Y).DefaultIfEmpty(0).Max();inspector.CustomMinimumSize=new(0,Math.Max(120,height+8));}
    }
}
#endif
