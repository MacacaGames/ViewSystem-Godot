using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

/// <summary>Typed access to a native Resource metadata schema. The carrier has
/// no C# script instance, so its Inspector/serialization survives assembly reload.</summary>
public sealed class ViewElementLayout
{
    public Resource Data { get; }
    public ViewElementLayout(Resource data)
    {
        Data=data;
        if(Data.HasMeta("SchemaVersion") && Data.GetMeta("SchemaVersion").AsInt32()!=1)
            throw new NotSupportedException("Unsupported native placement layout schema.");
        Data.SetMeta("SchemaVersion",1);
        if(!Data.HasMeta("AnchorLeft"))Data.SetMeta("AnchorLeft",0f);
        if(!Data.HasMeta("AnchorTop"))Data.SetMeta("AnchorTop",0f);
        if(!Data.HasMeta("AnchorRight"))Data.SetMeta("AnchorRight",0f);
        if(!Data.HasMeta("AnchorBottom"))Data.SetMeta("AnchorBottom",0f);
        if(!Data.HasMeta("OffsetLeft"))Data.SetMeta("OffsetLeft",0f);
        if(!Data.HasMeta("OffsetTop"))Data.SetMeta("OffsetTop",0f);
        if(!Data.HasMeta("OffsetRight"))Data.SetMeta("OffsetRight",0f);
        if(!Data.HasMeta("OffsetBottom"))Data.SetMeta("OffsetBottom",0f);
        if(!Data.HasMeta("UseNormalizedPivot"))Data.SetMeta("UseNormalizedPivot",false);
        if(!Data.HasMeta("NormalizedPivot"))Data.SetMeta("NormalizedPivot",new Vector2(.5f,.5f));
        if(!Data.HasMeta("PivotOffset"))Data.SetMeta("PivotOffset",Vector2.Zero);
        if(!Data.HasMeta("Scale"))Data.SetMeta("Scale",Vector2.One);
        if(!Data.HasMeta("Rotation"))Data.SetMeta("Rotation",0f);
        if(!Data.HasMeta("SortOrder"))Data.SetMeta("SortOrder",0);
    }
    public Variant Get(string name)=>Data.GetMeta(name);
    public float AnchorLeft { get=>Data.GetMeta("AnchorLeft").AsSingle(); set=>Data.SetMeta("AnchorLeft",value); }
    public float AnchorTop { get=>Data.GetMeta("AnchorTop").AsSingle(); set=>Data.SetMeta("AnchorTop",value); }
    public float AnchorRight { get=>Data.GetMeta("AnchorRight").AsSingle(); set=>Data.SetMeta("AnchorRight",value); }
    public float AnchorBottom { get=>Data.GetMeta("AnchorBottom").AsSingle(); set=>Data.SetMeta("AnchorBottom",value); }
    public float OffsetLeft { get=>Data.GetMeta("OffsetLeft").AsSingle(); set=>Data.SetMeta("OffsetLeft",value); }
    public float OffsetTop { get=>Data.GetMeta("OffsetTop").AsSingle(); set=>Data.SetMeta("OffsetTop",value); }
    public float OffsetRight { get=>Data.GetMeta("OffsetRight").AsSingle(); set=>Data.SetMeta("OffsetRight",value); }
    public float OffsetBottom { get=>Data.GetMeta("OffsetBottom").AsSingle(); set=>Data.SetMeta("OffsetBottom",value); }
    public bool UseNormalizedPivot { get=>Data.GetMeta("UseNormalizedPivot").AsBool(); set=>Data.SetMeta("UseNormalizedPivot",value); }
    public Vector2 NormalizedPivot { get=>Data.GetMeta("NormalizedPivot").AsVector2(); set=>Data.SetMeta("NormalizedPivot",value); }
    public Vector2 PivotOffset { get=>Data.GetMeta("PivotOffset").AsVector2(); set=>Data.SetMeta("PivotOffset",value); }
    public Vector2 Scale { get=>Data.GetMeta("Scale").AsVector2(); set=>Data.SetMeta("Scale",value); }
    public float Rotation { get=>Data.GetMeta("Rotation").AsSingle(); set=>Data.SetMeta("Rotation",value); }
    public int SortOrder { get=>Data.GetMeta("SortOrder").AsInt32(); set=>Data.SetMeta("SortOrder",value); }
    public void Apply(Control node)
    {
        if (new[]{AnchorLeft,AnchorTop,AnchorRight,AnchorBottom,OffsetLeft,OffsetTop,OffsetRight,OffsetBottom,PivotOffset.X,PivotOffset.Y,Scale.X,Scale.Y,NormalizedPivot.X,NormalizedPivot.Y,Rotation}.Any(value=>!float.IsFinite(value)))
            throw new InvalidOperationException("Placement geometry must contain finite values.");
        if (SortOrder < RenderingServer.CanvasItemZMin || SortOrder > RenderingServer.CanvasItemZMax)
            throw new InvalidOperationException("Placement SortOrder is outside Godot's CanvasItem ZIndex range.");
        if (node.GetParent() is Container) throw new InvalidOperationException("A Container owns this placement's geometry; remove its explicit Layout.");
        node.AnchorLeft=AnchorLeft;node.AnchorTop=AnchorTop;node.AnchorRight=AnchorRight;node.AnchorBottom=AnchorBottom;
        node.OffsetLeft=OffsetLeft;node.OffsetTop=OffsetTop;node.OffsetRight=OffsetRight;node.OffsetBottom=OffsetBottom;
        ApplyPivot(node);node.Scale=Scale;node.Rotation=Rotation;node.ZIndex=SortOrder;
    }
    public void ApplyPivot(Control node) => node.PivotOffset=UseNormalizedPivot ? node.Size*NormalizedPivot : PivotOffset;
}
