using Godot;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

/// <summary>Serializable preview data. Runtime must explicitly opt into fixtures.</summary>
[Tool, GlobalClass]
public partial class ViewModelData : Resource
{
    [Export] public global::Godot.Collections.Dictionary<string, Variant> Values { get; set; } = new();
}
