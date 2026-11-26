namespace DispatchGuard.Core.Interfaces;

/// <summary>Lives in Core because Infrastructure implements it and does not depend on Application.</summary>
public interface IDemoDataReset
{
    /// <summary>The right to rebuild the demo, until it is disposed; null if someone else has it, in any instance of the API.</summary>
    Task<IAsyncDisposable?> TryReserveAsync(CancellationToken ct = default);

    Task ResetAsync(CancellationToken ct = default);
}
