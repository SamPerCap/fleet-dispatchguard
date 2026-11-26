using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Benchmarks;

public sealed record ComplianceScenario(
    string Name,
    IReadOnlyList<DrivingPeriod> History,
    DateTime TripStart,
    TimeSpan TripDuration)
{
    public override string ToString() => Name;
}

/// <summary>
/// The expected outcome of each scenario is pinned by <c>ComplianceScenariosTests</c>, so that the figures always describe the
/// path they claim to (SPEC 0039).
/// </summary>
public static class ComplianceScenarios
{
    private const int DriverId = 1;

    /// <summary>Two weeks before the trip week; fixed, in UTC, for reproducibility.</summary>
    private static readonly DateTime FirstMonday = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<ComplianceScenario> All() =>
        [NewDriver(), RegularTwoWeeks(), ContinuousDrivingExceeded()];

    /// <summary>New hire with no history: compliant, only the "weekly rest not verifiable" warning (SPEC 0040).</summary>
    public static ComplianceScenario NewDriver() =>
        new("NewDriver", [], FirstMonday.AddDays(14).AddHours(7), TimeSpan.FromHours(2));

    /// <summary>
    /// Ten working days (Mon–Fri × 2 weeks), each 07:00–11:00 and 11:45–15:45 (4h + 45 min break + 4h):
    /// 80h in two natural weeks. A 2h trip the following Monday 07:00 is compliant with no violations.
    /// </summary>
    public static ComplianceScenario RegularTwoWeeks() =>
        new("RegularTwoWeeks", RegularHistory(), FirstMonday.AddDays(14).AddHours(7), TimeSpan.FromHours(2));

    /// <summary>Same history; a 2h trip right after the last 4h block (6h continuous &gt; 4h30) is non-compliant.</summary>
    public static ComplianceScenario ContinuousDrivingExceeded()
    {
        var history = RegularHistory();
        return new("ContinuousDrivingExceeded", history, history[^1].End, TimeSpan.FromHours(2));
    }

    private static List<DrivingPeriod> RegularHistory()
    {
        var periods = new List<DrivingPeriod>();
        for (var week = 0; week < 2; week++)
        for (var weekday = 0; weekday < 5; weekday++)
        {
            var day = FirstMonday.AddDays(week * 7 + weekday);
            periods.Add(new DrivingPeriod(DriverId, day.AddHours(7), day.AddHours(11)));
            periods.Add(new DrivingPeriod(DriverId, day.AddHours(11).AddMinutes(45), day.AddHours(15).AddMinutes(45)));
        }
        return periods;
    }
}
