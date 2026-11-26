using DispatchGuard.Application.Services.Resources;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services.Resources;

public class ResourceHistoryTests
{
    // 22 Nov 2026 10:00 in Madrid (CET, UTC+1). Today runs 21 Nov 23:00Z → 22 Nov 23:00Z.
    private static readonly DateTime Now = new(2026, 11, 22, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TodayFrom = new(2026, 11, 21, 23, 0, 0, DateTimeKind.Utc);
    // Day −27 is 26 Oct (CET since 25 Oct): 25 Oct 23:00Z.
    private static readonly DateTime WindowFrom = new(2026, 10, 25, 23, 0, 0, DateTimeKind.Utc);

    private static int _ids;
    private static HistoryTrip Trip(DateTime start, DateTime end, double? km = null, TripStatus status = TripStatus.Assigned) =>
        new(++_ids, start, end, status, km);

    [Fact]
    public void Window_Is28MadridDays_EndingToday_OldestFirst()
    {
        var days = ResourceHistory.Window(Now);

        Assert.Equal(ResourceHistory.Days, days.Count);
        Assert.Equal(28, days.Count);
        Assert.Equal((TodayFrom, TodayFrom.AddDays(1)), days[^1]);
        Assert.Equal(WindowFrom, days[0].From);
        Assert.All(days.Zip(days.Skip(1)), p => Assert.Equal(p.First.To, p.Second.From));
    }

    [Fact]
    public void Window_AcrossTheDstChange_HasA25HourDay()
    {
        // 25 Oct 2026: Madrid goes back from CEST to CET at 03:00 → the day lasts 25 h (24 Oct 22:00Z → 25 Oct 23:00Z).
        var days = ResourceHistory.Window(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));

        var dst = days.Single(d => d.From == new DateTime(2026, 10, 24, 22, 0, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromHours(25), dst.To - dst.From);
    }

    [Theory]
    [InlineData(TripStatus.Assigned, true)]
    [InlineData(TripStatus.ManuallyAssigned, true)]
    [InlineData(TripStatus.InProgress, true)]
    [InlineData(TripStatus.Completed, true)]
    [InlineData(TripStatus.Cancelled, false)]
    [InlineData(TripStatus.Pending, false)]
    public void IsDone_OnlyDrivingStatuses(TripStatus status, bool expected) =>
        Assert.Equal(expected, ResourceHistory.IsDone(status, Now.AddHours(-1), Now));

    [Fact]
    public void IsDone_EndsExactlyNow_IsDone_EndsAfterNow_IsNot()
    {
        Assert.True(ResourceHistory.IsDone(TripStatus.Assigned, Now, Now));
        Assert.False(ResourceHistory.IsDone(TripStatus.Assigned, Now.AddMinutes(1), Now));
    }

    [Fact]
    public void InWindow_EndedBeforeTheWindow_IsOut_EndedInside_IsIn()
    {
        Assert.False(ResourceHistory.InWindow(Trip(WindowFrom.AddHours(-3), WindowFrom), Now));
        Assert.True(ResourceHistory.InWindow(Trip(WindowFrom.AddHours(-1), WindowFrom.AddHours(1)), Now));
        Assert.False(ResourceHistory.InWindow(Trip(Now.AddHours(-1), Now.AddHours(1)), Now)); // in progress
    }

    [Fact]
    public void Daily_SumsDoneTripsOfEachDay()
    {
        var trips = new[]
        {
            Trip(TodayFrom.AddHours(7), TodayFrom.AddHours(9)),          // today 07:00–09:00 → 2 h
            Trip(TodayFrom.AddHours(-17), TodayFrom.AddHours(-12)),      // yesterday 07:00–12:00 → 5 h
            Trip(TodayFrom.AddHours(-10), TodayFrom.AddHours(-5.5)),     // yesterday 14:00–18:30 → 4.5 h
        };

        var days = ResourceHistory.Daily(trips, Now);

        Assert.Equal(TimeSpan.FromHours(2), days[^1].Duration);
        Assert.Equal(1, days[^1].Trips);
        Assert.Equal(TimeSpan.FromHours(9.5), days[^2].Duration);
        Assert.Equal(2, days[^2].Trips);
        Assert.All(days.Take(26), d => Assert.Equal(TimeSpan.Zero, d.Duration));
    }

    [Fact]
    public void Daily_TripAcrossMadridMidnight_IsSplitBetweenBothDays_AndCountsOnItsStartDay()
    {
        // Yesterday 23:00 → today 01:00 Madrid.
        var trip = Trip(TodayFrom.AddHours(-1), TodayFrom.AddHours(1));

        var days = ResourceHistory.Daily([trip], Now);

        Assert.Equal(TimeSpan.FromHours(1), days[^2].Duration);
        Assert.Equal(TimeSpan.FromHours(1), days[^1].Duration);
        Assert.Equal(1, days[^2].Trips);
        Assert.Equal(0, days[^1].Trips);
    }

    [Fact]
    public void Daily_TripStartedBeforeTheWindow_CountsOnlyItsPartInside_OnTheFirstDay()
    {
        var trip = Trip(WindowFrom.AddHours(-1.5), WindowFrom.AddHours(0.5), km: 40);

        var days = ResourceHistory.Daily([trip], Now);

        Assert.Equal(TimeSpan.FromHours(0.5), days[0].Duration);
        Assert.Equal(1, days[0].Trips);
        Assert.Equal(40, days[0].Km);
    }

    [Fact]
    public void Daily_IgnoresTripsNotDone_AndCancelled()
    {
        var trips = new[]
        {
            Trip(Now.AddHours(-1), Now.AddHours(1)),                                   // in progress
            Trip(TodayFrom.AddHours(1), TodayFrom.AddHours(2), status: TripStatus.Cancelled),
            Trip(Now.AddHours(5), Now.AddHours(6)),                                    // future
        };

        var days = ResourceHistory.Daily(trips, Now);

        Assert.All(days, d => { Assert.Equal(TimeSpan.Zero, d.Duration); Assert.Equal(0, d.Trips); });
    }

    [Fact]
    public void Totals_CountDaysAbove9AndAbove10_Strictly()
    {
        var yesterday = TodayFrom.AddDays(-1);
        var trips = new[]
        {
            Trip(yesterday.AddDays(-2).AddHours(6), yesterday.AddDays(-2).AddHours(15)),          // exactly 9 h → not above
            Trip(yesterday.AddDays(-1).AddHours(6), yesterday.AddDays(-1).AddHours(15.25)),       // 9 h 15 → above 9
            Trip(yesterday.AddHours(6), yesterday.AddHours(16.5)),                                // 10 h 30 → above 9 and above 10
        };

        var days = ResourceHistory.Daily(trips, Now);
        var totals = ResourceHistory.Totals(days, trips.Where(t => ResourceHistory.InWindow(t, Now)).ToList());

        Assert.Equal(2, totals.DaysOverDaily);
        Assert.Equal(1, totals.DaysOverExtended);
        Assert.Equal(3, totals.DaysWorked);
        Assert.Equal(TimeSpan.FromHours(28.75), totals.Duration);
        Assert.Equal(3, totals.Trips);
    }

    [Fact]
    public void Totals_LimitsComeFromDrivingLimits()
    {
        Assert.Equal(DrivingLimits.MaxDailyDriving, ResourceHistory.DailyLimit);
        Assert.Equal(DrivingLimits.MaxDailyDrivingExtended, ResourceHistory.ExtendedDailyLimit);
    }

    [Fact]
    public void Totals_KmOnlyFromEstimableTrips_TheRestCountedApart()
    {
        var trips = new[]
        {
            Trip(TodayFrom.AddHours(7), TodayFrom.AddHours(8), km: 67.3),
            Trip(TodayFrom.AddHours(-10), TodayFrom.AddHours(-9), km: 12.4),
            Trip(TodayFrom.AddHours(-20), TodayFrom.AddHours(-18), km: null),
        };

        var days = ResourceHistory.Daily(trips, Now);
        var totals = ResourceHistory.Totals(days, trips);

        Assert.Equal(67.3, days[^1].Km, 6);
        Assert.Equal(12.4, days[^2].Km, 6);
        Assert.Equal(79.7, totals.Km, 6);
        Assert.Equal(1, totals.TripsWithoutKm);
    }

    [Fact]
    public void EstimateKm_StraightLineThroughStops_RoundedTo100m()
    {
        var madrid = new GeoCoordinate(40.4168m, -3.7038m);
        var toledo = new GeoCoordinate(39.8628m, -4.0273m);

        var direct = ResourceHistory.EstimateKm([madrid, toledo]);
        var roundTrip = ResourceHistory.EstimateKm([madrid, toledo, madrid]);

        Assert.NotNull(direct);
        Assert.Equal(Math.Round(madrid.DistanceTo(toledo), 1), direct);
        Assert.InRange(direct!.Value, 67, 68);
        Assert.Equal(Math.Round(2 * madrid.DistanceTo(toledo), 1), roundTrip);
    }

    [Fact]
    public void EstimateKm_SameOriginAndDestinationWithoutStops_IsNotEstimable()
    {
        var origin = new GeoCoordinate(40.4168m, -3.7038m);

        Assert.Null(ResourceHistory.EstimateKm([origin, origin]));
        Assert.Null(ResourceHistory.EstimateKm([origin]));
        Assert.Null(ResourceHistory.EstimateKm([]));
    }
}
