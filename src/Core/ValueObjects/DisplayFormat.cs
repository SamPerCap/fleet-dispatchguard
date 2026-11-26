using System.Globalization;

namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// How texts for people show instants and decimals, whatever the server's zone and culture. Rules work in UTC instants
/// (SPEC 0057 R1); the only one that counts in Madrid time as well is the week of Art. 6, through <see cref="DrivingWeek"/>
/// (SPEC 0064).
/// </summary>
public static class DisplayFormat
{
    public static readonly TimeZoneInfo MadridTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    public static string Madrid(DateTime utc, string format, CultureInfo culture) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), MadridTimeZone).ToString(format, culture);
}
