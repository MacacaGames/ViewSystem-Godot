#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
namespace MacacaGames.ViewSystem;

public partial class ViewSystemEditor
{
    private sealed record AuthoringChoice(string Value,string Caption,string Tooltip,Variant.Type Type=Variant.Type.Nil,bool Enabled=true,string Handler="",bool ToggleValue=false,string Icon="");
    private readonly List<AuthoringChoice> authoringChoices=new();
    private PopupPanel authoringPopup=null!;
    private ItemList authoringList=null!;
    private LineEdit authoringSearch=null!,parentPath=null!;
    private Control parentRow=null!;
    private string authoringKey="";
    private Button authoringBack=null!;
    private Label authoringLocation=null!;
    private string targetPickerSearch="";
    private string returnToAuthoringMember="";
    private string locateScene="",locatePath="";
    private Node2D? authoringOutline;
    private void BuildAuthoringPickers(Node items)
    {
        parentRow=new VBoxContainer {Name="PlacementParentRow"};items.AddChild(parentRow);
        parentPath=LabeledField(parentRow,"Parent","ParentPath","相對頁面根節點的父 Control 路徑",".");
        WrapPicker(parentPath,"Parent",()=>RunFormAction("parent",ApplyParentPath));
        foreach(var pair in new[]{(target,"Target"),(property,"Property"),(value,"Value"),(eventTarget,"EventTarget"),(eventSignal,"EventSignal"),(eventAction,"EventAction")})
            WrapPicker(pair.Item1,pair.Item2,null);
        ConnectUi(property,LineEdit.SignalName.TextChanged,nameof(OnAuthoringPropertyChanged));ConnectUi(target,LineEdit.SignalName.TextChanged,nameof(OnAuthoringPropertyChanged));ConnectUi(eventTarget,LineEdit.SignalName.TextChanged,nameof(OnAuthoringPropertyChanged));
        ConnectUi(kinds,OptionButton.SignalName.ItemSelected,nameof(OnAuthoringKindChanged));
        authoringPopup=new PopupPanel {Name="AuthoringPicker"};panel.AddChild(authoringPopup);
        var box=new VBoxContainer();authoringPopup.AddChild(box);
        var navigation=new HBoxContainer();box.AddChild(navigation);
        authoringBack=Button(navigation,"← 返回目標","PickerBack",BackToAuthoringTarget);
        authoringLocation=new Label {Name="PickerLocation",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};navigation.AddChild(authoringLocation);
        Button(navigation,"取消","CancelPicker",CancelAuthoringPicker);
        ConnectUi(authoringPopup,Popup.SignalName.PopupHide,nameof(OnAuthoringPickerHidden));
        authoringSearch=Field(box,"PickerSearch","搜尋","");
        ConnectUi(authoringSearch,LineEdit.SignalName.TextChanged,nameof(OnAuthoringSearch));
        authoringList=new ItemList {Name="PickerOptions",CustomMinimumSize=new Vector2(460,360),SizeFlagsVertical=Control.SizeFlags.ExpandFill,FixedIconSize=new Vector2I(16,16)};box.AddChild(authoringList);
        ConnectUi(authoringList,ItemList.SignalName.ItemActivated,nameof(OnAuthoringChosen));
        BuildAuthoringTree(box);
        Button(box,"選取","ConfirmPicker",ConfirmAuthoringChoice);
    }
    private void WrapPicker(LineEdit field,string key,Action? apply)
    {
        var owner=field.GetParent();int position=field.GetIndex();owner.RemoveChild(field);
        var row=new HBoxContainer {Name=key+"PickerRow"};owner.AddChild(row);owner.MoveChild(row,position);
        row.AddChild(new TextureRect {Name=key+"TypeIcon",CustomMinimumSize=new Vector2(16,16),SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=Control.MouseFilterEnum.Ignore});
        field.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(field);
        Button(row,"…","Pick"+key,()=>Try(()=>OpenAuthoringPicker(key)));
        if(key!="Value")Button(row,"↗","Locate"+key,()=>Try(()=>LocateAuthoringTarget(key)));
        if(apply!=null)Button(row,"✓","Apply"+key,apply);
    }
    private Control? AuthoringRoot()=>SelectedDefinition()==null?null:previews.FirstOrDefault(p=>p.Definition==entry) is ViewInstance view
        ?SelectedDefinition() is ViewElementPlacement item?view.Elements.GetValueOrDefault(item.Id):view.Root:null;
    private Control? PageAuthoringRoot()=>previews.FirstOrDefault(p=>p.Definition==entry)?.Root;
    private static IEnumerable<Node> PickerNodes(Node root)
    {
        yield return root;
        foreach(var child in root.GetChildren())
            if(child.Name!="_ViewSelectionOutline")foreach(var node in PickerNodes(child))yield return node;
    }
    private static string PathFrom(Node root,Node node)=>ReferenceEquals(root,node)?".":root.GetPathTo(node).ToString();
    private bool IsAvailableParent(Control node)
    {
        if(SelectedDefinition() is not ViewElementPlacement item||entry==null)return false;
        var current=AuthoringRoot();
        if(current!=null&&(node==current||current.IsAncestorOf(node)))return false;
        var page=PageAuthoringRoot();if(page==null)return false;
        int index=entry.Elements.IndexOf(item);
        foreach(var other in entry.Elements.Skip(index+1))
            if(previews.First(p=>p.Definition==entry).Elements.TryGetValue(other.Id,out var later)&&(node==later||later.IsAncestorOf(node)))return false;
        return true;
    }
    private IEnumerable<AuthoringChoice> AuthoringOptions(string key)
    {
        var root=key=="Parent"?PageAuthoringRoot():AuthoringRoot();
        if(key=="EventReceiver")
        {
            yield return new("__dispatch","Catalog Actions","應用程式派送入口",Icon:"Callable");
            if(PageAuthoringRoot() is {} pageRoot)foreach(var receiver in PickerNodes(pageRoot))if(HasReceiverMethods(receiver))
                yield return new(PathFrom(pageRoot,receiver),PathFrom(pageRoot,receiver)+" · "+receiver.GetType().Name,receiver.GetType().FullName??"",Icon:"Node");
            yield break;
        }
        if(key=="EventAction")
        {
            if(catalog==null)yield break;
            if(PageAuthoringRoot() is {} page)foreach(var node in PickerNodes(page))
                foreach(var method in node.GetType().GetMethods(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic))
                    if(method.GetCustomAttributes(typeof(ViewSystemEventAttribute),false).FirstOrDefault() is ViewSystemEventAttribute attribute)
                        yield return new(method.Name,attribute.Group+" / "+method.Name,node.GetType().Name+"."+method.Name,Handler:PathFrom(page,node)+"::"+method.Name,Icon:"Callable");
            if(catalog.Actions.Count>0)
                foreach(var action in catalog.Actions.OrderBy(a=>a.Id,StringComparer.Ordinal))
                    yield return new(action.Id,action.Id,$"{action.HandlerScript?.ResourcePath}:{action.HandlerMethod}",Enabled:action.HasHandlerMethod(),Icon:"Callable");
            else
                foreach(var action in catalog.Entries.SelectMany(e=>e.Events.Concat(e.Elements.SelectMany(p=>p.Events))).Select(e=>e.Action).Where(a=>a.Length>0).Distinct().OrderBy(a=>a,StringComparer.Ordinal))
                    yield return new(action,action,"既有 binding；可於 Catalog.Actions 宣告應用程式派送入口。",Icon:"Callable");
            yield break;
        }
        if(root==null)yield break;
        if(key is "Target" or "EventTarget" or "Parent")
        {
            foreach(var node in PickerNodes(root))
            {
                if(key=="Parent"&&(node is not Control control||!IsAvailableParent(control)))continue;
                string path=PathFrom(root,node);int depth=path=="."?0:path.Count(c=>c=='/')+1;
                yield return new(path,new string(' ',depth*2)+(path=="."?root.Name.ToString():node.Name.ToString()),path+" · "+node.GetClass(),Icon:node.GetClass());
            }
            yield break;
        }
        string relative=key is "Property" or "Value"?target.Text:eventTarget.Text;
        var selected=root.GetNodeOrNull(new NodePath(relative));if(selected==null)yield break;
        if(key=="Value")
        {
            GodotObject owner=selected;var path=property.Text.Split(':');
            for(int i=0;i<path.Length;i++)
            {
                var info=owner.GetPropertyList().FirstOrDefault(p=>p["name"].AsString()==path[i]);if(info==null)yield break;
                if(i<path.Length-1){var nested=owner.Get(path[i]);if(nested.VariantType!=Variant.Type.Object||nested.AsGodotObject() is not Resource resource)yield break;owner=resource;continue;}
                var hint=(PropertyHint)info["hint"].AsInt32();if(hint is not (PropertyHint.Enum or PropertyHint.Flags))yield break;
                var names=info["hint_string"].AsString().Split(',');long.TryParse(value.Text,out long current);
                for(int n=0;n<names.Length;n++)
                {
                    string caption=names[n];int split=caption.LastIndexOf(':');long number=hint==PropertyHint.Flags?1L<<n:n;
                    if(split>=0&&long.TryParse(caption[(split+1)..],out long explicitValue)){number=explicitValue;caption=caption[..split];}
                    yield return new(number.ToString(CultureInfo.InvariantCulture),(hint==PropertyHint.Flags&&(current&number)!=0?"✓ ":"")+caption,caption+" = "+number,Variant.Type.Int,ToggleValue:hint==PropertyHint.Flags,Icon:"int");
                }
            }
            yield break;
        }
        if(key=="Property")
            foreach(var choice in PropertyOptions(selected,"",new HashSet<ulong>(),0))yield return choice;
        else if(key=="EventSignal")
            foreach(var signal in selected.GetSignalList().OrderBy(s=>s["name"].AsString(),StringComparer.Ordinal))
            {
                string name=signal["name"].AsString();int count=signal["args"].AsGodotArray().Count;
                yield return new(name,name+(count>0?" (…)":""),count>0?"原生 signal；可在事件設定轉送參數。":name,Enabled:count<=8,Icon:"Signal");
            }
    }
    private IEnumerable<AuthoringChoice> PropertyOptions(GodotObject owner,string prefix,HashSet<ulong> visited,int depth)
    {
        if(depth>3||!visited.Add(owner.GetInstanceId()))yield break;
        foreach(var info in owner.GetPropertyList().OrderBy(i=>i["name"].AsString(),StringComparer.Ordinal))
        {
            string name=info["name"].AsString();var usage=(PropertyUsageFlags)info["usage"].AsInt64();
            if((usage&PropertyUsageFlags.Editor)==0||(usage&PropertyUsageFlags.ReadOnly)!=0)continue;
            var type=(Variant.Type)info["type"].AsInt32();string path=prefix+name;
            bool supported=type is not (Variant.Type.Nil or Variant.Type.Callable or Variant.Type.Signal or Variant.Type.Rid);
            if(supported)yield return new(path,path,owner.GetClass()+" · "+type,type,Icon:ValueTypeIcon(type));
            if(type==Variant.Type.Object&&owner.Get(name).AsGodotObject() is Resource resource)
                foreach(var child in PropertyOptions(resource,path+":",visited,depth+1))yield return child;
        }
        visited.Remove(owner.GetInstanceId());
    }
    public string[] GetAuthoringOptions(string key)=>AuthoringOptions(key).Where(o=>o.Enabled).Select(o=>o.Value).ToArray();
    private void OpenAuthoringPicker(string key,bool guided=false)
    {
        if(!guided){bindingFlow="";returnToAuthoringMember="";}
        if(key is "Target" or "EventTarget")targetPickerSearch="";
        authoringKey=key;authoringChoices.Clear();authoringChoices.AddRange(AuthoringOptions(key));authoringSearch.Text="";
        authoringBack.Visible=key is "Property" or "EventSignal";
        authoringLocation.Text=key switch {"Property"=>target.Text+" · 屬性","EventSignal"=>eventTarget.Text+" · Signal",_=>"選擇目標"};
        RefreshAuthoringChoices();if(!authoringPopup.Visible)authoringPopup.PopupCentered(new Vector2I(540,480));authoringSearch.GrabFocus();
    }
    private void BackToAuthoringTarget()
    {
        string key=authoringKey=="EventSignal"?"EventTarget":"Target", search=targetPickerSearch;
        returnToAuthoringMember=authoringKey;
        OpenAuthoringPicker(key,true);authoringSearch.Text=search;RefreshAuthoringChoices();
    }
    private void OnAuthoringSearch(string text)=>RefreshAuthoringChoices();
    private void RefreshAuthoringChoices()
    {
        RefreshAuthoringTree();
        authoringList.Clear();
        for(int index=0;index<authoringChoices.Count;index++)
        {
            var choice=authoringChoices[index];
            if(authoringSearch.Text.Length>0&&!(choice.Value+" "+choice.Caption).Contains(authoringSearch.Text,StringComparison.OrdinalIgnoreCase))continue;
            int row=authoringList.AddItem(choice.Caption,EditorTypeIcon(choice.Icon));authoringList.SetItemMetadata(row,index);authoringList.SetItemTooltip(row,choice.Tooltip);authoringList.SetItemDisabled(row,!choice.Enabled);
        }
    }
    private void OnAuthoringChosen(long row)=>ChooseAuthoringChoice(authoringList.GetItemMetadata((int)row).AsInt32());
    private bool choosingAuthoringChoice;
    private void ChooseAuthoringChoice(int index)
    {choosingAuthoringChoice=true;try{ApplyAuthoringChoice(index);}finally{choosingAuthoringChoice=false;}}
    private void ApplyAuthoringChoice(int index)=>Try(()=>
    {
        var choice=authoringChoices[index];if(!choice.Enabled)return;string key=authoringKey;
        if(key is "Target" or "EventTarget")targetPickerSearch=authoringSearch.Text;
        switch(key)
        {
            case "Value":long.TryParse(value.Text,out long mask);value.Text=choice.ToggleValue?(mask^long.Parse(choice.Value,CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture):choice.Value;modelKey.Text="";break;
            case "Target":target.Text=choice.Value;break;
            case "EventTarget":eventTarget.Text=choice.Value;break;
            case "EventSignal":eventSignal.Text=choice.Value;break;
            case "EventAction":eventAction.Text=choice.Value;eventHandler.Text=choice.Handler;break;
            case "EventReceiver":eventReceiverPath=choice.Value;eventReceiverDraft=true;RefreshEventMethods();break;
            case "Parent":parentPath.Text=choice.Value;ApplyParentPath();break;
            case "Property":property.Text=choice.Value;RefreshTypedOverrideEditor(true);break;
        }
        OnAuthoringPropertyChanged("");RefreshBindingActions();
        if(key=="Value"&&choice.ToggleValue){authoringChoices.Clear();authoringChoices.AddRange(AuthoringOptions("Value"));RefreshAuthoringChoices();return;}
        bool advance=key is "Target" or "EventTarget"&&(returnToAuthoringMember.Length>0||bindingFlow is "override" or "event");
        if(!advance)authoringPopup.Hide();if(key is not ("EventAction" or "Value"))HighlightAuthoringTarget(key);
        if(returnToAuthoringMember.Length>0&&key is "Target" or "EventTarget")
        {string member=returnToAuthoringMember;returnToAuthoringMember="";OpenAuthoringPicker(member,true);return;}
        ContinueBindingFlow(key);
    });
    private void ValidateParentForm()
    {
        if(SelectedDefinition() is not ViewElementPlacement placement)throw new InvalidOperationException("Select an Element placement.");
        var parent=PageAuthoringRoot()?.GetNodeOrNull<Control>(new NodePath(parentPath.Text));
        if(parent==null||!IsAvailableParent(parent))throw new InvalidOperationException("Parent must exist before this placement and cannot be inside it.");
        if(parent is Container&&placement.Layout!=null)throw new InvalidOperationException("Container owns layout. Use native scene layout before reparenting.");
    }
    private void ApplyParentPath()
    {
        ValidateParentForm();var placement=(ViewElementPlacement)SelectedDefinition()!;
        AcceptCurrentDraft("parent");Change(placement,"ParentPath",new NodePath(parentPath.Text),placement.ParentPath,"ViewSystem parent");
    }
    private void OnAuthoringPropertyChanged(string text){RefreshValuePicker();RefreshAuthoringIcons();RefreshTypedOverrideEditor();RefreshBindingActions();}
    private void OnAuthoringKindChanged(long index)=>RefreshAuthoringIcons();
    private static Texture2D EditorTypeIcon(string name)
    {
        var theme=EditorInterface.Singleton.GetEditorTheme();
        while(name.Length>0)
        {
            if(theme.HasIcon(name,"EditorIcons"))return theme.GetIcon(name,"EditorIcons");
            if(!ClassDB.ClassExists(name))break;name=ClassDB.GetParentClass(name).ToString();
        }
        return theme.GetIcon("Node","EditorIcons");
    }
    private static string ValueTypeIcon(Variant.Type type)=>type switch
    {
        Variant.Type.Int=>"int",Variant.Type.Float=>"float",Variant.Type.Bool=>"bool",Variant.Type.Object=>"Resource",
        Variant.Type.StringName=>"StringName",_=>type.ToString()
    };
    private void WithAuthoringIconRoot(Action<Control?> draw)
    {
        var root=AuthoringRoot();Node? temporary=null;
        try
        {
            if(root==null)
            {
                var (scene,path)=SelectedDefinition() switch
                {ViewEntry page=>(page.Scene,page.ScenePath),ViewElementPlacement item=>(item.Scene,item.ScenePath),_=>(null,"")};
                if(scene==null&&path.Length>0&&ResourceLoader.Exists(path,"PackedScene"))scene=GD.Load<PackedScene>(path);
                temporary=scene?.Instantiate();root=temporary as Control;
            }
            draw(root);
        }
        finally{temporary?.Free();}
    }
    private static GodotObject? OverrideOwner(Control root,string targetPath,string propertyPath,out Variant.Type type)
    {
        type=Variant.Type.Nil;GodotObject? owner=root.GetNodeOrNull(new NodePath(targetPath));var parts=propertyPath.Split(':');
        for(int index=0;index<parts.Length&&owner!=null;index++)
        {
            var info=owner.GetPropertyList().FirstOrDefault(p=>p["name"].AsString()==parts[index]);if(info==null)return null;
            type=(Variant.Type)info["type"].AsInt32();
            if(index==parts.Length-1)return owner;
            var value=owner.Get(parts[index]);owner=value.VariantType==Variant.Type.Object?value.AsGodotObject() as Resource:null;
        }
        return null;
    }
    private void RefreshOverrideIcons()
    {
        if(!GodotObject.IsInstanceValid(rules)||rules.ItemCount==0)return;
        WithAuthoringIconRoot(root=>
        {
            var values=SelectedRules();
            for(int index=0;index<Math.Min(values.Count,rules.ItemCount);index++)
            {
                var rule=values[index];var owner=rule==null||root==null?null:OverrideOwner(root,rule.Target.ToString(),rule.Property,out _);
                string icon=owner?.GetClass()??(root==null?"Node":"Warning");rules.SetItemIcon(index,EditorTypeIcon(icon));
                rules.SetItemTooltip(index,rule==null?"空白 Override":$"{rule.Target}:{rule.Property}\n{owner?.GetClass()??(root==null?"來源尚未載入":"找不到目標或屬性")}");
            }
        });
    }
    private void RefreshAuthoringIcons()
    {
        if(!GodotObject.IsInstanceValid(panel)||HasMeta(ReloadState))return;
        WithAuthoringIconRoot(root=>
        {
            void Set(string key,string icon,string tooltip)
            {if(panel.FindChild(key+"TypeIcon",true,false) is TextureRect image){image.Texture=EditorTypeIcon(icon);image.TooltipText=tooltip;}}
            var node=root?.GetNodeOrNull(new NodePath(target.Text));Set("Target",node?.GetClass()??(root==null?"Node":"Warning"),node?.GetClass()??target.Text);
            var owner=root==null?null:OverrideOwner(root,target.Text,property.Text,out _);
            Set("Property",owner?.GetClass()??(root==null?"Node":"Warning"),owner?.GetClass()??property.Text);
            Set("Value",ValueTypeIcon((ViewValueKind)kinds.Selected switch {ViewValueKind.Integer=>Variant.Type.Int,ViewValueKind.Number=>Variant.Type.Float,ViewValueKind.Boolean=>Variant.Type.Bool,ViewValueKind.Color=>Variant.Type.Color,ViewValueKind.Vector2=>Variant.Type.Vector2,ViewValueKind.Vector3=>Variant.Type.Vector3,ViewValueKind.Vector2I=>Variant.Type.Vector2I,ViewValueKind.Vector3I=>Variant.Type.Vector3I,ViewValueKind.Resource=>Variant.Type.Object,ViewValueKind.Native=>typedOverrideDraft?.NativeValue.VariantType??Variant.Type.Nil,_=>Variant.Type.String}),"Value");
            var eventNode=root?.GetNodeOrNull(new NodePath(eventTarget.Text));Set("EventTarget",eventNode?.GetClass()??"Node",eventNode?.GetClass()??eventTarget.Text);
            Set("EventSignal","Signal","Signal");Set("EventAction","Callable","Action");
            var parent=PageAuthoringRoot()?.GetNodeOrNull(new NodePath(parentPath.Text));Set("Parent",parent?.GetClass()??"Node",parent?.GetClass()??parentPath.Text);
        });
    }
    private void RefreshValuePicker()
    {if(HasMeta(ReloadState)||!GodotObject.IsInstanceValid(panel))return;if(panel.FindChild("PickValue",true,false) is Control picker)picker.Visible=AuthoringOptions("Value").Any();}
    private void RefreshAuthoringSelection()
    {
        RefreshValuePicker();
        if(!GodotObject.IsInstanceValid(parentRow))return;
        parentRow.Visible=SelectedDefinition() is ViewElementPlacement;
        parentPath.Text=(SelectedDefinition() as ViewElementPlacement)?.ParentPath.ToString()??".";
        RefreshAuthoringIcons();
    }
    private void HighlightAuthoringTarget(string key)
    {
        if(GodotObject.IsInstanceValid(authoringOutline))authoringOutline!.Free();authoringOutline=null;
        var root=key=="Parent"?PageAuthoringRoot():AuthoringRoot();if(root==null)return;
        string path=key=="Parent"?parentPath.Text:key.StartsWith("Event",StringComparison.Ordinal)?eventTarget.Text:target.Text;
        Node? node=root.GetNodeOrNull(new NodePath(path));bool missing=node==null;
        while(node==null&&path!=".") {int slash=path.LastIndexOf('/');path=slash>=0?path[..slash]:".";node=root.GetNodeOrNull(new NodePath(path));}
        if(node is not Control control)return;
        using var script=ResourceLoader.Load<GDScript>("res://addons/macaca_viewsystem/Editor/selection_outline.gd");
        var frame=(Node2D)script.New().AsGodotObject();frame.Name="_ViewSelectionOutline";frame.ZIndex=(int)RenderingServer.CanvasItemZMax;control.AddChild(frame);
        var color=missing||PreviewError.Length>0?new Color(1,.25f,.2f):new Color(.2f,.7f,1);
        frame.Call("configure",control.Size,color);
        authoringOutline=frame;
        if(missing)ShowFormError(key.StartsWith("Event",StringComparison.Ordinal)?"event":"override","Missing target: "+(key.StartsWith("Event",StringComparison.Ordinal)?eventTarget.Text:target.Text));
    }
    private void LocateAuthoringTarget(string key)
    {
        if(key!="EventAction")
        {
            if(AuthoringRoot()!=null){HighlightAuthoringTarget(key);return;}
            var scene=key=="Parent"?entry?.Scene:SelectedDefinition() is ViewElementPlacement placement?placement.Scene:entry?.Scene;
            if(scene==null||scene.ResourcePath.Length==0)throw new InvalidOperationException("No source Scene is available for this target.");
            locateScene=scene.ResourcePath;locatePath=key=="Parent"?parentPath.Text:key.StartsWith("Event",StringComparison.Ordinal)?eventTarget.Text:target.Text;
            EditorInterface.Singleton.OpenSceneFromPath(locateScene);CallDeferred(nameof(LocateSourceNode),0);return;
        }
        if(eventHandler.Text.Length>0)
        {int split=eventHandler.Text.LastIndexOf("::",StringComparison.Ordinal);var node=split>0?PageAuthoringRoot()?.GetNodeOrNull(new NodePath(eventHandler.Text[..split])):null;if(node?.GetScript().AsGodotObject() is Script script){EditorInterface.Singleton.GetFileSystemDock().NavigateToPath(script.ResourcePath);EditorInterface.Singleton.EditScript(script);return;}throw new InvalidOperationException("Local handler source is missing.");}
        var action=catalog?.Actions.FirstOrDefault(a=>a.Id==eventAction.Text);
        if(action?.HandlerScript==null)throw new InvalidOperationException("Declare this Action's HandlerScript/HandlerMethod in Catalog.Actions.");
        if(!action.HasHandlerMethod())throw new InvalidOperationException("Handler method not found: "+action.HandlerMethod);
        EditorInterface.Singleton.GetFileSystemDock().NavigateToPath(action.HandlerScript.ResourcePath);
        EditorInterface.Singleton.EditResource(action);
    }
    private void LocateSourceNode(int attempt)
    {
        if(!GodotObject.IsInstanceValid(panel)||locateScene.Length==0)return;
        var root=EditorInterface.Singleton.GetEditedSceneRoot();
        if(root==null||root.SceneFilePath!=locateScene)
        {if(attempt<20)CallDeferred(nameof(LocateSourceNode),attempt+1);return;}
        string path=locatePath;Node? node=root.GetNodeOrNull(new NodePath(path));
        while(node==null&&path!="."){int slash=path.LastIndexOf('/');path=slash>=0?path[..slash]:".";node=root.GetNodeOrNull(new NodePath(path));}
        if(node!=null){var selection=EditorInterface.Singleton.GetSelection();selection.Clear();selection.AddNode(node);EditorInterface.Singleton.EditNode(node);}
        locateScene="";
    }
    private void ReleaseAuthoringPickers()
    {ReleaseAuthoringTrees();authoringChoices.Clear();locateScene="";locatePath="";targetPickerSearch="";returnToAuthoringMember="";authoringBack=null!;authoringLocation=null!;authoringPopup=null!;authoringList=null!;authoringSearch=null!;parentPath=null!;parentRow=null!;authoringOutline=null;}
}
#endif
