using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DispatchGuard.Infrastructure.Auditing;

/// <summary>No tracking: nothing here is saved.</summary>
public sealed class VisitReader(DispatchGuardDbContext db) : IVisitReader
{
    private const int MaxVisits = 100;

    /// <summary>Of each kind: the last ones.</summary>
    private const int MaxEntries = 500;

    public async Task<IReadOnlyList<VisitSummary>> LastAsync(CancellationToken cancellationToken = default) =>
        await db.Visits.AsNoTracking()
            .OrderByDescending(visit => visit.StartedAt)
            .Take(MaxVisits)
            .Select(visit => new VisitSummary(
                visit.SessionId,
                visit.StartedAt,
                db.VisitEvents.Where(e => e.SessionId == visit.SessionId).Max(e => (DateTime?)e.At) ?? visit.StartedAt,
                visit.Reference ?? db.VisitEvents
                    .Where(e => e.SessionId == visit.SessionId && e.Kind == VisitEventKind.Arrived)
                    .OrderBy(e => e.Id).Select(e => e.Name).FirstOrDefault(),
                db.VisitEvents.Count(e => e.SessionId == visit.SessionId),
                db.AuditEvents.Count(e => e.SessionId == visit.SessionId)))
            .ToListAsync(cancellationToken);

    public async Task<VisitTimeline> TimelineAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var visit = await db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.SessionId == sessionId, cancellationToken);
        var events = await db.VisitEvents.AsNoTracking().Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.Id).Take(MaxEntries).ToListAsync(cancellationToken);
        var requests = await db.RequestTraces.AsNoTracking().Where(t => t.SessionId == sessionId)
            .OrderByDescending(t => t.Id).Take(MaxEntries)
            .Select(t => new { t.Id, t.At, t.Method, t.Path, t.TraceId, t.Status, t.DurationMs })
            .ToListAsync(cancellationToken);
        var changes = await db.AuditEvents.AsNoTracking().Where(e => e.SessionId == sessionId)
            .OrderByDescending(e => e.Id).Take(MaxEntries).ToListAsync(cancellationToken);

        var entries = events
            .Select(e => new TimelineEntry(e.At, e.Kind.ToString(), e.Name, e.TraceId, e.Status, e.DurationMs))
            .Concat(requests.Select(t => new TimelineEntry(
                t.At, TimelineKinds.Request, $"{t.Method} {t.Path}", t.TraceId, t.Status, t.DurationMs, RequestId: t.Id)))
            .Concat(changes.OrderBy(e => e.Id).Select(e => new TimelineEntry(
                e.At, e.Action, e.Entity, e.TraceId, EntityId: e.EntityId, Changes: e.Changes, DeclaredName: e.DeclaredName)));
        return new VisitTimeline(sessionId, visit?.StartedAt, visit?.Reference, [.. entries.OrderBy(entry => entry.At)]);
    }

    public Task<RequestTrace?> RequestAsync(long id, CancellationToken cancellationToken = default) =>
        db.RequestTraces.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
}
