using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

public enum ViewValueKind { Text, Integer, Number, Boolean, Color, Vector2, Resource, Vector3, Vector2I, Vector3I, Native }
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
    [Export] public string ModelExpression {get;set;}="";
    public bool IsModelBinding=>!string.IsNullOrWhiteSpace(ModelKey)||!string.IsNullOrWhiteSpace(ModelExpression);
    [Export] public string ModelKey { get; set; } = "";
    [Export] public ViewInjectScope Scope { get; set; } = ViewInjectScope.PageFirst;
    [Export] public ViewValueKind Kind { get; set; }
    [Export] public string Text { get; set; } = "";
    [Export] public long Integer { get; set; }
    [Export] public double Number { get; set; }
    [Export] public bool Boolean { get; set; }
    [Export] public Color Color { get; set; } = Colors.White;
    [Export] public Vector2 Vector { get; set; }
    [Export] public Vector3 Vector3 {get;set;}
    [Export] public Vector2I Vector2I {get;set;}
    [Export] public Vector3I Vector3I {get;set;}
    [Export] public Resource? Resource { get; set; }
    private Variant nativeValue;
    private readonly HashSet<Resource> nativeResources=new();
    internal IEnumerable<Resource> RetainedNativeResources {get{RetainNative(nativeValue,0);return nativeResources;}}
    [Export] public Variant NativeValue {get=>nativeValue;set{nativeValue=value;RetainNative(value,0);}}
    private void RetainNative(Variant value,int depth)
    {
        if(depth>32)return;
        if(value.VariantType==Variant.Type.Object&&value.AsGodotObject() is Resource resource)nativeResources.Add(resource);
        else if(value.VariantType==Variant.Type.Array)foreach(var item in value.AsGodotArray())RetainNative(item,depth+1);
        else if(value.VariantType==Variant.Type.Dictionary)foreach(var item in value.AsGodotDictionary().Values)RetainNative(item,depth+1);
    }

    public Variant Literal => Kind switch
    {
        ViewValueKind.Text => Text,
        ViewValueKind.Integer => Integer,
        ViewValueKind.Number => Number,
        ViewValueKind.Boolean => Boolean,
        ViewValueKind.Color => Color,
        ViewValueKind.Vector2 => Vector,
        ViewValueKind.Resource => Variant.From(Resource),
        ViewValueKind.Vector3 => Vector3,ViewValueKind.Vector2I=>Vector2I,ViewValueKind.Vector3I=>Vector3I,
        ViewValueKind.Native=>NativeValue,
        _ => throw new ArgumentOutOfRangeException(nameof(Kind))
    };
    public Variant Resolve(ViewContext context)
    {
        if(!string.IsNullOrWhiteSpace(ModelExpression))
        {if(!string.IsNullOrWhiteSpace(ModelKey))throw new InvalidOperationException("Choose ModelKey or ModelExpression, not both.");return context.ResolveModelExpression(ModelExpression);}
        return string.IsNullOrWhiteSpace(ModelKey)?Literal:context.Value(ModelKey,Scope);
    }
}
