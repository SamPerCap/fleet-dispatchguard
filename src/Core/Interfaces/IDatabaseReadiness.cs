namespace DispatchGuard.Core.Interfaces;

public interface IDatabaseReadiness
{
    /// <summary>Answers within seconds, whatever the state of the database.</summary>
    Task<bool> IsReadyAsync();
}
