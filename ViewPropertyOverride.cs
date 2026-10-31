using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

public enum ViewValueKind { Text, Integer, Number, Boolean, Color, Vector2, Resource }
public enum ViewInjectScope { PageFirst, PageOnly, SharedFirst, SharedOnly }

/// <summary>A literal or model-backed Godot property. Paths are relative to its element.</summary>
[Tool, GlobalClass]
public partial class ViewPropertyOverride : Resource
{
    [Export] public string SourceProperty { get; set; } = "";
    [Export] public string SourceComponent { get; set; } = "";
    [Export] public string SourceTransformPath { get; set; } = "";
    [Export] public string SourceValueJson { get; set; } = "";
    [Export] public NodePath Target { get; set; } = new(".");
    [Export] public string Property { get; set; } = "text";
    [Export] public string ModelKey { get; set; } = "";
    [Export] public ViewInjectScope Scope { get; set; } = ViewInjectScope.PageFirst;
    [Export] public ViewValueKind Kind { get; set; }
    [Export] public string Text { get; set; } = "";
    [Export] public long Integer { get; set; }
    [Export] public double Number { get; set; }
    [Export] public bool Boolean { get; set; }
    [Export] public Color Color { get; set; } = Colors.White;
    [Export] public Vector2 Vector { get; set; }
    [Export] public Resource? Resource { get; set; }

    public Variant Literal => Kind switch
    {
        ViewValueKind.Text => Text,
        ViewValueKind.Integer => Integer,
        ViewValueKind.Number => Number,
        ViewValueKind.Boolean => Boolean,
        ViewValueKind.Color => Color,
        ViewValueKind.Vector2 => Vector,
        ViewValueKind.Resource => Variant.From(Resource),
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
    public Variant Resolve(ViewContext context) => string.IsNullOrWhiteSpace(ModelKey)
        ? Literal : context.Value(ModelKey, Scope);
}
