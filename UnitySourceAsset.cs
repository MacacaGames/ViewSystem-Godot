using Godot;
namespace MacacaGames.ViewSystem;
/// <summary>Lossless source metadata retained next to converted native resources.</summary>
[Tool,GlobalClass]
public partial class UnitySourceAsset:Resource
{
    [Export] public string SourceGuid {get;set;}="";
    [Export] public long SourceFileId {get;set;}
    [Export] public string SourcePath {get;set;}="";
    [Export] public string SourceType {get;set;}="";
    [Export(PropertyHint.MultilineText)] public string SourceJson {get;set;}="";
}
