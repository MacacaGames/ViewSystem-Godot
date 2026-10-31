using Godot;
namespace MacacaGames.ViewSystem;

/// <summary>Authoring contract for an application-owned action dispatcher, not a service locator.</summary>
[Tool, GlobalClass]
public partial class ViewActionDefinition : Resource
{
    [Export] public string Id { get; set; } = "";
    [Export] public Script? HandlerScript { get; set; }
    [Export] public string HandlerMethod { get; set; } = "";
    public bool HasHandlerMethod() => HandlerScript != null && HandlerMethod.Length > 0 &&
        System.Linq.Enumerable.Any(HandlerScript.GetScriptMethodList(), method => method["name"].AsString() == HandlerMethod);
}
