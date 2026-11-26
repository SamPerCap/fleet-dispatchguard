namespace DispatchGuard.API.Http;

public static class UtcInstantExtensions
{
    /// <summary>An instant without a zone already means UTC for this API (SPEC 0067): it is labelled, never shifted.</summary>
    public static DateTime AsUtc(this DateTime instant) => instant.Kind == DateTimeKind.Local
        ? instant.ToUniversalTime()
        : DateTime.SpecifyKind(instant, DateTimeKind.Utc);

    public static DateTime? AsUtc(this DateTime? instant) => instant?.AsUtc();
}
