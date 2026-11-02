using Godot;
using System;
namespace MacacaGames.ViewSystem;

[Tool,GlobalClass]
public partial class ViewSafeAreaSettings:Resource
{
    [Export] public bool Enabled {get;set;}
    [Export] public bool Left {get;set;}=true;
    [Export] public bool Top {get;set;}=true;
    [Export] public bool Right {get;set;}=true;
    [Export] public bool Bottom {get;set;}=true;
    [Export] public bool FlipPadding {get;set;}
    public Vector4 Insets(Rect2 normalized)
    {
        var n=normalized.Intersection(new Rect2(Vector2.Zero,Vector2.One));
        if(!float.IsFinite(n.Position.X)||!float.IsFinite(n.Position.Y)||!float.IsFinite(n.Size.X)||!float.IsFinite(n.Size.Y)||!n.HasArea())
            throw new InvalidOperationException("Safe area must be a finite nonempty normalized rectangle.");
        var result=new Vector4(Left?n.Position.X:0,Top?n.Position.Y:0,Right?1-n.End.X:0,Bottom?1-n.End.Y:0);
        return FlipPadding?new Vector4(result.Z,result.W,result.X,result.Y):result;
    }
}
