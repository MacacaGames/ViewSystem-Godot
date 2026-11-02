using Godot;
namespace MacacaGames.ViewSystem;
/// <summary>Portable ScriptableObject data. Keys preserve Unity serialized property paths.</summary>
[Tool,GlobalClass]
public partial class UnitySerializedResource:UnitySourceAsset
{
    [Export] public Godot.Collections.Dictionary<string,Variant> Fields {get;set;}=new();
}
