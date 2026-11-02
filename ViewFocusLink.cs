using Godot;
namespace MacacaGames.ViewSystem;

public enum ViewFocusDirection { Automatic,None,Horizontal,Vertical,Explicit }
/// <summary>Native focus neighbours, relative to the composed page root.</summary>
[Tool,GlobalClass]
public partial class ViewFocusLink:Resource
{
    [Export] public NodePath Target {get;set;}=new(".");
    [Export] public ViewFocusDirection Direction {get;set;}=ViewFocusDirection.Automatic;
    [Export] public Control.FocusModeEnum Mode {get;set;}=Control.FocusModeEnum.All;
    [Export] public NodePath Left {get;set;}=new("");
    [Export] public NodePath Top {get;set;}=new("");
    [Export] public NodePath Right {get;set;}=new("");
    [Export] public NodePath Bottom {get;set;}=new("");
    [Export] public NodePath Next {get;set;}=new("");
    [Export] public NodePath Previous {get;set;}=new("");
}
