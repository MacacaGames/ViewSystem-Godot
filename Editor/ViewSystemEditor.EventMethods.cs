#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;
public partial class ViewSystemEditor
{
    private Button eventReceiverButton=null!;
    private MenuButton eventMethodButton=null!;
    private Control eventAdvanced=null!;
    private string eventReceiverPath="__dispatch";
    private bool eventReceiverDraft,eventNativeHandler,eventRelativeHandler;
    private sealed record EventMethodChoice(string Action,string Handler,string Caption,bool Native,bool Enabled,bool Forward=false);
    private readonly List<EventMethodChoice> eventMethodChoices=new();
    private static bool CompatibleSignalArgument(Variant argument,Variant source)
    {
        using var arg=argument.AsGodotDictionary();using var sent=source.AsGodotDictionary();int type=arg["type"].AsInt32();if(type==0)return true;if(type!=sent["type"].AsInt32())return false;
        if(type!=(int)Variant.Type.Object)return true;
        string expected=arg.TryGetValue("class_name",out var expectedName)?expectedName.AsString():"",actual=sent.TryGetValue("class_name",out var actualName)?actualName.AsString():"";
        return expected.Length==0||expected=="Object"||actual.Length>0&&(expected==actual||ClassDB.IsParentClass(actual,expected));
    }
    private static bool ValidActionMethod(MethodInfo method)
    {var args=method.GetParameters();return !method.ContainsGenericParameters&&method.ReturnType==typeof(void)&&(args.Length==0||args.Length==1&&(args[0].ParameterType==typeof(ViewActionEvent)||args[0].ParameterType.IsAssignableFrom(typeof(Control))));}
    private IEnumerable<MethodInfo> ReceiverMethods(Node node)
    {
        var methods=node.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(m=>m.GetCustomAttribute<ViewSystemEventAttribute>()!=null).ToArray();
        // The runtime resolves by name and uses an explicitly AOT-preserved schema.
        foreach(var method in methods)if(ValidActionMethod(method)&&methods.Count(m=>m.Name==method.Name)==1)yield return method;
    }
    private bool HasReceiverMethods(Node node)=>ReceiverMethods(node).Any()||node.GetScript().AsGodotObject() is GDScript script&&script.GetScriptMethodList().Any(m=>!m["name"].AsString().StartsWith("_",StringComparison.Ordinal));
    private void BuildEventMethods(Node parent)
    {
        var receiver=new VBoxContainer{Name="EventReceiverRow"};parent.AddChild(receiver);receiver.AddChild(new Label{Text="接收對象"});
        eventReceiverButton=Button(receiver,"選擇接收對象…","EventReceiver",()=>OpenAuthoringPicker("EventReceiver"));
        eventReceiverButton.Icon=eventReceiverButton.GetThemeIcon("arrow","OptionButton");eventReceiverButton.IconAlignment=HorizontalAlignment.Right;
        parent.AddChild(new Label{Text="方法"});eventMethodButton=new MenuButton{Name="EventMethod",Text="選擇方法…",Flat=false};parent.AddChild(eventMethodButton);
        eventMethodButton.Icon=eventMethodButton.GetThemeIcon("arrow","OptionButton");eventMethodButton.IconAlignment=HorizontalAlignment.Right;
        ConnectUi(eventMethodButton.GetPopup(),PopupMenu.SignalName.AboutToPopup,nameof(FillEventMethods));ConnectUi(eventMethodButton.GetPopup(),PopupMenu.SignalName.IdPressed,nameof(OnEventMethodChosen));
        var advanced=new VBoxContainer{Name="EventAdvanced",Visible=false};parent.AddChild(advanced);eventAdvanced=advanced;
        Button(parent,"進階","ToggleEventAdvanced",()=>eventAdvanced.Visible=!eventAdvanced.Visible);
        foreach(var field in new[]{eventAction,eventHandler})
        {
            var row=field.GetParent();var label=row is HBoxContainer?row.GetParent().GetChild<Control>(row.GetIndex()-1):row.GetChild<Control>(field.GetIndex()-1);
            if(row is HBoxContainer){row.GetParent().RemoveChild(label);advanced.AddChild(label);row.GetParent().RemoveChild(row);advanced.AddChild(row);}
            else {row.RemoveChild(label);advanced.AddChild(label);row.RemoveChild(field);advanced.AddChild(field);}
        }
        parent.RemoveChild(forwardEventArguments);advanced.AddChild(forwardEventArguments);
    }
    private void RefreshEventMethods()
    {
        if(eventReceiverButton==null)return;
        if(!eventReceiverDraft){int split=eventHandler.Text.LastIndexOf("::",StringComparison.Ordinal);eventReceiverPath=split>0?eventHandler.Text[..split]:"__dispatch";}
        eventReceiverButton.Text=eventReceiverPath=="__dispatch"?"Catalog Actions":eventReceiverPath;
        eventReceiverButton.Disabled=SelectedDefinition() is not ViewElementPlacement||AuthoringRoot()?.GetNodeOrNull(new NodePath(eventTarget.Text))?.HasSignal(eventSignal.Text)!=true;
        eventMethodButton.Disabled=eventReceiverButton.Disabled;eventMethodButton.Text=eventReceiverDraft||eventAction.Text.Length==0?"選擇方法…":eventAction.Text;
        eventMethodButton.TooltipText=eventHandler.Text;
    }
    private void FillEventMethods()
    {
        eventMethodChoices.Clear();var popup=eventMethodButton.GetPopup();popup.Clear();
        if(eventReceiverPath=="__dispatch"&&catalog!=null)
        {
            foreach(var action in catalog.Actions)
                eventMethodChoices.Add(new(action.Id,"",action.Id+" · "+action.HandlerMethod,false,action.HasHandlerMethod()));
            if(catalog.Actions.Count==0)foreach(string action in catalog.Entries.SelectMany(e=>e.Events.Concat(e.Elements.SelectMany(p=>p.Events))).Select(e=>e.Action).Where(a=>a.Length>0).Distinct())eventMethodChoices.Add(new(action,"",action,false,true));
        }
        else if(PageAuthoringRoot()?.GetNodeOrNull(new NodePath(eventReceiverPath)) is {} receiver)
        {
            foreach(var method in ReceiverMethods(receiver))
            {
                string args=string.Join(", ",method.GetParameters().Select(p=>p.ParameterType.Name+" "+p.Name));bool registered=false;
                try{registered=ViewAttributeBindings.For(receiver)?.Actions.Any(a=>a.Method==method)==true;}catch(InvalidOperationException){}
                eventMethodChoices.Add(new(method.Name,eventReceiverPath+"::"+method.Name,method.Name+"("+args+")"+(registered?"":" · 尚未註冊 AOT"),false,registered,method.GetParameters().Any(p=>p.ParameterType==typeof(ViewActionEvent))));
            }
            if(receiver.GetScript().AsGodotObject() is GDScript script)
            {
                var source=AuthoringRoot()?.GetNodeOrNull(new NodePath(eventTarget.Text));var signal=source?.GetSignalList().FirstOrDefault(s=>s["name"].AsString()==eventSignal.Text);var signalArgs=signal?["args"].AsGodotArray();
                foreach(var method in script.GetScriptMethodList())
                {
                    string name=method["name"].AsString();if(name.StartsWith("_",StringComparison.Ordinal))continue;
                    var args=method["args"].AsGodotArray();bool compatible=args.Count==0||signalArgs!=null&&args.Count==signalArgs.Count&&args.Select((a,i)=>
                    CompatibleSignalArgument(a,signalArgs[i])).All(v=>v);
                    string caption=name+"("+string.Join(", ",args.Select(a=>a.AsGodotDictionary()["name"].AsString()))+")";
                    eventMethodChoices.Add(new(name,eventReceiverPath+"::"+name,caption,true,compatible,args.Count>0));
                }
            }
        }
        for(int i=0;i<eventMethodChoices.Count;i++){popup.AddItem(eventMethodChoices[i].Caption,i);popup.SetItemDisabled(i,!eventMethodChoices[i].Enabled);}
        if(popup.ItemCount==0){popup.AddItem("沒有可用方法");popup.SetItemDisabled(0,true);}
    }
    private void OnEventMethodChosen(long index)
    {
        if(index<0||index>=eventMethodChoices.Count||!eventMethodChoices[(int)index].Enabled)return;
        var method=eventMethodChoices[(int)index];eventAction.Text=method.Action;eventHandler.Text=method.Handler;eventNativeHandler=method.Native;eventRelativeHandler=false;forwardEventArguments.SetPressedNoSignal(method.Forward);eventReceiverDraft=false;
        ArmAutoForm("event");CallDeferred(nameof(FlushAutoFormDeferred));RefreshEventMethods();
    }
    private string EventHandlerError()
    {
        if(eventHandler.Text.Length==0)return "";
        int split=eventHandler.Text.LastIndexOf("::",StringComparison.Ordinal);if(split<1)return "請選擇接收對象與方法。";
        var scope=eventRelativeHandler?AuthoringRoot():PageAuthoringRoot();var node=scope?.GetNodeOrNull(new NodePath(eventHandler.Text[..split]));if(node==null)return "接收節點不存在。";
        string name=eventHandler.Text[(split+2)..];
        if(!eventNativeHandler)
        {try{return ViewAttributeBindings.For(node)?.Actions.Count(a=>a.Method.Name==name&&ValidActionMethod(a.Method))==1?"":"方法未註冊或簽名不相容。";}catch(InvalidOperationException error){return error.Message;}}
        if(!node.HasMethod(name))return "接收方法不存在。";
        var method=node.GetMethodList().FirstOrDefault(m=>m["name"].AsString()==name);if(method==null)return "接收方法不存在。";
        var args=method["args"].AsGodotArray();var source=AuthoringRoot()?.GetNodeOrNull(new NodePath(eventTarget.Text));var signal=source?.GetSignalList().FirstOrDefault(s=>s["name"].AsString()==eventSignal.Text);
        var incoming=signal?["args"].AsGodotArray();int count=forwardEventArguments.ButtonPressed?incoming?.Count??0:0;
        if(args.Count!=count)return "方法參數數量與 Signal 不相容。";
        for(int i=0;i<count;i++)if(!CompatibleSignalArgument(args[i],incoming![i]))return "方法參數型別與 Signal 不相容。";
        return "";
    }
    private void ReleaseEventMethods(){eventReceiverButton=null!;eventMethodButton=null!;eventAdvanced=null!;eventMethodChoices.Clear();}
}
#endif
