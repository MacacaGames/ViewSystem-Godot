using Godot;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
namespace MacacaGames.ViewSystem;

[GlobalClass]
public partial class ViewElementGroup:Control
{
    [Export] public bool OnlyManualMode {get;set;}
    [Export] public ViewElementAnimation? ShowTransition {get;set;}
    [Export] public ViewElementAnimation? HideTransition {get;set;}
    public async Task SetShownAsync(bool show,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var nodes=ViewComposer.Walk(this).OfType<IViewElementLifeCycle>().ToArray();
        foreach(var node in nodes){node.OnChangePage(show);if(show)node.OnBeforeShow();else node.OnBeforeLeave();}
        if(show)Show();foreach(var node in nodes)if(show)node.OnStartShow();else node.OnStartLeave();
        var animation=show?ShowTransition:HideTransition;if(animation!=null)await animation.PlayAsync(this,show,token);
        if(!show)Hide();foreach(var node in nodes){if(show)node.OnShown();else node.OnHidden();node.OnChangedPage();}
    }
}
