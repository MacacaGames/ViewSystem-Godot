using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

/// <summary>A stable use of a reusable scene. The parent scene owns its layout.</summary>
[Tool, GlobalClass]
public partial class ViewElementPlacement : Resource
{
    [Export] public string Id { get; set; } = "Element";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public string SourceAsset { get; set; } = "";
    [Export] public string SourceGuid { get; set; } = "";
    [Export] public string SourceLocalFileId { get; set; } = "";
    [Export] public string SourcePageAsset { get; set; } = "";
    [Export] public string SourceItemId { get; set; } = "";
    [Export] public Resource? LayoutData { get; set; }
    private ViewElementLayout? layout;
    public ViewElementLayout? Layout => LayoutData==null?null:
        layout!=null && ReferenceEquals(layout.Data,LayoutData)?layout:layout=new ViewElementLayout(LayoutData);
    [Export] public global::Godot.Collections.Array<ViewBreakpointLayout> Breakpoints { get; set; } = new();
    public ViewBreakpointLayout? ResolveBreakpoint(ViewContext context) => Breakpoints.FirstOrDefault(b => b != null && context.ActiveBreakpoints?.Contains(b.Name) == true);
    public ViewElementLayout? ResolveLayout(ViewContext context) => ResolveBreakpoint(context) is {} b ? b.Layout : Layout;
    public NodePath ResolveParent(ViewContext context) => ResolveBreakpoint(context) is { OverrideParent:true } b ? b.ParentPath : ParentPath;
    [Export] public string[] ExcludeFeatures {get;set;}=System.Array.Empty<string>();
    [Export] public bool ManualOnly {get;set;}
    [Export] public bool InstantPosition {get;set;}
    [Export] public double TransferDuration {get;set;}=.4;
    [Export] public Tween.TransitionType TransferTransition {get;set;}=Tween.TransitionType.Quad;
    [Export] public Tween.EaseType TransferEase {get;set;}=Tween.EaseType.Out;
    [Export] public double DelayIn {get;set;}
    [Export] public double DelayOut {get;set;}
    [Export] public ViewElementAnimation? ShowTransition {get;set;}
    [Export] public ViewElementAnimation? HideTransition {get;set;}
    [Export] public ViewElementPolicy? PoolPolicy {get;set;}
    [Export] public int SiblingIndex { get; set; } = -1;
    [Export] public string NodeName { get; set; } = "";
    [Export(PropertyHint.File,"*.tscn,*.scn")] public string ScenePath {get;set;}="";
    [Export] public PackedScene Scene { get; set; } = null!;
    [Export] public NodePath ParentPath { get; set; } = new(".");
    [Export] public global::Godot.Collections.Array<ViewPropertyOverride> Overrides { get; set; } = new();
    [Export] public global::Godot.Collections.Array<ViewEventBinding> Events { get; set; } = new();
}
