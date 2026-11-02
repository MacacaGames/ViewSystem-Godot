#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private readonly HashSet<ulong> interactionHooks=new();
    private string autoForm="";
    private Resource? autoOwner,autoBinding;
    private bool committingAuto;
    private PopupMenu navigationMenu=null!;
    private bool IsTextInput(Control? node)=>node is LineEdit or TextEdit;
    private bool Inside(Node? child,Node? parent)=>child!=null&&GodotObject.IsInstanceValid(parent)&&(child==parent||parent!.IsAncestorOf(child));
    private Control? CurrentInteractionFocus()
    {
        if(!GodotObject.IsInstanceValid(panel))return null;
        foreach(var window in panel.FindChildren("*","Window",true,false).OfType<Window>())if(window.Visible&&window.HasFocus()&&window.GuiGetFocusOwner() is {} focused)return focused;
        return panel.GetViewport().GuiGetFocusOwner();
    }
    private void BuildInteractions()
    {
        SetProcessShortcutInput(true);
        navigationMenu=new PopupMenu{Name="NavigationContextMenu"};panel.AddChild(navigationMenu);
        foreach(var pair in new[]{(100,"重新命名"),(101,"複製"),(102,"移除"),(103,"開啟來源 Scene")})navigationMenu.AddItem(pair.Item2,pair.Item1);
        ConnectUi(navigationMenu,PopupMenu.SignalName.IdPressed,nameof(OnNavigationCommand));
        entries.AllowRmbSelect=true;ConnectUi(entries,Control.SignalName.GuiInput,nameof(OnNavigationMouse));
        ConnectUi(pageGraph,Control.SignalName.GuiInput,nameof(OnNavigationKey));
        foreach(var pair in new[]{(definitionId,"definition"),(definitionState,"definition"),(target,"override"),(property,"override"),(rawOverrideValue!,"override"),(eventTarget,"event"),(eventSignal,"event"),(eventAction,"event"),(eventHandler,"event"),(parentPath,"parent"),((LineEdit)panel.FindChild("ElementDisplayName",true,false),"name")})
            ConnectUi(pair.Item1,LineEdit.SignalName.TextChanged,nameof(OnInteractiveText),pair.Item1,pair.Item2);
        ConnectUi(definitionKind,OptionButton.SignalName.ItemSelected,nameof(OnDefinitionOption));
        ConnectUi(definitionOrder,Godot.Range.SignalName.ValueChanged,nameof(OnDefinitionOrder));
        ConnectUi(forwardEventArguments,BaseButton.SignalName.Toggled,nameof(OnEventForward));
        foreach(string name in new[]{"ApplyDefinition","ApplyElementName","ApplyParent","ApplyOverride","ApplyEvent"})if(panel.FindChild(name,true,false) is Control apply)apply.Hide();
    }
    private void OnInteractiveText(string text,Control field,string kind)
    {if(field.HasFocus()&&!restoringDrafts&&!savingDrafts&&!rebuilding)ArmAutoForm(kind);}
    private void OnDefinitionOption(long index){if(definitionKind.HasFocus())ArmAutoForm("definition");}
    private void OnDefinitionOrder(double number){if(Inside(CurrentInteractionFocus(),definitionOrder))ArmAutoForm("definition");}
    private void OnEventForward(bool enabled){if(forwardEventArguments.HasFocus())ArmAutoForm("event");}
    private void ArmAutoForm(string kind)
    {
        if(committingAuto||SelectedDefinition()==null||restoringDrafts||savingDrafts||HasMeta(ReloadState))return;
        if(autoForm.Length>0&&autoForm!=kind)FlushAutoForm();
        autoForm=kind;autoOwner=SelectedDefinition();autoBinding=DraftBinding(kind);
    }
    private void OnInteractiveFocusExit()=>CallDeferred(nameof(FlushAutoFormDeferred));
    private void OnInteractiveNativeKey(InputEvent input)
    {
        if(input is not InputEventKey{Pressed:true,Echo:false} key)return;
        if(authoringPopup?.Visible==true||referenceDialog?.Visible==true||previewPresetDialog?.Visible==true)return;
        if(key.Keycode==Key.Escape)
        {
            if(autoForm.Length>0){string kind=autoForm;ResetAutoForm();OnFormKey(input,kind);}
            foreach(var pending in pendingSettings.ToArray()){pendingSettings.Remove(pending);pending.Dispose();}
            if(settingsOwner!=null){settingsRevision="";RefreshContextSettings();}
        }
        else if(key.Keycode is Key.Enter or Key.KpEnter&&(!(CurrentInteractionFocus() is TextEdit)||key.CtrlPressed||key.MetaPressed))CallDeferred(nameof(CommitInteractiveInput));
    }
    private void CommitInteractiveInput(){FlushAutoForm(true);FlushContextSettings();}
    private void ResetAutoForm(){autoForm="";autoOwner=null;autoBinding=null;}
    private void FlushAutoFormDeferred()=>FlushAutoForm();
    private void FlushAutoForm(bool force=false)
    {
        if(committingAuto||savingDrafts||restoringDrafts||restoringBindingReturn||autoForm.Length==0||(!force&&Input.IsMouseButtonPressed(MouseButton.Left)))return;
        string kind=autoForm;var owner=autoOwner;var binding=autoBinding;ResetAutoForm();
        if(owner!=SelectedDefinition()||binding!=DraftBinding(kind)||!formBaselines.TryGetValue(kind,out var baseline))return;
        using var current=FormValues(kind);if(SameForm(current,baseline)){if(kind=="override")typedValueEdited=false;return;}
        committingAuto=true;
        try{RunFormAction(kind,()=>{switch(kind){case "definition":ApplyDefinition();break;case "name":OnElementNameSubmitted(((LineEdit)panel.FindChild("ElementDisplayName",true,false)).Text);break;case "parent":ApplyParentPath();break;case "override":if(typedPropertyValid)ApplyRule();break;case "event":if(selectedEventResource!=null)ApplyEvent();break;}});}
        finally{committingAuto=false;}
    }
    private void PollInteractions()
    {
        if(panel==null||!panel.IsVisibleInTree()||target==null)return;
        var focus=CurrentInteractionFocus();
        if(focus!=null&&(Inside(focus,panel)||Inside(focus,advancedInspector))&&interactionHooks.Add(focus.GetInstanceId()))
        {
            ConnectUi(focus,Control.SignalName.FocusExited,nameof(OnInteractiveFocusExit));
            ConnectUi(focus,Control.SignalName.GuiInput,nameof(OnInteractiveNativeKey));
        }
        bool typing=IsTextInput(focus);
        if(!Input.IsMouseButtonPressed(MouseButton.Left)&&!typing)
        {FlushAutoForm();if(pendingSettings.Count>0)FlushContextSettings();}
    }
    private void OnNavigationMouse(InputEvent input)
    {
        if(input is not InputEventMouseButton{Pressed:true,ButtonIndex:MouseButton.Right} mouse)return;
        int index=entries.GetItemAtPosition(mouse.Position,true);if(index<0)return;
        CallDeferred(nameof(OpenNavigationContext),index,entries.GlobalPosition+mouse.Position);entries.AcceptEvent();
    }
    private void OpenNavigationContext(int index,Vector2 position)
    {
        if(NavigationChoice(index) is not {} choice)return;
        if(choice.Choice is ViewElementPlacement placement){if(SelectedDefinition()!=placement)SelectPlacement(choice.Owner,placement,false);}else if(SelectedDefinition()!=choice.Owner)SelectEntry(catalog!.Entries.IndexOf(choice.Owner));
        navigationMenu.SetItemDisabled(navigationMenu.GetItemIndex(101),SelectedDefinition() is ViewElementPlacement item&&item.PoolPolicy?.Unique==true);
        navigationMenu.PopupOnParent(new Rect2I((Vector2I)position,Vector2I.One));
    }
    private void OnNavigationCommand(long id)=>Try(()=>
    {
        if(SelectedDefinition()==null)return;
        switch(id){case 100:BeginInlineRename();break;case 101:if(SelectedDefinition() is ViewElementPlacement item){if(item.PoolPolicy?.Unique!=true)CopyElement();}else DuplicateEntry();break;case 102:if(SelectedDefinition() is ViewElementPlacement)RemoveElement();else RemoveEntry();break;case 103:OpenSource();break;}
    });
    private void BeginInlineRename()
    {
        Control field=SelectedDefinition() is ViewElementPlacement?panel.FindChild("ElementDisplayName",true,false) as Control??definitionId:definitionId;
        string section=SelectedDefinition() is ViewElementPlacement?"元件":"設定";inspectorSections[section].GetNode<Control>("Content").Show();
        ((ScrollContainer)panel.FindChild("SelectionInspector",true,false)).EnsureControlVisible(field);field.GrabFocus();if(field is LineEdit text)text.SelectAll();
    }
    private bool HandleNavigationShortcut(InputEvent input)
    {
        if(input is not InputEventKey{Pressed:true,Echo:false} key||IsTextInput(CurrentInteractionFocus()))return false;
        int command=key.Keycode==Key.F2?100:key.Keycode is Key.Delete or Key.Backspace?102:key.Keycode==Key.D&&(key.CtrlPressed||key.MetaPressed)?101:-1;
        if(command<0)return false;CallDeferred(nameof(OnNavigationCommand),(long)command);panel.GetViewport().SetInputAsHandled();return true;
    }
    public override void _ShortcutInput(InputEvent input)
    {
        if(panel==null||!panel.IsVisibleInTree()||input is not InputEventKey{Pressed:true,Echo:false} key||!(key.CtrlPressed||key.MetaPressed))return;
        var focus=CurrentInteractionFocus();if(!Inside(focus,panel)&&!Inside(focus,advancedInspector))return;
        if(IsTextInput(focus)){if(key.Keycode==Key.D)panel.GetViewport().SetInputAsHandled();return;}
        if(key.Keycode==Key.Z){if(key.ShiftPressed)RedoDocument();else UndoDocument();panel.GetViewport().SetInputAsHandled();}
        else if(key.Keycode==Key.Y){RedoDocument();panel.GetViewport().SetInputAsHandled();}
    }
    private void ReleaseInteractions(){SetProcessShortcutInput(false);ResetAutoForm();interactionHooks.Clear();navigationMenu=null!;}
}
#endif
