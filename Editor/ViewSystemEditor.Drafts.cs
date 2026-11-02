#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using Values=Godot.Collections.Dictionary;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private readonly List<Resource> formDrafts=new(),ownerSessions=new();
    private readonly Dictionary<string,Values> formBaselines=new();
    private Resource? formOwner;
    private bool restoringDrafts,savingDrafts;
    private static readonly string[] DraftKinds={"definition","name","parent","override","event"};
    private void SetFormBaseline(string kind,Values values)
    {if(formBaselines.TryGetValue(kind,out var previous))previous.Dispose();formBaselines[kind]=values;}
    private void ClearFormBaselines()
    {foreach(var values in formBaselines.Values)values.Dispose();formBaselines.Clear();}
    private void DropDraft(Resource draft)
    {int index=formDrafts.FindIndex(d=>ReferenceEquals(d,draft));if(index<0)return;formDrafts.RemoveAt(index);draft.Dispose();}
    private void ClearDraftStorage(bool dispose)
    {
        if(dispose){foreach(var resource in formDrafts.Concat(ownerSessions).ToArray())resource.Dispose();overrideRawModes.Clear();}
        formDrafts.Clear();ownerSessions.Clear();ClearFormBaselines();formOwner=null;
    }
    private void RestoreDraftForm(string kind,Resource draft)
    {using var values=draft.GetMeta("values").AsGodotDictionary();RestoreForm(kind,values);}
    private Resource? DraftBinding(string kind)=>kind=="override"?selectedRuleResource:kind=="event"?selectedEventResource:null;
    private Values FormValues(string kind)=>kind switch
    {
        "definition"=>new(){{"id",definitionId.Text},{"state",definitionState.Text},{"order",definitionOrder.Value},{"kind",definitionKind.Selected}},
        "name"=>new(){{"name",((LineEdit)panel.FindChild("ElementDisplayName",true,false)).Text}},
        "parent"=>new(){{"parent",parentPath.Text}},
        "override"=>new(){{"target",target.Text},{"property",property.Text},{"value",value.Text},{"model",modelKey.Text},{"scope",scopes.Selected},{"kind",kinds.Selected},{"native",typedOverrideDraft?.NativeValue??default},{"native_value",value.Text==typedMirroredText},{"typed_edited",typedValueEdited}},
        "event"=>new(){{"target",eventTarget.Text},{"signal",eventSignal.Text},{"action",eventAction.Text},{"handler",eventHandler.Text},{"forward",forwardEventArguments.ButtonPressed},{"native_handler",eventNativeHandler},{"relative_handler",eventRelativeHandler}},
        _=>new()
    };
    private bool HasForm(string kind)=>SelectedDefinition()!=null&&(kind=="definition"?SelectedDefinition() is ViewEntry:kind is "name" or "parent"?SelectedDefinition() is ViewElementPlacement:true);
    private static bool SameForm(Values left,Values right)
    {
        using var first=left.Duplicate();using var second=right.Duplicate();
        first.Remove("typed_edited");second.Remove("typed_edited");
        return Json.Stringify(first)==Json.Stringify(second);
    }
    private static Resource? MetaResource(Resource source,string key)=>source.HasMeta(key)?source.GetMeta(key).AsGodotObject() as Resource:null;
    private Resource? FindDraft(Resource owner,string kind,Resource? binding)=>formDrafts.FirstOrDefault(d=>d.GetMeta("owner").AsGodotObject()==owner&&d.GetMeta("kind").AsString()==kind&&MetaResource(d,"binding")==binding);
    private void CaptureCurrentDraft()
    {
        if(restoringDrafts||savingDrafts||restoringBindingReturn||formOwner==null||formOwner!=SelectedDefinition()||!GodotObject.IsInstanceValid(panel))return;
        CaptureNestedTypedEdits();
        foreach(string kind in DraftKinds)
        {
            if(!HasForm(kind)||!formBaselines.TryGetValue(kind,out var baseline))continue;
            using var values=FormValues(kind);var binding=DraftBinding(kind);var draft=FindDraft(formOwner,kind,binding);
            bool dirty=!SameForm(values,baseline)||(kind=="override"&&typedValueEdited);
            if(!dirty){if(draft!=null)DropDraft(draft);continue;}
            if(draft==null){draft=new Resource();draft.SetMeta("owner",formOwner);draft.SetMeta("kind",kind);draft.SetMeta("binding",Variant.From(binding));formDrafts.Add(draft);}
            draft.SetMeta("values",values);
        }
        var session=ownerSessions.FirstOrDefault(s=>s.GetMeta("owner").AsGodotObject()==formOwner);
        if(session==null){session=new Resource();session.SetMeta("owner",formOwner);ownerSessions.Add(session);}
        session.SetMeta("override",Variant.From(selectedRuleResource));session.SetMeta("event",Variant.From(selectedEventResource));
        session.SetMeta("scroll",((ScrollContainer)panel.FindChild("SelectionInspector",true,false)).ScrollVertical);
        var folds=new Godot.Collections.Dictionary<string,bool>();foreach(var pair in inspectorSections)folds[pair.Key]=pair.Value.GetNode<Control>("Content").Visible;session.SetMeta("folds",folds);
    }
    private void RebaseForm(string kind)
    {if(!restoringDrafts&&formOwner==SelectedDefinition())SetFormBaseline(kind,FormValues(kind));}
    private void RefreshPersistedForms()
    {
        if(formOwner==null||formOwner!=SelectedDefinition()||restoringDrafts)return;
        foreach(string kind in DraftKinds)
        {
            if(!HasForm(kind)||FindDraft(formOwner,kind,DraftBinding(kind))!=null)continue;
            restoringDrafts=true;
            try
            {
                if(kind=="override"&&selectedRule>=0)LoadRule(selectedRule);
                if(kind=="event"&&selectedEvent>=0)OnEventSelected(selectedEvent);
                SetFormBaseline(kind,FormValues(kind));
            }
            finally{restoringDrafts=false;}
        }
    }
    private void AcceptCurrentDraft(string kind)
    {
        if(SelectedDefinition() is not Resource owner)return;
        var draft=FindDraft(owner,kind,DraftBinding(kind));if(draft!=null)DropDraft(draft);
        SetFormBaseline(kind,FormValues(kind));if(kind=="override")typedValueEdited=false;
    }
    private void BindOwnerForms()
    {
        formOwner=SelectedDefinition();ClearFormBaselines();if(formOwner==null)return;
        restoringDrafts=true;
        try
        {
            var session=ownerSessions.FirstOrDefault(s=>s.GetMeta("owner").AsGodotObject()==formOwner);
            if(session!=null)
            {
                var rule=MetaResource(session,"override") as ViewPropertyOverride;int r=rule==null?-1:SelectedRules().IndexOf(rule);if(r>=0)LoadRule(r);
                var ev=MetaResource(session,"event") as ViewEventBinding;int e=ev==null?-1:SelectedEvents().IndexOf(ev);if(e>=0)OnEventSelected(e);
                if(session.HasMeta("folds")){var folds=session.GetMeta("folds").AsGodotDictionary();foreach(var pair in inspectorSections)if(folds.TryGetValue(pair.Key,out var visible)){pair.Value.GetNode<Control>("Content").Visible=visible.AsBool();pair.Value.GetChild<Button>(0).Text=(visible.AsBool()?"▾ ":"▸ ")+pair.Key;}}
                if(session.HasMeta("scroll"))((ScrollContainer)panel.FindChild("SelectionInspector",true,false)).ScrollVertical=session.GetMeta("scroll").AsInt32();
            }
            foreach(string kind in DraftKinds)if(HasForm(kind))SetFormBaseline(kind,FormValues(kind));
        }
        finally{restoringDrafts=false;}
        RestoreVisibleDrafts();
    }
    private void RestoreVisibleDrafts()
    {
        if(restoringDrafts||formOwner!=SelectedDefinition()||formOwner==null)return;
        restoringDrafts=true;
        try{foreach(string kind in DraftKinds)if(HasForm(kind)&&FindDraft(formOwner,kind,DraftBinding(kind)) is {} draft)RestoreDraftForm(kind,draft);}
        finally{restoringDrafts=false;}
    }
    private void RestoreForm(string kind,Values v)
    {
        switch(kind)
        {
            case "definition":definitionId.Text=v["id"].AsString();definitionState.Text=v["state"].AsString();definitionOrder.SetValueNoSignal(v["order"].AsDouble());definitionKind.Select(v["kind"].AsInt32());break;
            case "name":((LineEdit)panel.FindChild("ElementDisplayName",true,false)).Text=v["name"].AsString();break;
            case "parent":parentPath.Text=v["parent"].AsString();break;
            case "override":target.Text=v["target"].AsString();property.Text=v["property"].AsString();modelKey.Text=v["model"].AsString();scopes.Select(v["scope"].AsInt32());kinds.Select(v["kind"].AsInt32());RefreshTypedOverrideEditor(true);typedOverrideDraft=new(){Kind=ViewValueKind.Native,NativeValue=v["native"]};typedValueEdited=!v.TryGetValue("typed_edited",out var edited)||edited.AsBool();if(overrideValueProxy!=null){typedOverrideSync=true;overrideValueProxy.Set("Value",v["native"]);typedOverrideSync=false;}value.Text=v["value"].AsString();if(v.TryGetValue("native_value",out var native)&&native.AsBool())typedMirroredText=value.Text;break;
            case "event":eventTarget.Text=v["target"].AsString();eventSignal.Text=v["signal"].AsString();eventAction.Text=v["action"].AsString();eventHandler.Text=v["handler"].AsString();forwardEventArguments.SetPressedNoSignal(v["forward"].AsBool());eventNativeHandler=v.TryGetValue("native_handler",out var nativeHandler)?nativeHandler.AsBool():selectedEventResource?.NativeHandler??false;eventRelativeHandler=v.TryGetValue("relative_handler",out var relative)?relative.AsBool():selectedEventResource?.HandlerRelativeToElement??false;eventReceiverDraft=false;RefreshEventMethods();break;
        }
    }
    private bool BelongsToDocument(Resource owner)=>catalog!=null&&((owner is ViewEntry page&&catalog.Entries.Contains(page))||catalog.Entries.Any(e=>e.Elements.Any(p=>p==owner)));
    private bool IsApplicableDraft(Resource draft)
    {
        if(draft.GetMeta("owner").AsGodotObject() is not Resource owner||!BelongsToDocument(owner))return false;
        var binding=MetaResource(draft,"binding");
        return binding switch
        {
            ViewPropertyOverride rule=>owner is ViewEntry page?page.Overrides.Contains(rule):((ViewElementPlacement)owner).Overrides.Contains(rule),
            ViewEventBinding ev=>owner is ViewEntry page?page.Events.Contains(ev):((ViewElementPlacement)owner).Events.Contains(ev),
            _=>true
        };
    }
    private bool HasPendingDrafts()
    {CaptureCurrentDraft();return pendingSettings.Any(d=>d.GetMeta("owner").AsGodotObject() is Resource pendingOwner&&BelongsToDocument(pendingOwner))||formDrafts.Any(IsApplicableDraft);}
    private sealed record PendingForm(Resource Owner,string Kind,Resource? Binding,Values Values);
    private void ApplyPendingDrafts()
    {
        if(savingDrafts)return;CaptureCurrentDraft();savingDrafts=true;
        using var location=CaptureEditorLocation();
        var pending=formDrafts.Where(IsApplicableDraft).Select(d=>
        {using var values=d.GetMeta("values").AsGodotDictionary();return new PendingForm((Resource)d.GetMeta("owner").AsGodotObject(),d.GetMeta("kind").AsString(),MetaResource(d,"binding"),values.Duplicate());}).ToArray();
        var edits=new Dictionary<(Resource Owner,string Field),Variant>();
        var replacements=new Dictionary<Resource,Resource>();
        var rawModes=new List<(Resource Owner,Resource? Before,Resource After)>();
        Variant Get(Resource owner,string field)=>edits.TryGetValue((owner,field),out var next)?next:owner.Get(field);
        try
        {
            // Build and validate every edit without changing authored Resources or
            // Undo history. Multiple drafts for one array share the staged value.
            foreach(var draft in pending)
            {
                var owner=draft.Owner;var page=owner as ViewEntry??catalog!.Entries.First(e=>e.Elements.Any(p=>p==owner));
                if(owner is ViewElementPlacement item)SelectPlacement(page,item);else SelectEntry(catalog!.Entries.IndexOf(page));
                if(draft.Kind=="override"){selectedRuleResource=draft.Binding as ViewPropertyOverride;selectedRule=selectedRuleResource==null?-1:SelectedRules().IndexOf(selectedRuleResource);}
                if(draft.Kind=="event"){selectedEventResource=draft.Binding as ViewEventBinding;selectedEvent=selectedEventResource==null?-1:SelectedEvents().IndexOf(selectedEventResource);}
                RestoreForm(draft.Kind,draft.Values);
                try
                {
                    switch(draft.Kind)
                    {
                        case "definition":foreach(var change in DefinitionChanges())edits[(owner,change.Field)]=change.Next;break;
                        case "name":edits[(owner,"DisplayName")]=((LineEdit)panel.FindChild("ElementDisplayName",true,false)).Text;break;
                        case "parent":ValidateParentForm();edits[(owner,"ParentPath")]=new NodePath(parentPath.Text);break;
                        case "override":
                            var rule=BuildRule();var rules=new Godot.Collections.Array<ViewPropertyOverride>(Get(owner,"Overrides").AsGodotArray<ViewPropertyOverride>());
                            int r=draft.Binding is ViewPropertyOverride oldRule?rules.IndexOf(oldRule):-1;
                            if(r>=0){rules[r]=rule;replacements[draft.Binding!]=rule;}else rules.Add(rule);rawModes.Add((owner,draft.Binding,rule));edits[(owner,"Overrides")]=rules;break;
                        case "event":
                            var binding=BuildEvent();var events=new Godot.Collections.Array<ViewEventBinding>(Get(owner,"Events").AsGodotArray<ViewEventBinding>());
                            int e=draft.Binding is ViewEventBinding oldEvent?events.IndexOf(oldEvent):-1;
                            if(e>=0){events[e]=binding;replacements[draft.Binding!]=binding;}else events.Add(binding);edits[(owner,"Events")]=events;break;
                    }
                }
                catch(Exception error){ShowFormError(draft.Kind,error.Message);throw;}
            }
            RestoreEditorLocation(location);
            foreach(var mode in rawModes)CopyOverrideMode(mode.Owner,mode.Before,mode.After);
            foreach(var draft in pending)if(FindDraft(draft.Owner,draft.Kind,draft.Binding) is {} stored)DropDraft(stored);
            formOwner=null;ClearFormBaselines();
            var changes=edits.Where(p=>referenceRouter==null||!ViewReferenceDiff.Equivalent(p.Key.Owner.Get(p.Key.Field),p.Value,referenceRouter)).ToArray();
            if(changes.Length>0)
            {
                var before=location.Duplicate();var after=location.Duplicate();
                foreach(string key in new[]{"rule","event"})if(after[key].AsGodotObject() is Resource original&&replacements.TryGetValue(original,out var next))after[key]=next;
                var undo=GetUndoRedo();undo.CreateAction("Apply ViewSystem drafts",customContext:catalog,markUnsaved:false);
                foreach(var change in changes)
                {undo.ForceFixedHistory();undo.AddDoProperty(change.Key.Owner,change.Key.Field,change.Value);undo.ForceFixedHistory();undo.AddUndoProperty(change.Key.Owner,change.Key.Field,change.Key.Owner.Get(change.Key.Field));}
                CommitEditorAction(undo,before,after);
            }
            else RestoreEditorLocation(location);
        }
        finally{foreach(var draft in pending)draft.Values.Dispose();savingDrafts=false;}
    }
}
#endif
