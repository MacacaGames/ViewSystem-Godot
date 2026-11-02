using Godot;
using System;
namespace MacacaGames.ViewSystem;
public enum ViewButtonOperation { ChangePage,ShowOverlay,LeaveOverlay }
/// <summary>Native scene component for the three Unity handy-button workflows.</summary>
[Tool,GlobalClass]
public partial class ViewNavigationButton:Node
{
    [Signal] public delegate void NavigationFailedEventHandler(string message);
    [Export] public ViewHost? Host {get;set;}
    [Export] public NodePath HostPath {get;set;}=new("");
    [Export] public NodePath ButtonPath {get;set;}=new("");
    [Export] public ViewButtonOperation Operation {get;set;}
    [Export,ViewId] public string Page {get;set;}="";
    [Export] public bool IgnoreClickProtection {get;set;}
    [Export] public bool IgnoreTimeScale {get;set;}=true;
    private BaseButton? button;
    private bool busy;
    public override void _EnterTree()
    {
        if(Engine.IsEditorHint())return;
        if(!GodotObject.IsInstanceValid(Host))Host=HostPath.IsEmpty?null:GetNodeOrNull<ViewHost>(HostPath);
        button=ButtonPath.IsEmpty?GetParent() as BaseButton:GetNodeOrNull<BaseButton>(ButtonPath);
        if(button==null||Host==null)throw new InvalidOperationException("ViewNavigationButton needs a BaseButton and ViewHost.");
        button.Pressed+=Navigate;
    }
    private async void Navigate()
    {
        if(busy||Host==null)return;busy=true;
        try
        {
            var request=Operation==ViewButtonOperation.ChangePage?Host.FullPageChanger():Host.OverlayPageChanger();
            request.SetPage(Page).SetIgnoreClickProtection(IgnoreClickProtection).SetIgnoreTimeScale(IgnoreTimeScale);
            if(Operation==ViewButtonOperation.LeaveOverlay)await request.Leave();else await request.Show();
        }
        catch(Exception error){if(GodotObject.IsInstanceValid(this))EmitSignal(SignalName.NavigationFailed,error.Message);else GD.PushError(error.ToString());}
        finally{busy=false;}
    }
    public override void _ExitTree()
    {if(GodotObject.IsInstanceValid(button))button!.Pressed-=Navigate;button=null;}
}
