using Godot;
using System;
namespace MacacaGames.ViewSystem;

public sealed record ViewNavigationOptions
{
    public ViewNavigationMode? Mode {get;init;}
    public bool WaitPreviousPageFinish {get;init;}
    public bool IgnoreClickProtection {get;init;}
    public bool IgnoreTransition {get;init;}
    public bool? IgnoreTimeScale {get;init;}=true;
    public bool ReusePage {get;init;}=true;
    public bool ReuseOverlay {get;init;}=true;
    public bool ReplayWhileSamePage {get;init;}
    public Control? CustomRoot {get;init;}
    public int? SortOrder {get;init;}
    public Action? OnStart {get;init;}
    public Action<ViewInstance>? OnChanged {get;init;}
}
