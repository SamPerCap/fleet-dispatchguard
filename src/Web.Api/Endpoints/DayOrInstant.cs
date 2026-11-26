using System.Globalization;
using DispatchGuard.Application.Services.Control;

namespace DispatchGuard.API.Endpoints;

/// <summary>
/// Limit of a period in a query string (SPEC 0071 R4): a date (<c>yyyy-MM-dd</c>) is the Madrid midnight in which that day starts
/// (<see cref="OperationalDay.StartOf"/>); with a time it is an instant, with the rule of SPEC 0067 (an explicit offset is converted,
/// Z or no zone is UTC).
/// </summary>
public readonly record struct DayOrInstant(DateTime Utc)
{
    public static bool TryParse(string? value, IFormatProvider? provider, out DayOrInstant result)
    {
        var text = value?.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            result = new(OperationalDay.StartOf(date));
            return true;
        }

        var parsed = DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant);
        result = new(instant);
        return parsed;
    }
}
