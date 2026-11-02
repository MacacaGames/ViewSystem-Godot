using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;
public enum ViewKind { Page, State, Overlay, Element }
[Tool, GlobalClass]
public partial class ViewEntry:Resource
{
    [Export] public string Id {get;set;}="";
    [Export(PropertyHint.File,"*.tscn,*.scn")] public string ScenePath {get;set;}="";
    [Export] public PackedScene Scene {get;set;}=null!;
    [Export] public ViewKind Kind {get;set;}
    [Export] public bool UseGlobalSafeArea {get;set;}=true;
    [Export] public ViewSafeAreaSettings SafeArea {get;set;}=new();
    [Export] public bool Modal {get;set;}
    [Export] public bool NavigationEnabled {get;set;}
    [Export] public string FirstSelectedState {get;set;}="";
    [Export] public global::Godot.Collections.Array<ViewStateNavigationOverride> StateNavigation {get;set;}=new();
    [Export] public NodePath FirstSelected {get;set;}=new("");
    [Export] public global::Godot.Collections.Array<ViewFocusLink> FocusLinks {get;set;}=new();
    [Export] public string State {get;set;}="";
    [Export] public ViewPageTransitionTiming TransitionTiming {get;set;}=ViewPageTransitionTiming.WithPrevious;
    [Export] public double CustomTransitionWait {get;set;}=.5;
    [Export] public int TargetFrameRate {get;set;}=-1;
    [Export] public int SortOrder {get;set;}
    [Export] public global::Godot.Collections.Array<ViewElementPlacement> Elements { get; set; } = new();
    [Export] public global::Godot.Collections.Array<ViewPropertyOverride> Overrides { get; set; } = new();
    [Export] public global::Godot.Collections.Array<ViewEventBinding> Events { get; set; } = new();
    [Export] public ViewModelData PreviewModel { get; set; } = new();
    [Export] public ViewElementPolicy? PoolPolicy {get;set;}
    [Export] public ViewElementAnimation? ShowTransition {get;set;}
    [Export] public ViewElementAnimation? HideTransition {get;set;}
    [Export] public NodePath AnimationPlayerPath { get; set; } = new("AnimationPlayer");
    [Export] public string ShowAnimation { get; set; } = "";
    [Export] public string HideAnimation { get; set; } = "";
}
