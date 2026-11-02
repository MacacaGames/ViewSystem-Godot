using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

/// <summary>A named action connected to a native signal; arguments may be forwarded to the host.</summary>
[Tool, GlobalClass]
public partial class ViewEventBinding : Resource
{
    [Export] public bool Enabled {get;set;}=true;
    [Export] public bool NativeHandler {get;set;}
    [Export] public bool HandlerRelativeToElement {get;set;}
    [Export] public string SourceScript { get; set; } = "";
    [Export] public string SourceMethod { get; set; } = "";
    [Export] public string SourceTransformPath { get; set; } = "";
    [Export] public NodePath Target { get; set; } = new(".");
    [Export] public bool ForwardArguments { get; set; }
    [Export] public string Signal { get; set; } = "pressed";
    [Export] public string Handler {get;set;}="";
    [Export] public string Action { get; set; } = "";
}
