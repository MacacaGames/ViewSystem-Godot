#if TOOLS
using Godot;
using System;
using System.Linq;
namespace MacacaGames.ViewSystem;

public partial class ViewSystemEditor
{
    // Controls project this state; they never decide which Resource an edit owns.
    private sealed class EditorSelection
    {
        public ViewCatalog? Catalog;
        public ViewEntry? Owner, OpenedOwner;
        public ViewElementPlacement? Placement;
        public bool Active;
    }
    private readonly EditorSelection editing=new();
    private ViewCatalog? catalog {get=>editing.Catalog;set=>editing.Catalog=value;}
    private ViewEntry? entry {get=>editing.Owner;set=>editing.Owner=value;}
    private ViewEntry? navigationEntry {get=>editing.OpenedOwner;set=>editing.OpenedOwner=value;}
    private ViewElementPlacement? navigationPlacement {get=>editing.Placement;set=>editing.Placement=value;}
    private Resource? SelectedDefinition()=>editing.Active?(Resource?)editing.Placement??editing.Owner:null;
    public Godot.Collections.Dictionary GetAuthoringSelection()=>new()
    {
        ["scope"]=navigationEntry==null?"views":"elements",["owner"]=entry?.Id??"",["element"]=editing.Placement?.Id??"",["active"]=editing.Active,
        ["override"]=selectedRule,["event"]=selectedEvent,["drafts"]=formDrafts.Count
    };

