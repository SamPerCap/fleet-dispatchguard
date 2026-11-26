using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DispatchGuard.Infrastructure.Persistence;

/// <summary>
/// <c>timestamptz</c> only admits UTC. An instant without kind already means UTC here (SPEC 0067). It is read without kind, as it
/// always was, so that the dates of the answers keep their text (SPEC 0092).
/// </summary>
internal sealed class UtcInstantConverter() : ValueConverter<DateTime, DateTime>(
    instant => ToUtc(instant),
    stored => DateTime.SpecifyKind(stored, DateTimeKind.Unspecified))
{
    private static DateTime ToUtc(DateTime instant) => instant.Kind == DateTimeKind.Local
        ? instant.ToUniversalTime()
        : DateTime.SpecifyKind(instant, DateTimeKind.Utc);
}
