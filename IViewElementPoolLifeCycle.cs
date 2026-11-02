namespace MacacaGames.ViewSystem;
/// <summary>Runs after leave, immediately before the scene is parked or destroyed.</summary>
public interface IViewElementPoolLifeCycle
{
    void OnBeforeReturnToPool();
}
