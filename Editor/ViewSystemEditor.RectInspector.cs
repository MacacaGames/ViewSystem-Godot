#if TOOLS
using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;

public partial class ViewSystemEditor
{
    private readonly Dictionary<string,SpinBox> rectFields=new();
    private readonly Dictionary<string,Label> rectLabels=new();
    private GridContainer anchorPresets=null!;
    private PopupPanel anchorPopup=null!;
    private Button anchorButton=null!;
    private Resource? rectClipboard;
    private bool syncingRect,rectNormalizePending;
    private Control? SelectedControl()=>SelectedDefinition() is ViewElementPlacement p ? previews.FirstOrDefault(i=>i.Definition==entry)?.Elements.GetValueOrDefault(p.Id):null;
    private void BuildRectInspector(VBoxContainer parent)
    {
        layoutResponsibility.Visible=false;
        var top=new HBoxContainer();parent.AddChild(top);
        anchorButton=PresetButton(-1,-1,"AnchorSelector");top.AddChild(anchorButton);
        anchorButton.TooltipText="Anchor Presets";anchorButton.SizeFlagsVertical=Control.SizeFlags.ShrinkBegin;
        string key=anchorButton.GetInstanceId().ToString();buttonActions.Add(key,()=>anchorPopup.Popup(new Rect2I((Vector2I)anchorButton.GlobalPosition+(Vector2I)anchorButton.GetWindow().Position,(Vector2I)(anchorPresets.GetCombinedMinimumSize()+new Vector2(12,12)))));ConnectUi(anchorButton,BaseButton.SignalName.Pressed,nameof(OnButtonPressed),key);
        var geometry=new GridContainer {Columns=2,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};top.AddChild(geometry);
        foreach(var name in new[]{"X","Y","W","H"})RectCell(geometry,name,name);
        anchorPopup=new PopupPanel {Name="AnchorPopup"};parent.AddChild(anchorPopup);
        var popupBody=new VBoxContainer();anchorPopup.AddChild(popupBody);
        anchorPresets=new GridContainer {Name="AnchorPresets",Columns=4};popupBody.AddChild(anchorPresets);
        for(int y=0;y<4;y++)for(int x=0;x<4;x++)
        {
            int px=x,py=y;var button=PresetButton(x,y,$"AnchorPreset{x}{y}");anchorPresets.AddChild(button);
            string id=button.GetInstanceId().ToString();buttonActions.Add(id,()=>Try(()=>ApplyAnchorPreset(px,py)));ConnectUi(button,BaseButton.SignalName.Pressed,nameof(OnButtonPressed),id);
        }
        var anchors=new VBoxContainer {Name="AnchorDetails",Visible=false};parent.AddChild(anchors);
        var fold=Button(parent,"Anchors","AnchorDetailsToggle",()=>anchors.Visible=!anchors.Visible);parent.MoveChild(fold,anchors.GetIndex());
        RectVector(anchors,"Min","MinX","MinY");RectVector(anchors,"Max","MaxX","MaxY");
        RectVector(parent,"Pivot","PivotU","PivotV");
        RectVector(parent,"Scale","ScaleU","ScaleV");
        var rotation=new HBoxContainer();parent.AddChild(rotation);rotation.AddChild(new Label {Text="Rotation",CustomMinimumSize=new Vector2(65,0)});RectCell(rotation,"Degrees","Z");
        var actions=new HBoxContainer();parent.AddChild(actions);
        Button(actions,"Pick","PickRect",()=>Try(PickRect));
        Button(actions,"Copy","CopyRect",()=>{if(SelectedDefinition() is ViewElementPlacement {LayoutData:{} data}){rectClipboard=data.Duplicate(true);RefreshSmartRect();}});
        Button(actions,"Paste","PasteRect",()=>{if(rectClipboard!=null && SelectedDefinition() is ViewElementPlacement item)Change(item,"LayoutData",rectClipboard.Duplicate(true),item.LayoutData,"Paste Rect Transform");});
        var advanced=new VBoxContainer {Name="RectAdvanced",Visible=false};parent.AddChild(advanced);
        var advancedFold=Button(parent,"Advanced","RectAdvancedToggle",()=>advanced.Visible=!advanced.Visible);parent.MoveChild(advancedFold,advanced.GetIndex());
        RectVector(advanced,"Order","ZOrder","Sibling");
        anchorDiagram=(Control)ResourceLoader.Load<GDScript>("res://addons/macaca_viewsystem/Editor/anchor_diagram.gd").New().AsGodotObject();anchorDiagram.Name="AnchorDiagram";anchorDiagram.CustomMinimumSize=new Vector2(0,110);anchors.AddChild(anchorDiagram);
        ConnectUi(anchorDiagram,"anchors_edited",nameof(OnAnchorDragged));
    }
    private Button PresetButton(int x,int y,string name)
    {
        var result=(Button)ResourceLoader.Load<GDScript>("res://addons/macaca_viewsystem/Editor/anchor_preset.gd").New().AsGodotObject();
        result.Name=name;result.CustomMinimumSize=new Vector2(48,48);result.ClipContents=true;
        if(x>=0){result.ToggleMode=true;result.SetMeta("preset_x",x);result.SetMeta("preset_y",y);result.Call("configure",x,y);result.TooltipText=new[]{"Left","Center","Right","Stretch"}[x]+" / "+new[]{"Top","Middle","Bottom","Stretch"}[y];}
        return result;
    }
    private void RectVector(Node parent,string text,string x,string y)
    {
        var row=new HBoxContainer();parent.AddChild(row);row.AddChild(new Label {Text=text,CustomMinimumSize=new Vector2(65,0)});RectCell(row,x,"X");RectCell(row,y,"Y");
    }
    private void RectCell(Node parent,string key,string caption)
    {
        var cell=new VBoxContainer {SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};parent.AddChild(cell);
        var label=new Label {Text=caption};cell.AddChild(label);rectLabels.Add(key,label);
        var input=new SpinBox {Name="Rect"+key,MinValue=-1000000,MaxValue=1000000,Step=key is "ZOrder" or "Sibling"?1:0,CustomArrowStep=1,UpdateOnTextChanged=true,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new Vector2(65,0)};
        cell.AddChild(input);rectFields.Add(key,input);ConnectUi(input,Godot.Range.SignalName.ValueChanged,nameof(OnRectEdited),key);
    }
    private Vector2 RectPivot(ViewElementLayout l)
    {
        if(l.UseNormalizedPivot)return l.NormalizedPivot;
        var size=SelectedControl()?.Size??new Vector2(l.OffsetRight-l.OffsetLeft,l.OffsetBottom-l.OffsetTop);
        return new Vector2(size.X==0?0:l.PivotOffset.X/size.X,size.Y==0?0:l.PivotOffset.Y/size.Y);
    }
    private void RefreshSmartRect()
    {
        if(rectFields.Count==0)return;syncingRect=true;
        var l=(SelectedDefinition() as ViewElementPlacement)?.Layout;
        foreach(var field in rectFields.Values)field.Editable=l!=null;
        anchorButton.Disabled=l==null;
        var control=SelectedControl();bool container=control?.GetParent() is Container;
        var host=anchorButton.GetParent().GetParent();
        ((Button)host.FindChild("PickRect",true,false)).Disabled=control==null||container;
        ((Button)host.FindChild("CopyRect",true,false)).Disabled=l==null;
        ((Button)host.FindChild("PasteRect",true,false)).Disabled=SelectedDefinition() is not ViewElementPlacement||rectClipboard==null||container;
        if(l!=null)
        {
            bool sx=l.AnchorLeft!=l.AnchorRight,sy=l.AnchorTop!=l.AnchorBottom;var p=RectPivot(l);
            rectLabels["X"].Text=sx?"Left":"Pos X";rectLabels["W"].Text=sx?"Right":"Width";
            rectLabels["Y"].Text=sy?"Top":"Pos Y";rectLabels["H"].Text=sy?"Bottom":"Height";
            var values=new Dictionary<string,double> {
                ["X"]=sx?l.OffsetLeft:l.OffsetLeft+(l.OffsetRight-l.OffsetLeft)*p.X,
                ["Y"]=sy?l.OffsetTop:-(l.OffsetTop+(l.OffsetBottom-l.OffsetTop)*p.Y),
                ["W"]=sx?-l.OffsetRight:l.OffsetRight-l.OffsetLeft,["H"]=sy?-l.OffsetBottom:l.OffsetBottom-l.OffsetTop,
                ["MinX"]=l.AnchorLeft,["MinY"]=1-l.AnchorBottom,["MaxX"]=l.AnchorRight,["MaxY"]=1-l.AnchorTop,
                ["PivotU"]=p.X,["PivotV"]=1-p.Y,["ScaleU"]=l.Scale.X,["ScaleV"]=l.Scale.Y,["Degrees"]=Mathf.RadToDeg(l.Rotation),["ZOrder"]=l.SortOrder,["Sibling"]=((ViewElementPlacement)SelectedDefinition()!).SiblingIndex};
            foreach(var value in values)rectFields[value.Key].SetValueNoSignal(value.Value);
            foreach(var button in anchorPresets.GetChildren().OfType<Button>())
            {
                int x=button.GetMeta("preset_x").AsInt32(),y=button.GetMeta("preset_y").AsInt32();
                button.SetPressedNoSignal(l.AnchorLeft==(x==3?0:x*.5f)&&l.AnchorRight==(x==3?1:x*.5f)&&l.AnchorTop==(y==3?0:y*.5f)&&l.AnchorBottom==(y==3?1:y*.5f));
            }
            anchorButton.Call("show_anchors",new Vector2(l.AnchorLeft,l.AnchorTop),new Vector2(l.AnchorRight,l.AnchorBottom));
        }
        syncingRect=false;
    }
    private void OnRectEdited(double value,string key)
    {
        if(syncingRect||HasMeta(ReloadState)||SelectedDefinition() is not ViewElementPlacement {Layout:{} l})return;
        var p=RectPivot(l);bool sx=l.AnchorLeft!=l.AnchorRight,sy=l.AnchorTop!=l.AnchorBottom;
        syncingLayout=true;
        if(!l.UseNormalizedPivot){rectNormalizePending=true;layoutFields["PivotX"].SetValueNoSignal(p.X);layoutFields["PivotY"].SetValueNoSignal(p.Y);}
        void Set(string name,double v)=>layoutFields[name].SetValueNoSignal(v);
        if(key=="X") {if(sx)Set("OffsetLeft",value);else {double d=value-(l.OffsetLeft+(l.OffsetRight-l.OffsetLeft)*p.X);Set("OffsetLeft",l.OffsetLeft+d);Set("OffsetRight",l.OffsetRight+d);}}
        if(key=="Y") {if(sy)Set("OffsetTop",value);else {double d=-value-(l.OffsetTop+(l.OffsetBottom-l.OffsetTop)*p.Y);Set("OffsetTop",l.OffsetTop+d);Set("OffsetBottom",l.OffsetBottom+d);}}
        if(key=="W") {if(sx)Set("OffsetRight",-value);else {double pos=l.OffsetLeft+(l.OffsetRight-l.OffsetLeft)*p.X;Set("OffsetLeft",pos-value*p.X);Set("OffsetRight",pos+value*(1-p.X));}}
        if(key=="H") {if(sy)Set("OffsetBottom",-value);else {double pos=l.OffsetTop+(l.OffsetBottom-l.OffsetTop)*p.Y;Set("OffsetTop",pos-value*p.Y);Set("OffsetBottom",pos+value*(1-p.Y));}}
        if(key=="MinX")Set("AnchorLeft",value);if(key=="MaxX")Set("AnchorRight",value);
        if(key=="MinY")Set("AnchorBottom",1-value);if(key=="MaxY")Set("AnchorTop",1-value);
        var size=SelectedControl()?.Size??Vector2.One;
        if(key=="PivotU")Set("PivotX",rectNormalizePending||l.UseNormalizedPivot?value:value*size.X);
        if(key=="PivotV")Set("PivotY",rectNormalizePending||l.UseNormalizedPivot?1-value:(1-value)*size.Y);
        if(key=="ScaleU")Set("ScaleX",value);if(key=="ScaleV")Set("ScaleY",value);
        if(key=="Degrees")Set("Rotation",Mathf.DegToRad((float)value));if(key=="ZOrder")Set("SortOrder",value);if(key=="Sibling")Set("SiblingIndex",value);
        syncingLayout=false;try{Try(ApplyPlacementLayout);}finally{rectNormalizePending=false;}
    }
    private void PickRect()
    {
        if(SelectedDefinition() is not ViewElementPlacement item || SelectedControl() is not {} c)return;
        if(c.GetParent() is Container)throw new InvalidOperationException("Parent Container owns layout.");
        var l=new ViewElementLayout(new Resource()){AnchorLeft=c.AnchorLeft,AnchorTop=c.AnchorTop,AnchorRight=c.AnchorRight,AnchorBottom=c.AnchorBottom,OffsetLeft=c.OffsetLeft,OffsetTop=c.OffsetTop,OffsetRight=c.OffsetRight,OffsetBottom=c.OffsetBottom,PivotOffset=c.PivotOffset,Scale=c.Scale,Rotation=c.Rotation,SortOrder=c.ZIndex};
        Change(item,"LayoutData",l.Data,item.LayoutData,"Pick Rect Transform");
    }
    private void ReleaseRectInspector(){rectFields.Clear();rectLabels.Clear();anchorPopup=null!;anchorPresets=null!;anchorButton=null!;}
}
#endif
