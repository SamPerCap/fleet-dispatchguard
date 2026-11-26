using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Auditing;

/// <summary>
/// Each insert checks its own room: the ceilings hold with any number of instances. The one of the whole table counts ids, which
/// costs nothing and only ever errs by filling up too early; what expires makes room again.
/// </summary>
public sealed class VisitLog(DispatchGuardDbContext db) : IVisitLog
{
    private const int EventsForEachSession = 2_000;
    private const int EventsForAll = 200_000;
    private const int TracesForEachSession = 500;
    private const int TracesForAll = 40_000;

    public async Task<bool> RecordAsync(
        string sessionId, string? reference, IReadOnlyCollection<VisitEventEntry> events, CancellationToken cancellationToken = default)
    {
        var isNew = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "Visits" ("SessionId", "StartedAt", "Reference") VALUES ({sessionId}, now(), {reference})
            ON CONFLICT ("SessionId") DO NOTHING
            """,
            cancellationToken) == 1;
        if (events.Count == 0)
        {
            return isNew;
        }

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "VisitEvents" ("SessionId", "At", "TraceId", "Kind", "Name", "Status", "DurationMs")
            SELECT {sessionId}, now() - e.age, e.trace, e.kind, e.name, e.status, e.duration
            FROM unnest({events.Select(e => e.Age).ToArray()}, {events.Select(e => e.TraceId).ToArray()},
                {events.Select(e => e.Kind.ToString()).ToArray()}, {events.Select(e => e.Name).ToArray()},
                {events.Select(e => e.Status).ToArray()}, {events.Select(e => e.DurationMs).ToArray()})
                WITH ORDINALITY AS e(age, trace, kind, name, status, duration, position)
            WHERE (SELECT count(*) FROM "VisitEvents" WHERE "SessionId" = {sessionId}) < {EventsForEachSession}
                AND (SELECT coalesce(max("Id") - min("Id"), 0) FROM "VisitEvents") < {EventsForAll}
            ORDER BY e.position
            """,
            cancellationToken);
        return isNew;
    }

    public Task RecordAsync(RequestTrace trace, CancellationToken cancellationToken = default) => db.Database.ExecuteSqlAsync(
        $"""
        INSERT INTO "RequestTraces"
            ("SessionId", "At", "TraceId", "Method", "Path", "Status", "DurationMs", "Queries", "QueriesMs", "Detail")
        SELECT {trace.SessionId}, now() - {trace.DurationMs} * interval '1 millisecond', {trace.TraceId}, {trace.Method}, {trace.Path},
            {trace.Status}, {trace.DurationMs}, {trace.Queries}, {trace.QueriesMs}, {trace.Detail}
        WHERE (SELECT count(*) FROM "RequestTraces" WHERE "SessionId" = {trace.SessionId}) < {TracesForEachSession}
            AND (SELECT coalesce(max("Id") - min("Id"), 0) FROM "RequestTraces") < {TracesForAll}
        """,
        cancellationToken);

    public Task DeleteExpiredAsync(CancellationToken cancellationToken = default) =>
        db.Database.ExecuteSqlAsync($"SELECT public.delete_expired_records()", cancellationToken);
}
