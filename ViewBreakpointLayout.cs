using Godot;
namespace MacacaGames.ViewSystem;

/// <summary>First enabled name wins, matching Unity's authored breakpoint order.</summary>
[Tool, GlobalClass]
public partial class ViewBreakpointLayout : Resource
{
    [Export] public string Name { get; set; } = "";
    [Export] public Resource? LayoutData { get; set; }
    [Export] public bool OverrideParent { get; set; }
    [Export] public NodePath ParentPath { get; set; } = new(".");
    public ViewElementLayout? Layout => LayoutData == null ? null : new(LayoutData);
}
