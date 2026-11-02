#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private EditorInspector entrySettings=null!,elementSettings=null!;
    private Resource? entrySettingsProxy,elementSettingsProxy,settingsOwner;
    private string settingsFields="";
    private string settingsRevision="";
    private readonly List<Resource> pendingSettings=new();
    private bool flushingSettings;
    private bool preservingContextControls;
    private void BuildContextSettings(Node basic,Node items)
    {
        entrySettings=new EditorInspector{Name="EntrySettingsInspector",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(0,120)};basic.AddChild(entrySettings);
        elementSettings=new EditorInspector{Name="ElementSettingsInspector",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(0,120)};items.AddChild(elementSettings);
        var script=GD.Load<GDScript>("res://addons/macaca_viewsystem/Editor/selection_settings_proxy.gd");
        entrySettingsProxy=(Resource)script.New().AsGodotObject();elementSettingsProxy=(Resource)script.New().AsGodotObject();
        ConnectUi(entrySettingsProxy,"setting_changed",nameof(OnContextSettingChanged));ConnectUi(elementSettingsProxy,"setting_changed",nameof(OnContextSettingChanged));
        ConnectUi(entrySettings,EditorInspector.SignalName.PropertyEdited,nameof(OnContextPropertyEdited));ConnectUi(elementSettings,EditorInspector.SignalName.PropertyEdited,nameof(OnContextPropertyEdited));
    }
    private void RefreshContextSettings()
    {
        RefreshAdvancedSettings();
        if(entrySettingsProxy==null||elementSettingsProxy==null)return;
        var owner=SelectedDefinition();string[] fields=owner switch
        {
            ViewEntry page=>new[]{"Scene",page.Scene==null?"ScenePath":"","UseGlobalSafeArea",!page.UseGlobalSafeArea?"SafeArea":"",page.Kind==ViewKind.Overlay?"Modal":"","NavigationEnabled",page.NavigationEnabled?"FirstSelected":"",page.NavigationEnabled?"FirstSelectedState":"",page.NavigationEnabled?"StateNavigation":"",page.NavigationEnabled?"FocusLinks":"","TransitionTiming",page.TransitionTiming==ViewPageTransitionTiming.Custom?"CustomTransitionWait":"","TargetFrameRate","PoolPolicy","ShowTransition","HideTransition","AnimationPlayerPath","ShowAnimation","HideAnimation"},
            ViewElementPlacement item=>new[]{"Scene",item.Scene==null?"ScenePath":"","NodeName","ExcludeFeatures","ManualOnly","InstantPosition",!item.InstantPosition?"TransferDuration":"",!item.InstantPosition?"TransferTransition":"",!item.InstantPosition?"TransferEase":"","DelayIn","DelayOut","ShowTransition","HideTransition","PoolPolicy"},
            _=>Array.Empty<string>()
        };
        fields=fields.Where(f=>f.Length>0).ToArray();string key=string.Join("|",fields);
        string revision=catalog==null?"":Signature(catalog);
        var currentProxy=owner is ViewEntry?entrySettingsProxy:elementSettingsProxy;
        if(settingsOwner==owner&&settingsFields==key&&preservingContextControls&&owner!=null&&ContextValuesMatch(owner,currentProxy,fields)){settingsRevision=revision;return;}
        if(settingsOwner==owner&&settingsFields==key&&settingsRevision==revision)return;settingsOwner=owner;settingsFields=key;settingsRevision=revision;
        entrySettings.Edit(null);elementSettings.Edit(null);
        if(owner==null)return;
        var proxy=owner is ViewEntry?entrySettingsProxy:elementSettingsProxy;
        proxy.Call("configure",owner,fields);(owner is ViewEntry?entrySettings:elementSettings).Edit(proxy);
    }
    private bool ContextValuesMatch(Resource owner,Resource proxy,string[] fields)=>referenceRouter!=null&&fields.All(field=>ViewReferenceDiff.Equivalent(owner.Get(field),proxy.Get(field),referenceRouter));
    private void OnContextSettingChanged(StringName property,Variant next)
    {
        if(SelectedDefinition() is not Resource owner||owner!=settingsOwner)return;
        QueueContextSetting(owner,property,next);
    }
    private void QueueContextSetting(Resource owner,StringName property,Variant next)
    {
        var pending=pendingSettings.FirstOrDefault(p=>p.GetMeta("owner").AsGodotObject()==owner&&p.GetMeta("property").AsString()==property.ToString());
        if(pending==null){pending=new Resource();pending.SetMeta("owner",owner);pending.SetMeta("property",property.ToString());pendingSettings.Add(pending);}
        pending.SetMeta("next",next);CallDeferred(nameof(FlushContextSettingsDeferred));
    }
    private void CommitContextSetting(Resource owner,StringName property,Variant next)
    {
        if(!BelongsToDocument(owner)||referenceRouter==null||ViewReferenceDiff.Equivalent(owner.Get(property),next,referenceRouter))return;
        if(SelectedDefinition()!=owner)
        {
            if(owner is ViewEntry page)SelectEntry(catalog!.Entries.IndexOf(page));
            else if(owner is ViewElementPlacement item)SelectPlacement(catalog!.Entries.First(e=>e.Elements.Contains(item)),item);
        }
        preservingContextControls=true;
        try{Change(owner,property,next,owner.Get(property),"ViewSystem "+property);}
        finally{preservingContextControls=false;}
    }
    private void FlushContextSettingsDeferred()
    {var focus=CurrentInteractionFocus();if(Input.IsMouseButtonPressed(MouseButton.Left)||IsTextInput(focus)&&(Inside(focus,entrySettings)||Inside(focus,elementSettings)||Inside(focus,advancedInspector)))return;Try(FlushContextSettings);}
    private void FlushContextSettings()
    {
        if(flushingSettings||pendingSettings.Count==0)return;flushingSettings=true;
        try
        {
            var values=pendingSettings.ToArray();pendingSettings.Clear();
            foreach(var pending in values)
            {try{if(pending.GetMeta("owner").AsGodotObject() is Resource owner)CommitContextSetting(owner,pending.GetMeta("property").AsString(),pending.HasMeta("next")?pending.GetMeta("next"):default);}finally{pending.Dispose();}}
        }
        finally{flushingSettings=false;}
    }
    private void OnContextPropertyEdited(StringName path)
    {
        if(settingsOwner==null||referenceRouter==null)return;string field=path.ToString().Split('/',':')[0];
        if(!settingsFields.Split('|').Contains(field))return;
        var proxy=settingsOwner is ViewEntry?entrySettingsProxy:elementSettingsProxy;if(proxy==null)return;
        var next=proxy.Get(field);var pending=pendingSettings.FirstOrDefault(p=>p.GetMeta("owner").AsGodotObject()==settingsOwner&&p.GetMeta("property").AsString()==field);
        var previous=pending!=null?(pending.HasMeta("next")?pending.GetMeta("next"):default):settingsOwner.Get(field);
        if(!ViewReferenceDiff.Equivalent(previous,next,referenceRouter))OnContextSettingChanged(field,next);
    }
    private void ReleaseContextSettings()
    {if(GodotObject.IsInstanceValid(entrySettings))entrySettings.Edit(null);if(GodotObject.IsInstanceValid(elementSettings))elementSettings.Edit(null);entrySettingsProxy?.Dispose();elementSettingsProxy?.Dispose();entrySettingsProxy=null;elementSettingsProxy=null;settingsOwner=null;settingsFields="";entrySettings=null!;elementSettings=null!;}
}
#endif
