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
[Flags]
public enum ViewLayoutFlags
{
    None=0, SizeDelta=1, AnchoredPosition=2, AnchorMax=4, AnchorMin=8,
    LocalEulerAngles=16, LocalScale=32, Pivot=64, SortOrder=128, All=255
}

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
    public ViewLayoutFlags Flags { get=>Data.HasMeta("Flags")?(ViewLayoutFlags)Data.GetMeta("Flags").AsInt32():ViewLayoutFlags.All; set=>Data.SetMeta("Flags",(int)value); }
    public static ViewElementLayout Capture(Control node) => new(new Resource())
    { AnchorLeft=node.AnchorLeft, AnchorTop=node.AnchorTop, AnchorRight=node.AnchorRight, AnchorBottom=node.AnchorBottom,
      OffsetLeft=node.OffsetLeft, OffsetTop=node.OffsetTop, OffsetRight=node.OffsetRight, OffsetBottom=node.OffsetBottom,
      PivotOffset=node.PivotOffset, Scale=node.Scale, Rotation=node.Rotation, SortOrder=node.ZIndex };
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
        bool Has(ViewLayoutFlags flag) => (Flags & flag) != 0;
        var oldPivot = new Vector2(node.Size.X == 0 ? 0 : node.PivotOffset.X/node.Size.X, node.Size.Y == 0 ? 0 : node.PivotOffset.Y/node.Size.Y);
        var sourceSize = new Vector2(OffsetRight-OffsetLeft,OffsetBottom-OffsetTop);
        var sourcePivot = UseNormalizedPivot ? NormalizedPivot : new Vector2(node.Size.X == 0 ? 0 : PivotOffset.X/node.Size.X,node.Size.Y == 0 ? 0 : PivotOffset.Y/node.Size.Y);
        var delta = Has(ViewLayoutFlags.SizeDelta) ? sourceSize : new Vector2(node.OffsetRight-node.OffsetLeft,node.OffsetBottom-node.OffsetTop);
        var position = Has(ViewLayoutFlags.AnchoredPosition) ? new Vector2(OffsetLeft,OffsetTop)+sourceSize*sourcePivot : new Vector2(node.OffsetLeft,node.OffsetTop)+new Vector2(node.OffsetRight-node.OffsetLeft,node.OffsetBottom-node.OffsetTop)*oldPivot;
        var pivot = Has(ViewLayoutFlags.Pivot) ? sourcePivot : oldPivot;
        if(Has(ViewLayoutFlags.AnchorMin)){node.AnchorLeft=AnchorLeft;node.AnchorBottom=AnchorBottom;}
        if(Has(ViewLayoutFlags.AnchorMax)){node.AnchorRight=AnchorRight;node.AnchorTop=AnchorTop;}
        if(Has(ViewLayoutFlags.SizeDelta)||Has(ViewLayoutFlags.AnchoredPosition)||Has(ViewLayoutFlags.Pivot))
        { var a=position-delta*pivot;var b=a+delta;node.OffsetLeft=a.X;node.OffsetTop=a.Y;node.OffsetRight=b.X;node.OffsetBottom=b.Y; }
        ApplyPivot(node);
        if(Has(ViewLayoutFlags.LocalScale))node.Scale=Scale;
        if(Has(ViewLayoutFlags.LocalEulerAngles))node.Rotation=Rotation;
        if(Has(ViewLayoutFlags.SortOrder))node.ZIndex=SortOrder;
    }
    public void ApplyPivot(Control node) { if((Flags & ViewLayoutFlags.Pivot)!=0)node.PivotOffset=UseNormalizedPivot ? node.Size*NormalizedPivot : PivotOffset; }
}
