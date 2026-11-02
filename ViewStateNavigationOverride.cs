using Godot;
namespace MacacaGames.ViewSystem;
[Tool,GlobalClass]
public partial class ViewStateNavigationOverride:Resource
{
    [Export] public string StateId {get;set;}="";
    [Export] public global::Godot.Collections.Array<ViewFocusLink> Links {get;set;}=new();
}
