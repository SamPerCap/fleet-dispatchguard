using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.ValueObjects;

public class ServiceReachTests
{
    private static readonly DateTime Eight = new(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeWindow Service = new(Eight, Eight.AddHours(2));
    private static readonly GeoCoordinate Origin = At(0);
    private static readonly GeoCoordinate Destination = At(100);

    /// <summary>Straight north of the origin; 111.195 km per degree in the haversine of <see cref="GeoCoordinate"/>.</summary>
    private static GeoCoordinate At(double km) => new(40m + (decimal)(km / 111.19492664), -3.7m);

    private static HeldService Held(int id, double startHours, double endHours, double fromKm, double toKm) =>
        new(id, new TimeWindow(Eight.AddHours(startHours), Eight.AddHours(endHours)), At(fromKm), At(toKm));

    private static ServiceReach Reach(GeoCoordinate? home, params HeldService[] held) => ServiceReach.For(Service, Origin, Destination, home, held);

    [Fact]
    public void Position_NoServices_IsTheReferenceLocation()
    {
        var reach = Reach(At(12));
        Assert.Equal(ReachSource.HomeLocation, reach.Source);
        Assert.Equal(12, reach.DistanceKm!.Value, 1);
        Assert.True(reach.Arrives);
    }

    [Fact]
    public void Position_NoServicesNorReference_IsUnknown()
    {
        var reach = Reach(null);
        Assert.Equal(ReachSource.Unknown, reach.Source);
        Assert.Null(reach.DistanceKm);
        Assert.Null(reach.StepKm);
    }

    [Theory]
    [InlineData(539, ReachSource.PreviousService, 3.0)] // 8 h 59: same working day
    [InlineData(540, ReachSource.HomeLocation, 12.0)] // 9 h: a daily rest fits, it is another day
    public void Position_PreviousService_OnlyInTheSameWorkingDay(int gapMinutes, ReachSource source, double km)
    {
        var end = -gapMinutes / 60.0;
        var reach = Reach(At(12), Held(7, end - 1, end, 50, 3));
        Assert.Equal(source, reach.Source);
        Assert.Equal(km, reach.DistanceKm!.Value, 1);
        Assert.Equal(source == ReachSource.PreviousService ? 7 : null, reach.FromTripId);
    }

    [Fact]
    public void Position_SeveralServices_IsTheLastOneBeforeWhateverTheirOrder()
    {
        var reach = Reach(At(12), Held(1, -3, -2, 0, 20), Held(3, 4, 5, 100, 100), Held(2, -2, -1, 20, 4), Held(4, -6, -4, 0, 1));
        Assert.Equal(2, reach.FromTripId);
        Assert.Equal(4, reach.DistanceKm!.Value, 1);
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(59, false)]
    public void Arrives_FromThePreviousService_At70KmPerHour(int gapMinutes, bool arrives)
    {
        // 69.9 km: 59.9 min at 70 km/h.
        var reach = Reach(At(0), Held(7, -3, -gapMinutes / 60.0, 0, 69.9));
        Assert.Equal(arrives, reach.Arrives);
        Assert.Equal(arrives ? null : 7, reach.LateFromTripId);
        if (!arrives) Assert.Contains("estimación", reach.LateReason.Es());
    }

    [Theory]
    [InlineData(60, true)]
    [InlineData(59, false)]
    public void Arrives_ToTheNextService_At70KmPerHour(int gapMinutes, bool arrives)
    {
        var reach = Reach(At(0), Held(8, 2 + gapMinutes / 60.0, 5, 169.9, 200));
        Assert.Equal(arrives, reach.Arrives);
        Assert.Equal(arrives ? null : 8, reach.LateToTripId);
    }

    [Fact]
    public void Arrives_PreviousServiceOfAnotherDay_StillCounts()
    {
        // 10 h of gap (another working day) and 800 km: 11 h 25 at 70 km/h.
        var reach = Reach(At(1), Held(7, -12, -10, 700, 800));
        Assert.Equal(ReachSource.HomeLocation, reach.Source);
        Assert.Equal(7, reach.LateFromTripId);
    }

    [Theory]
    [InlineData(0.0, 5.0)]
    [InlineData(5.0, 5.0)]
    [InlineData(5.1, 15.0)]
    [InlineData(15.0, 15.0)]
    [InlineData(15.1, 30.0)]
    [InlineData(30.0, 30.0)]
    public void StepOf_IsTheSmallestInclusiveStep(double km, double step) => Assert.Equal(step, RecommendationSteps.StepOf(km));

    [Fact]
    public void StepOf_BeyondTheLastOrUnknown_IsNull()
    {
        Assert.Null(RecommendationSteps.StepOf(30.1));
        Assert.Null(RecommendationSteps.StepOf(null));
        Assert.Equal([5.0, 15.0, 30.0], RecommendationSteps.Km);
    }

    private static ReachCandidate Candidate(int id, double? km, bool arrives = true, bool sameBooking = false) =>
        new(id, new ServiceReach(km, km is null ? ReachSource.Unknown : ReachSource.HomeLocation, null, arrives ? null : 1, null), sameBooking);

    [Fact]
    public void Choose_WidensOnlyWhenThereIsNoPair()
    {
        Assert.Equal(5, RecommendationPick.Choose([Candidate(1, 20), Candidate(2, 4)], [Candidate(9, 5)])!.StepKm);
        var pick = RecommendationPick.Choose([Candidate(1, 3)], [Candidate(8, 31), Candidate(9, 14)])!;
        Assert.Equal((1, 9, 15.0), (pick.Driver.Id, pick.Vehicle.Id, pick.StepKm));
    }

    [Fact]
    public void Choose_InsideTheStep_KeepsTheSearchOrder_AndASmallerStepGoesFirst()
    {
        Assert.Equal(1, RecommendationPick.Choose([Candidate(1, 29), Candidate(2, 16)], [Candidate(9, 1)])!.Driver.Id);
        Assert.Equal(2, RecommendationPick.Choose([Candidate(1, 16), Candidate(2, 15)], [Candidate(9, 1)])!.Driver.Id);
        // The vehicle forces 30 km, so the drivers are looked at up to 30 km too.
        Assert.Equal(1, RecommendationPick.Choose([Candidate(1, 29), Candidate(2, 4)], [Candidate(9, 20)])!.Driver.Id);
    }

    [Fact]
    public void Choose_WhoDoesNotArrive_OrHasNoPosition_IsNeverPicked()
    {
        Assert.Equal(2, RecommendationPick.Choose([Candidate(1, 1, arrives: false), Candidate(2, 25)], [Candidate(9, 1)])!.Driver.Id);
        Assert.Null(RecommendationPick.Choose([Candidate(1, null)], [Candidate(9, 1)]));
        Assert.Null(RecommendationPick.Choose([Candidate(1, 1)], [Candidate(9, 30.1)]));
        Assert.Null(RecommendationPick.Choose([], [Candidate(9, 1)]));
    }

    [Fact]
    public void Choose_SameBooking_HasNoDistanceLimit_EachOnItsOwn_ButMustArrive()
    {
        var pick = RecommendationPick.Choose([Candidate(1, 1), Candidate(2, 70, sameBooking: true)], [Candidate(8, 2), Candidate(9, 90)])!;
        Assert.Equal((2, 8), (pick.Driver.Id, pick.Vehicle.Id));
        Assert.Equal(8, RecommendationPick.Choose([Candidate(1, 1)], [Candidate(7, 1), Candidate(8, 70, sameBooking: true)])!.Vehicle.Id);
        Assert.Equal(1, RecommendationPick.Choose([Candidate(1, 1), Candidate(2, 70, arrives: false, sameBooking: true)], [Candidate(8, 2)])!.Driver.Id);
        Assert.Equal(2, RecommendationPick.Choose([Candidate(2, null, sameBooking: true)], [Candidate(8, 2)])!.Driver.Id);
    }

    [Fact]
    public void Counts_SayWhyEachOneIsLeftOut()
    {
        Assert.Equal(new ReachCounts(6, 2, 1, 1), ReachCounts.Of(
            [Candidate(1, 4), Candidate(2, 31), Candidate(3, 90), Candidate(4, 50, arrives: false), Candidate(5, null), Candidate(6, 70, sameBooking: true)]));
    }
}
