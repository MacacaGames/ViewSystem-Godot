using Godot;
namespace MacacaGames.ViewSystem;
/// <summary>Queue the native minimum-size/Container pass after a view becomes visible.</summary>
[GlobalClass]
public partial class ViewSystemLayoutRebuilder:ViewElementBehaviour
{
    [Export] public NodePath Target {get;set;}=new("..");
    public override void OnStartShow(){base.OnStartShow();CallDeferred(nameof(Rebuild));}
    public void Rebuild()
    {
        var root=GetNodeOrNull(Target);if(root==null)return;
        foreach(var node in ViewComposer.Walk(root))
        {if(node is Control control)control.UpdateMinimumSize();if(node is Container container)container.QueueSort();}
    }
}
