using Godot;
using System;
using System.Linq;
namespace MacacaGames.ViewSystem;

/// <summary>All coordinates use Godot screen coordinates, including negative monitor origins.</summary>
public sealed record ViewDisplaySnapshot(int WindowId,int Screen,Rect2 SafeScreenRect,Transform2D LocalToScreen,Vector2 LocalSize);
public static class ViewDisplayMetrics
{
    public static ViewDisplaySnapshot Capture(Control host)
    {
        var window=host.GetWindow();
        if(host.GetViewport()!=window)throw new InvalidOperationException("Offscreen SubViewport Safe Area requires DisplayMetricsProvider or UseSimulatedSafeArea.");
        // Build from the actual native window. In 4.7.2 CanvasItem's screen
        // transform can omit a secondary native window's origin.
        var transform=window.GetFinalTransform()*host.GetGlobalTransformWithCanvas();
        while(window.IsEmbedded()&&window.GetParent()?.GetWindow() is {} parent)
        {transform=parent.GetFinalTransform()*new Transform2D(0,(Vector2)window.Position)*transform;window=parent;}
        transform=new Transform2D(0,(Vector2)DisplayServer.WindowGetPosition(window.GetWindowId()))*transform;
        int screen=window.CurrentScreen;
        Rect2 safe=OS.HasFeature("android")||OS.HasFeature("ios")?DisplayServer.GetDisplaySafeArea():DisplayServer.ScreenGetUsableRect(screen);
        return new(window.GetWindowId(),screen,safe,transform,host.Size);
    }
    public static Rect2 Normalize(ViewDisplaySnapshot display)
    {
        var size=display.LocalSize;var safe=display.SafeScreenRect;var transform=display.LocalToScreen;
        if(size.X<=0||size.Y<=0||!size.IsFinite()||!safe.HasArea()||!transform.IsFinite()||Mathf.IsZeroApprox(transform.Determinant()))return new(0,0,1,1);
        var inv=transform.AffineInverse();
        var polygon=new[]{inv*safe.Position,inv*new Vector2(safe.End.X,safe.Position.Y),inv*safe.End,inv*new Vector2(safe.Position.X,safe.End.Y)};
        var clipped=Geometry2D.IntersectPolygons(polygon,new[]{Vector2.Zero,new Vector2(size.X,0),size,new Vector2(0,size.Y)});
        if(clipped.Count==0)return new(0,0,0,0);
        var points=clipped[0];var min=points.Aggregate((a,b)=>a.Min(b));var max=points.Aggregate((a,b)=>a.Max(b));
        // An inscribed rectangle also keeps rotated/skewed hosts inside the safe polygon.
        var center=points.Aggregate(Vector2.Zero,(sum,p)=>sum+p)/points.Length;
        var half=(max-min)*.5f;float factor=1;
        for(int i=0;i<points.Length;i++)
        {
            var edge=points[(i+1)%points.Length]-points[i];var normal=new Vector2(-edge.Y,edge.X);
            float distance=Mathf.Abs(normal.Dot(center-points[i]));float extent=Mathf.Abs(normal.X)*half.X+Mathf.Abs(normal.Y)*half.Y;
            if(extent>0)factor=Mathf.Min(factor,distance/extent);
        }
        half*=factor;
        return new((center-half)/size,half*2/size);
    }
}

public partial class ViewHost
{
    /// <summary>Optional platform/test adapter. Each host captures its own native window.</summary>
    public Func<Control,ViewDisplaySnapshot>? DisplayMetricsProvider {get;set;}
    private Rect2? lastSafeArea;
    public Rect2 NormalizedSafeArea=>GetNormalizedSafeArea();
    public void RefreshSafeArea()
    {
        if(!initialized)return;var safe=GetNormalizedSafeArea();
        if(lastSafeArea is {} prior&&prior.IsEqualApprox(safe))return;
        lastSafeArea=safe;
        foreach(var view in owned.Values.Concat(requestedViews).Distinct().ToArray())if(!view.IsDisposed&&view.IsCurrentOwner)view.UpdateSafeArea(safe);
    }
    public override void _Process(double delta)=>RefreshSafeArea();
}
