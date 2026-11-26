using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Control;

/// <summary>
/// The calendar day in Europe/Madrid, not the UTC day: at 00:30 in Madrid on the 5th (23:30Z on the 4th) the operational day is
/// the 5th (SPEC 0057 R1).
/// </summary>
public static class OperationalDay
{
    public static readonly TimeZoneInfo TimeZone = DisplayFormat.MadridTimeZone;

    /// <summary>[From, To) in UTC.</summary>
    public static (DateTime From, DateTime To) Containing(DateTime utcInstant)
    {
        var date = DateOf(utcInstant);
        return (StartOf(date), StartOf(date.AddDays(1)));
    }

    /// <summary>Madrid date: what "the same day" means anywhere (SPEC 0071 R4).</summary>
    public static DateOnly DateOf(DateTime utcInstant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc), TimeZone));

    /// <summary>UTC instant of the Madrid midnight.</summary>
    public static DateTime StartOf(DateOnly date) => ToUtc(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>The day before the one that contains <paramref name="utcInstant"/>.</summary>
    public static (DateTime From, DateTime To) Before(DateTime utcInstant) =>
        Containing(Containing(utcInstant).From.AddMinutes(-1));

    // Midnight is never skipped nor repeated in Madrid (DST changes at 02:00/03:00).
    private static DateTime ToUtc(DateTime localMidnight) =>
        DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localMidnight, DateTimeKind.Unspecified), TimeZone),
            DateTimeKind.Utc);
}
