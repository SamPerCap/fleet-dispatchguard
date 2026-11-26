using DispatchGuard.Core.Enums;
using DispatchGuard.DemoData;

namespace Infrastructure.Tests.SampleData;

/// <summary>Checked for every weekday as "today".</summary>
public sealed class DemoPlanTests
{
    // Wednesday 2026-11-18 10:00 Madrid (09:00 UTC) plus the six following days at different hours.
    public static TheoryData<DateTime> Anchors => new(Enumerable.Range(0, 7)
        .Select(i => new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc).AddDays(i).AddHours(i % 3 * 3)));

    private static readonly DemoPlan Plan = DemoPlan.Build(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Dataset_HasThreeBasesWithEnoughPeopleAndVariedFleet()
    {
        Assert.Equal(3, DemoDataset.Bases.Count);
        Assert.Single(DemoDataset.Bases, b => b.IsDefault);
        Assert.All(DemoDataset.Bases, b =>
        {
            Assert.InRange(b.Drivers.Count, 15, 25);
            Assert.InRange(b.Vehicles.Count, 10, 15);
            Assert.True(b.Vehicles.Select(v => v.SeatingCapacity).Distinct().Count() >= 4);
            Assert.True(b.Locations.Count >= 6);
        });
        var all = DemoDataset.Bases.SelectMany(b => b.Drivers).Select(d => d.IdentificationNumber).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        var plates = DemoDataset.Bases.SelectMany(b => b.Vehicles).Select(v => v.LicensePlate).ToList();
        Assert.Equal(plates.Count, plates.Distinct().Count());
        var names = DemoDataset.Locations.Select(l => l.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(DemoDataset.Locations, l => Assert.InRange(l.Latitude, 38.9m, 40.8m)); // Madrid, Guadalajara, Valencia
    }

    [Theory]
    [MemberData(nameof(Anchors))]
    public void Build_AnyDay_RespectsPlanningMargins(DateTime now)
    {
        var plan = DemoPlan.Build(now);
        var covered = plan.Trips.Where(t => t.Covered).ToList();

        Assert.Equal(plan.Trips.Count, plan.Trips.Select(t => t.Description).Distinct().Count());
        Assert.All(plan.Trips, t => Assert.True(t.Duration <= DemoPlanner.MaxTrip, t.Description));
        foreach (var services in covered.GroupBy(t => t.DriverId))
        {
            var mine = services.OrderBy(t => t.Start).ToList();
            for (var i = 1; i < mine.Count; i++)
            {
                var gap = mine[i].Start - mine[i - 1].End;
                Assert.True(gap >= DemoPlanner.DriverGap, $"{services.Key}: {mine[i - 1].Description} → {mine[i].Description}");
                if (DemoClock.LocalDate(mine[i].Start) != DemoClock.LocalDate(mine[i - 1].Start))
                    Assert.True(gap >= DemoPlanner.MinRest, $"{services.Key}: descanso de {gap} antes de {mine[i].Description}");
            }
        }
        foreach (var services in covered.GroupBy(t => t.VehiclePlate))
        {
            var mine = services.OrderBy(t => t.Start).ToList();
            for (var i = 1; i < mine.Count; i++) Assert.True(mine[i].Start >= mine[i - 1].End, $"{services.Key}: solape en {mine[i].Description}");
        }
        var seats = DemoDataset.Bases.SelectMany(b => b.Vehicles).ToDictionary(v => v.LicensePlate, v => v.SeatingCapacity);
        Assert.All(covered, t => Assert.True(seats[t.VehiclePlate!] >= t.RequiredSeats, t.Description));
    }

    [Theory]
    [MemberData(nameof(Anchors))]
    public void Build_AnyDay_HeavyDriverReaches90PercentOfTwoWeeks_WithinLimits(DateTime now)
    {
        var plan = DemoPlan.Build(now);
        foreach (var operatingBase in DemoDataset.Bases)
        {
            var heavy = operatingBase.Drivers[0].IdentificationNumber;
            var trips = plan.Trips.Where(t => t.DriverId == heavy).ToList();
            // The previous and the current natural week. With the demo's weekday pattern 90 % of 90 h is reached from Friday on (SPEC 0064).
            var today = DemoClock.LocalDate(now);
            var monday = DemoClock.MondayOf(today);
            var twoWeeks = trips.Where(t => DemoClock.LocalDate(t.Start) >= monday.AddDays(-7) && t.End <= now).Sum(t => t.Duration.TotalHours);
            if (today.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday)
                Assert.True(twoWeeks >= 81, $"{operatingBase.Name}: {twoWeeks} h en dos semanas naturales");
            foreach (var week in trips.GroupBy(t => DemoClock.MondayOf(DemoClock.LocalDate(t.Start))))
            {
                Assert.True(week.Sum(t => t.Duration.TotalHours) <= 56, $"{operatingBase.Name}: semana {week.Key}");
                var next = trips.Where(t => DemoClock.MondayOf(DemoClock.LocalDate(t.Start)) == week.Key.AddDays(7)).Sum(t => t.Duration.TotalHours);
                Assert.True(week.Sum(t => t.Duration.TotalHours) + next <= 90, $"{operatingBase.Name}: dos semanas desde {week.Key}");
            }
            Assert.Contains(trips, t => DemoClock.LocalDate(t.Start) == plan.Today);
        }
    }

    [Fact]
    public void Build_TodayHasEveryIncidentInEveryBase()
    {
        foreach (var operatingBase in DemoDataset.Bases)
        {
            var today = Plan.Trips.Where(t => t.BaseKey == operatingBase.Key && DemoClock.LocalDate(t.Start) == Plan.Today).ToList();
            var uncovered = today.Where(t => !t.Covered).Select(t => t.Start - Plan.Now).ToList();
            Assert.Contains(uncovered, d => d < TimeSpan.FromHours(1));
            Assert.Contains(uncovered, d => d >= TimeSpan.FromHours(1) && d <= TimeSpan.FromHours(3));

            var partial = Plan.Trips.Where(t => t.BaseKey == operatingBase.Key).GroupBy(t => t.Booking)
                .Where(g => g.Any(t => t.Covered) && g.Any(t => !t.Covered) && g.Min(t => t.Start) > Plan.Now).ToList();
            Assert.Contains(partial, g => g.Key.Kind == BookingKind.Circuit);

            var sick = Plan.DriverLeaves.Single(l => l.Type == UnavailabilityType.SickLeave && operatingBase.Drivers.Any(d => d.IdentificationNumber == l.DriverId));
            Assert.Contains(today, t => t.DriverId == sick.DriverId && t.Start < sick.End && sick.Start < t.End);

            var workshop = Plan.VehicleLeaves.Single(l => l.Type == VehicleUnavailabilityType.Repair && operatingBase.Vehicles.Any(v => v.LicensePlate == l.VehiclePlate));
            Assert.True(workshop.Start <= Plan.Now && Plan.Now < workshop.End);
            Assert.Contains(today, t => t.VehiclePlate == workshop.VehiclePlate);
            Assert.DoesNotContain(Plan.Trips, t => t.VehiclePlate == workshop.VehiclePlate && DemoClock.LocalDate(t.Start) > Plan.Today);
        }
    }

    /// <summary>For the 28-day history of every base, with drivers and vehicles busy on most days (SPEC 0061).</summary>
    [Fact]
    public void Build_CoversFourWeeksOfPast_InEveryBase()
    {
        foreach (var operatingBase in DemoDataset.Bases)
        {
            var days = Plan.Trips.Where(t => t.BaseKey == operatingBase.Key && t.Covered).Select(t => DemoClock.LocalDate(t.Start)).ToHashSet();
            for (var d = Plan.Today.AddDays(-DemoPlan.PastDays); d < Plan.Today; d = d.AddDays(1)) Assert.Contains(d, days);
            var past = Plan.Trips.Where(t => t.BaseKey == operatingBase.Key && t.Covered && t.End <= Plan.Now).ToList();
            // Most of the people and fleet of the base have history (not just a handful).
            Assert.True(past.Select(t => t.DriverId).Distinct().Count() >= operatingBase.Drivers.Count - 2, operatingBase.Name);
            Assert.True(past.Select(t => t.VehiclePlate).Distinct().Count() >= operatingBase.Vehicles.Count / 2, operatingBase.Name);
        }
    }

    /// <summary>The past vacation and inspection are registered, and nobody is planned while absent (SPEC 0061).</summary>
    [Theory]
    [MemberData(nameof(Anchors))]
    public void Build_PastAbsences_NoServiceDuringThem(DateTime now)
    {
        var plan = DemoPlan.Build(now);
        var vacations = plan.DriverLeaves.Where(l => l.Type == UnavailabilityType.Vacation).ToList();
        var itv = plan.VehicleLeaves.Where(l => l.Type == VehicleUnavailabilityType.TechnicalInspection).ToList();
        Assert.Equal(DemoDataset.Bases.Count, vacations.Count);
        Assert.Equal(DemoDataset.Bases.Count, itv.Count);
        Assert.All(vacations, l => Assert.True(l.End <= plan.Now, "la vacación es pasada"));
        Assert.All(itv, l => Assert.True(l.End <= plan.Now, "la ITV es pasada"));
        foreach (var leave in vacations)
            Assert.DoesNotContain(plan.Trips, t => t.DriverId == leave.DriverId && t.Start < leave.End && leave.Start < t.End);
        foreach (var leave in itv)
            Assert.DoesNotContain(plan.Trips, t => t.VehiclePlate == leave.VehiclePlate && t.Start < leave.End && leave.Start < t.End);
    }

    [Fact]
    public void Build_CoversTwoWeeksWithEveryServiceTypeAndMultiTripBookings()
    {
        var days = Plan.Trips.Select(t => DemoClock.LocalDate(t.Start)).ToHashSet();
        for (var d = Plan.Today.AddDays(-7); d <= Plan.Today.AddDays(7); d = d.AddDays(1)) Assert.Contains(d, days);
        foreach (var operatingBase in DemoDataset.Bases)
        {
            var mine = Plan.Trips.Where(t => t.BaseKey == operatingBase.Key).ToList();
            Assert.All(Enum.GetValues<ServiceType>(), s => Assert.Contains(mine, t => t.ServiceType == s));
            Assert.Contains(mine.GroupBy(t => t.Booking), g => g.Count() >= 3 && g.Key.Kind != BookingKind.RegularLine);
            Assert.All(mine.GroupBy(t => t.Booking), g => Assert.InRange(g.Count(), 1, 50));
        }
    }

    [Fact]
    public void Build_PlansEveryKindOfRejectedAttemptInEveryBase_OnFutureCoveredTrips()
    {
        var trips = Plan.Trips.ToDictionary(t => t.Description);
        foreach (var operatingBase in DemoDataset.Bases)
        {
            var mine = Plan.Rejections.Where(r => trips[r.TripDescription].BaseKey == operatingBase.Key).ToList();
            Assert.All(Enum.GetValues<RejectionKind>(), k => Assert.Contains(mine, r => r.Kind == k));
            Assert.All(mine, r =>
            {
                var trip = trips[r.TripDescription];
                Assert.True(trip.Covered && trip.Start > Plan.Now);
                Assert.False(r.DriverId == trip.DriverId && r.VehiclePlate == trip.VehiclePlate);
            });
        }
    }
}
