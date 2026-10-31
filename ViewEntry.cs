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
    [Export] public PackedScene Scene {get;set;}=null!;
    [Export] public ViewKind Kind {get;set;}
    [Export] public string State {get;set;}="";
    [Export] public int SortOrder {get;set;}
    [Export] public global::Godot.Collections.Array<ViewElementPlacement> Elements { get; set; } = new();
    [Export] public global::Godot.Collections.Array<ViewPropertyOverride> Overrides { get; set; } = new();
    [Export] public global::Godot.Collections.Array<ViewEventBinding> Events { get; set; } = new();
    [Export] public ViewModelData PreviewModel { get; set; } = new();
    [Export] public NodePath AnimationPlayerPath { get; set; } = new("AnimationPlayer");
    [Export] public string ShowAnimation { get; set; } = "";
    [Export] public string HideAnimation { get; set; } = "";
}
