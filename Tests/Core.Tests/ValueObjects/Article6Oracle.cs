using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

/// <summary>
/// Independent reading of CE 561/2006 Art. 6 for the tests of SPEC 0064. Written from the text (Art. 4(i), 4(k), 4(l), 6.1,
/// 6.2, 6.3), not from the engine: it looks at the whole calendar at once, in slots of 15 minutes, and asks each question
/// literally. It uses neither the engine nor the platform's time zones: summer time comes from Directive 2000/84/EC. Every period must start and end on a quarter of an hour.
/// </summary>
internal static class Article6Oracle
{
    private static readonly TimeSpan Slot = TimeSpan.FromMinutes(15);

    /// <summary>Articles broken by the calendar; empty when it is legal.</summary>
    public static IReadOnlySet<string> Broken(IEnumerable<DrivingPeriod> calendar)
    {
        var periods = calendar.OrderBy(p => p.Start).ToList();
        var broken = new SortedSet<string>();
        if (periods.Count == 0) return broken;

        // Art. 4(k): the driving between a daily rest and the next; a rest is nine hours or more without driving.
        var days = new List<(DateTime Start, double Hours)>();
        DateTime? lastEnd = null;
        foreach (var period in periods)
        {
            var hours = (period.End - period.Start).TotalHours;
            if (lastEnd is null || (period.Start - lastEnd.Value).TotalHours >= 9) days.Add((period.Start, hours));
            else days[^1] = (days[^1].Start, days[^1].Hours + hours);
            lastEnd = period.End;
        }

        // Art. 6.1: nine hours, ten at most twice a week.
        if (days.Any(d => d.Hours > 10)) broken.Add("6.1");
        foreach (var spain in new[] { true, false })
        {
            if (days.Where(d => d.Hours > 9).GroupBy(d => Week(d.Start, spain)).Any(g => g.Count() > 2)) broken.Add("6.1");

            // Art. 4(l) and 6.2: accumulated driving during a week; Art. 6.3: during two consecutive weeks.
            var weeks = new SortedDictionary<int, double>();
            foreach (var period in periods)
                for (var t = period.Start; t < period.End; t += Slot)
                {
                    var week = Week(t, spain);
                    weeks[week] = weeks.GetValueOrDefault(week) + Slot.TotalHours;
                }

            if (weeks.Values.Any(h => h > 56)) broken.Add("6.2");
            if (weeks.Any(w => w.Value + weeks.GetValueOrDefault(w.Key + 1) > 90)) broken.Add("6.3");
        }

        return broken;
    }

    /// <summary>Art. 4(i): number of the week (Monday 00:00 to Sunday 24:00) that contains the instant, in Spain or in UTC.</summary>
    private static int Week(DateTime utc, bool spain)
    {
        var local = spain ? utc.AddHours(IsSummerTime(utc) ? 2 : 1) : utc;
        return (int)Math.Floor((local - FirstMonday).TotalDays / 7);
    }

    private static readonly DateTime FirstMonday = new(2024, 1, 1); // 1 January 2024 was a Monday

    /// <summary>Directive 2000/84/EC: from 01:00 UTC on the last Sunday of March to 01:00 UTC on the last Sunday of October.</summary>
    private static bool IsSummerTime(DateTime utc) => utc >= LastSunday(utc.Year, 3).AddHours(1) && utc < LastSunday(utc.Year, 10).AddHours(1);

    private static DateTime LastSunday(int year, int month)
    {
        var lastOfMonth = new DateTime(year, month, 31);
        return lastOfMonth.AddDays(-(int)lastOfMonth.DayOfWeek);
    }
}
