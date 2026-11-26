namespace DispatchGuard.Core.Interfaces;

/// <summary>Who and what a change comes from (SPEC 0085 R1). Everything is null outside a request.</summary>
public interface IAuditOrigin
{
    string? SessionId { get; }

    string? TraceId { get; }

    string? DeclaredName { get; }

    /// <summary>The load of the demo: the reset that precedes it already leaves its event.</summary>
    bool IsDemoLoad { get; }
}
