using DispatchGuard.Core.Auditing;

namespace DispatchGuard.Core.Interfaces;

/// <param name="Age">How long ago it happened: the clock of the browser is not trusted.</param>
public sealed record VisitEventEntry(TimeSpan Age, VisitEventKind Kind, string Name, string? TraceId = null, int? Status = null, int? DurationMs = null);

/// <summary>What is recorded of a visit besides its changes. Full, it stops adding instead of failing.</summary>
public interface IVisitLog
{
    /// <returns>Whether this is the first news of the session, in any instance of the API.</returns>
    Task<bool> RecordAsync(string sessionId, string? reference, IReadOnlyCollection<VisitEventEntry> events, CancellationToken cancellationToken = default);

    Task RecordAsync(RequestTrace trace, CancellationToken cancellationToken = default);

    /// <summary>Deletes what is older than the time it may be kept, which only the database knows (SPEC 0085 R8).</summary>
    Task DeleteExpiredAsync(CancellationToken cancellationToken = default);
}

public sealed record VisitSummary(string SessionId, DateTime StartedAt, DateTime LastAt, string? Reference, int Events, int Changes);

/// <summary>One line of the timeline: something of the web, a request or a change.</summary>
/// <param name="RequestId">Its trace can be asked for with it; null if it is not a request.</param>
/// <param name="Changes">JSON of <see cref="AuditEvent.Changes"/>; null if it is not a change.</param>
public sealed record TimelineEntry(
    DateTime At, string Kind, string Name, string? TraceId, int? Status = null, int? DurationMs = null, long? RequestId = null,
    int? EntityId = null, string? Changes = null, string? DeclaredName = null);

/// <summary>In time order.</summary>
public sealed record VisitTimeline(string SessionId, DateTime? StartedAt, string? Reference, IReadOnlyList<TimelineEntry> Entries);

public interface IVisitReader
{
    /// <summary>The last ones, the newest first.</summary>
    Task<IReadOnlyList<VisitSummary>> LastAsync(CancellationToken cancellationToken = default);

    /// <summary>Without entries if nothing is kept of it.</summary>
    Task<VisitTimeline> TimelineAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<RequestTrace?> RequestAsync(long id, CancellationToken cancellationToken = default);
}
