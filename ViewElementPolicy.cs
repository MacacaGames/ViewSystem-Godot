using Godot;
namespace MacacaGames.ViewSystem;
public enum ViewElementRecoveryPolicy { KeepForever,KeepN,DestroyOnRecovery }
public interface IViewElementSingleton { }
[Tool,GlobalClass]
public partial class ViewElementPolicy:Resource
{
    [Export] public bool Unique {get;set;}
    [Export] public ViewElementRecoveryPolicy Recovery {get;set;}=ViewElementRecoveryPolicy.KeepForever;
    [Export(PropertyHint.Range,"0,10000,1")] public int KeepCount {get;set;}=1;
}