    private void SetEditingSelection(ViewEntry? owner,ViewElementPlacement? placement,bool inside,bool active=true)
    {
        FlushAutoForm(true);
        FlushContextSettings();
        var previous=SelectedDefinition();
        CaptureCurrentDraft();
        entry=owner;navigationEntry=inside?owner:null;navigationPlacement=placement;
        editing.Active=active&&owner!=null;
        navigationGeneration++;
        elements.Clear();elements.AddItem("Root");
        if(owner!=null)foreach(var item in owner.Elements)elements.AddItem(item.DisplayName.Length>0?item.DisplayName+" · "+item.Id:item.Id);
        elements.Select(placement!=null&&owner!=null?owner.Elements.IndexOf(placement)+1:0);
        if(previous!=SelectedDefinition()){ResetBindingSelection();ClearFormErrors();}
        RefreshRules();RefreshPreview();RefreshNavigation();SyncGraphSelection();
        if(previous!=SelectedDefinition()||formOwner!=SelectedDefinition())BindOwnerForms();
    }
    private void SelectPlacement(ViewEntry owner,ViewElementPlacement item,bool reveal=true)
    {
        if(catalog?.Entries.Contains(owner)!=true||!owner.Elements.Contains(item))return;
        if(navigationEntry==null){navigationRootSearch=entrySearch.Text;navigationRootFilter=entryFilter.Selected;navigationRootScroll=entries.GetVScrollBar().Value;}
        if(reveal)entrySearch.Text="";
        SetEditingSelection(owner,item,true);entries.EnsureCurrentIsVisible();
    }
    private void ClearEditingSelection(bool keepOwner)
    {
        SetEditingSelection(keepOwner?entry:null,null,keepOwner&&navigationEntry!=null,false);
    }
    private void ResetBindingSelection()
    {
        selectedRule=-1;selectedEvent=-1;selectedRuleResource=null;selectedEventResource=null;
        eventReceiverDraft=false;eventNativeHandler=false;eventRelativeHandler=false;
        bindingFlow="";returnToAuthoringMember="";targetPickerSearch="";
        authoringPopup?.Hide();
        if(target==null)return;
        target.Text=".";property.Text="";value.Text="";modelKey.Text="";
        eventTarget.Text=".";eventSignal.Text="";eventAction.Text="";eventHandler.Text="";
        forwardEventArguments.SetPressedNoSignal(false);
        typedOverrideDraft=null;typedOverrideKey="";typedLookupKey="";typedValueEdited=false;
    }
    private ViewPropertyOverride? selectedRuleResource;
    private ViewEventBinding? selectedEventResource;
    private void ReconcileBindingSelection()
    {
        selectedRule=selectedRuleResource==null?-1:SelectedRules().IndexOf(selectedRuleResource);
        selectedEvent=selectedEventResource==null?-1:SelectedEvents().IndexOf(selectedEventResource);
        if(selectedRule<0)selectedRuleResource=null;
        if(selectedEvent<0)selectedEventResource=null;
    }
    private Godot.Collections.Dictionary? lastMutationAfter;
    private Godot.Collections.Dictionary CaptureEditorLocation()=>new()
    {
        ["catalog"]=Variant.From(catalog),["owner"]=Variant.From(entry),["opened"]=Variant.From(navigationEntry),["placement"]=Variant.From(editing.Placement),["active"]=editing.Active,
        ["rule"]=Variant.From(selectedRuleResource),["event"]=Variant.From(selectedEventResource),["search"]=entrySearch.Text,["filter"]=entryFilter.Selected,
        ["root_search"]=navigationRootSearch,["root_filter"]=navigationRootFilter,
        ["root_scroll"]=navigationRootScroll,["list_scroll"]=entries.GetVScrollBar().Value,
        ["scroll"]=((ScrollContainer)panel.FindChild("SelectionInspector",true,false)).ScrollVertical
    };
    private void RestoreEditorLocation(Godot.Collections.Dictionary location)
    {
        if(location["catalog"].AsGodotObject()!=catalog)return;
        var owner=location["owner"].AsGodotObject() as ViewEntry;var placement=location["placement"].AsGodotObject() as ViewElementPlacement;
        if(owner!=null&&catalog?.Entries.Contains(owner)!=true)owner=null;
        if(placement!=null&&owner?.Elements.Contains(placement)!=true)placement=null;
        navigationRootSearch=location["root_search"].AsString();navigationRootFilter=location["root_filter"].AsInt32();
        if(location.TryGetValue("root_scroll",out var rootScroll))navigationRootScroll=rootScroll.AsDouble();
        entrySearch.Text=location["search"].AsString();entryFilter.Select(location["filter"].AsInt32());
        bool inside=owner!=null&&location["opened"].AsGodotObject()==owner;
        SetEditingSelection(owner,placement,inside,location["active"].AsBool()&&(!inside||placement!=null));
        if(location["rule"].AsGodotObject() is ViewPropertyOverride rule&&SelectedRules().IndexOf(rule) is int r&&r>=0)LoadRule(r);
        if(location["event"].AsGodotObject() is ViewEventBinding binding&&SelectedEvents().IndexOf(binding) is int e&&e>=0)OnEventSelected(e);
        ((ScrollContainer)panel.FindChild("SelectionInspector",true,false)).ScrollVertical=location["scroll"].AsInt32();RefreshBindingTrees();
        if(location.TryGetValue("list_scroll",out var listScroll))CallDeferred(nameof(RestoreNavigationScroll),listScroll.AsDouble(),navigationGeneration);
    }
    private void CommitEditorAction(EditorUndoRedoManager undo,Godot.Collections.Dictionary before,Godot.Collections.Dictionary? nextLocation=null)
    {
        var after=nextLocation??CaptureEditorLocation();lastMutationAfter?.Dispose();lastMutationAfter=after;
        undo.AddDoMethod(this,nameof(RestoreEditorLocation),after);undo.AddUndoMethod(this,nameof(RestoreEditorLocation),before);
        undo.AddDoMethod(this,MethodName.RefreshDocument);undo.AddUndoMethod(this,MethodName.RefreshDocument);undo.CommitAction();before.Dispose();
    }
    private void ChangeSelectedBinding(Resource owner,StringName field,Variant next,Variant previous,Resource binding,int index,bool events)
    {
        var before=CaptureEditorLocation();
        if(events){selectedEventResource=(ViewEventBinding)binding;selectedEvent=index;}else{selectedRuleResource=(ViewPropertyOverride)binding;selectedRule=index;}
        var after=CaptureEditorLocation();var undo=GetUndoRedo();undo.CreateAction(events?"ViewSystem event":"ViewSystem override",customContext:catalog,markUnsaved:false);
        undo.ForceFixedHistory();undo.AddDoProperty(owner,field,next);undo.ForceFixedHistory();undo.AddUndoProperty(owner,field,previous);CommitEditorAction(undo,before,after);
    }
    private void RememberMutationSelection()
    {
        if(lastMutationAfter==null)return;using var current=CaptureEditorLocation();lastMutationAfter.Clear();lastMutationAfter.Merge(current,true);
    }
}
#endif
