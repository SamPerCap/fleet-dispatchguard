namespace DispatchGuard.Core.Interfaces;

/// <summary>
/// Lives in Core so that Infrastructure can drop the cache whenever trips are saved or wiped, without depending on Application.
/// </summary>
public interface ITripDurationCache
{
    void Invalidate();
}
