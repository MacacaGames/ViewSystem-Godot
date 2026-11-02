using Godot;
namespace MacacaGames.ViewSystem;

/// <summary>Optional lifecycle component; may be a child of any native Control.</summary>
[GlobalClass]
public partial class ViewElementBehaviour:Node,IViewElementLifeCycle
{
    [Signal] public delegate void BeforeShowEventHandler();
    [Signal] public delegate void StartShowEventHandler();
    [Signal] public delegate void ShownEventHandler();
    [Signal] public delegate void BeforeLeaveEventHandler();
    [Signal] public delegate void StartLeaveEventHandler();
    [Signal] public delegate void HiddenEventHandler();
    [Signal] public delegate void RefreshedEventHandler();
    public virtual void OnBeforeShow()=>EmitSignal(SignalName.BeforeShow);
    public virtual void OnStartShow()=>EmitSignal(SignalName.StartShow);
    public virtual void OnShown()=>EmitSignal(SignalName.Shown);
    public virtual void OnBeforeLeave()=>EmitSignal(SignalName.BeforeLeave);
    public virtual void OnStartLeave()=>EmitSignal(SignalName.StartLeave);
    public virtual void OnHidden()=>EmitSignal(SignalName.Hidden);
    public virtual void RefreshView()=>EmitSignal(SignalName.Refreshed);
    public virtual void OnChangePage(bool show){}
    public virtual void OnChangedPage(){}
}
