namespace DispatchGuard.Core.ValueObjects;

/// <summary>The two clocks in which a week is counted. The Regulation names none, so both must be met (SPEC 0064 R1).</summary>
public enum WeekClock
{
    /// <summary>Official time in Spain (analogue record sheets, Regulation 165/2014 Art. 34.5.a).</summary>
    Madrid = 0,

    /// <summary>Time of the digital tachograph (Regulation 165/2014 Art. 2).</summary>
    Utc = 1
}

/// <summary>
/// Week of CE 561/2006 Art. 4(i): from 00:00 on Monday to 24:00 on Sunday (SPEC 0064 R1). Instants are UTC in and out;
/// a Madrid week lasts one hour less in March and one more in October.
/// </summary>
public static class DrivingWeek
{
    public static readonly IReadOnlyList<WeekClock> Clocks = [WeekClock.Madrid, WeekClock.Utc];

    /// <summary>Longest in either clock: 14 days plus the hour of the autumn change.</summary>
    public static readonly TimeSpan LongestTwoWeeks = TimeSpan.FromDays(14) + TimeSpan.FromHours(1);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<DateTime, DateTime> MadridStarts = new();

    /// <summary>Local date of the Monday; it identifies the week that contains <paramref name="utc"/> in that clock.</summary>
    public static DateTime MondayOf(DateTime utc, WeekClock clock)
    {
        var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var day = (clock == WeekClock.Utc ? instant : TimeZoneInfo.ConvertTimeFromUtc(instant, DisplayFormat.MadridTimeZone)).Date;
        return DateTime.SpecifyKind(day.AddDays(-(((int)day.DayOfWeek + 6) % 7)), DateTimeKind.Unspecified);
    }

    /// <summary>UTC instant. Monday 00:00 is never skipped nor repeated in Madrid, so the conversion is never ambiguous.</summary>
    public static DateTime StartUtc(DateTime monday, WeekClock clock) => clock == WeekClock.Utc
        ? DateTime.SpecifyKind(monday, DateTimeKind.Utc)
        : MadridStarts.GetOrAdd(monday, static m => TimeZoneInfo.ConvertTimeToUtc(m, DisplayFormat.MadridTimeZone));

    /// <summary>UTC instant in which the week that contains <paramref name="utc"/> starts.</summary>
    public static DateTime StartOf(DateTime utc, WeekClock clock) => StartUtc(MondayOf(utc, clock), clock);

    public static LocalizedText Name(WeekClock clock) => clock == WeekClock.Utc ? Texts.UtcClock() : Texts.MadridClock();
}
