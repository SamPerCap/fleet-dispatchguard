using DispatchGuard.Application.Services.Control;

namespace Core.Tests.Services.Control;

public class ControlIncidentsTests
{
    private static readonly DateTime Now = new(2035, 1, 8, 11, 40, 0, DateTimeKind.Utc);
    private static readonly ControlAlertSeverity Urgent = ControlAlertSeverity.Urgent, Warning = ControlAlertSeverity.Warning;

    [Fact]
    public void VehicleInWorkshop_WithThreeTrips_IsOneIncidentWithImpactThree()
    {
        var trips = new[] { Trip(1, 13, 50), Trip(2, 15, 30), Trip(3, 18, 20) };
        var incidents = ControlIncidents.Group([
            Fact(ControlAlertType.VehicleUnavailable, Warning, "Vehicle", 7, Now.AddHours(1.5), AlertFact.VehicleKey(7), trips)]);

        var incident = Assert.Single(incidents);
        Assert.Equal(3, incident.AffectedTripCount);
        Assert.Equal(100, incident.AffectedSeats);
        Assert.Equal("Cambiar de vehículo", incident.RecommendedAction.Es());
    }

    [Fact]
    public void SingleTripBooking_UncoveredTripAndIncompleteBooking_IsOneUrgentIncidentOnTheTrip()
    {
        var trip = Trip(10, 11.9, 10);
        var incidents = ControlIncidents.Group([
            Fact(ControlAlertType.UnassignedTrip, Urgent, "Trip", 10, trip.Start, AlertFact.BookingKey(5), [trip], "Asignar conductor y vehículo"),
            Fact(ControlAlertType.PartialBooking, Warning, "Booking", 5, trip.Start, AlertFact.BookingKey(5), [trip], "Completar la reserva"),
        ]);

        var incident = Assert.Single(incidents);
        Assert.Equal(Urgent, incident.Severity);
        Assert.Equal(ControlAlertType.UnassignedTrip, incident.Type);
        Assert.Equal(("Trip", 10), (incident.EntityType, incident.EntityId));
        Assert.Equal("Asignar conductor y vehículo", incident.RecommendedAction.Es());
        Assert.Equal(1, incident.AffectedTripCount); // The same trip is not counted twice.
        Assert.Equal([ControlAlertType.UnassignedTrip, ControlAlertType.PartialBooking], incident.Types);
    }

    [Fact]
    public void Group_TakesWorstSeverityAndEarliestDeadline()
    {
        var incidents = ControlIncidents.Group([
            Fact(ControlAlertType.DriverNearLimit, Warning, "Driver", 3, Now.AddHours(4), AlertFact.DriverKey(3), [Trip(1, 16, 10)]),
            Fact(ControlAlertType.ForcedAssignment, Warning, "Trip", 2, Now.AddHours(2), AlertFact.DriverKey(3), [Trip(2, 14, 10)]),
        ]);

        var incident = Assert.Single(incidents);
        Assert.Equal(Now.AddHours(2), incident.Deadline);
        Assert.Equal(ControlAlertType.ForcedAssignment, incident.Type); // Nearest deadline among equals.
        Assert.Equal(2, incident.AffectedTripCount);
    }

    [Fact]
    public void Order_UrgentFirst_ThenDeadline_ThenImpact_NoDeadlineLast()
    {
        var incidents = ControlIncidents.Group([
            Fact(ControlAlertType.PartialBooking, Warning, "Booking", 1, null, AlertFact.BookingKey(1), [Trip(1, 20, 10)]),
            Fact(ControlAlertType.DriverNearLimit, Warning, "Driver", 2, Now.AddHours(3), AlertFact.DriverKey(2), [Trip(2, 15, 10)]),
            Fact(ControlAlertType.VehicleUnavailable, Warning, "Vehicle", 3, Now.AddHours(3), AlertFact.VehicleKey(3), [Trip(3, 15, 10), Trip(4, 16, 10)]),
            Fact(ControlAlertType.UnassignedTrip, Urgent, "Trip", 5, Now.AddMinutes(30), AlertFact.BookingKey(9), [Trip(5, 12, 10)]),
        ]);

        Assert.Equal(["Booking:9", "Vehicle:3", "Driver:2", "Booking:1"], incidents.Select(i => i.Key));
    }

    [Fact]
    public void Order_SameDeadlineAndTrips_MoreSeatsFirst_ThenKey()
    {
        var incidents = ControlIncidents.Group([
            Fact(ControlAlertType.UnassignedTrip, Warning, "Trip", 1, Now.AddHours(2), AlertFact.BookingKey(1), [Trip(1, 14, 10)]),
            Fact(ControlAlertType.UnassignedTrip, Warning, "Trip", 2, Now.AddHours(2), AlertFact.BookingKey(2), [Trip(2, 14, 55)]),
            Fact(ControlAlertType.UnassignedTrip, Warning, "Trip", 3, Now.AddHours(2), AlertFact.BookingKey(3), [Trip(3, 14, 10)]),
        ]);

        Assert.Equal(["Booking:2", "Booking:1", "Booking:3"], incidents.Select(i => i.Key));
    }

    [Fact]
    public void UnrelatedAlerts_StaySeparate()
    {
        var incidents = ControlIncidents.Group([
            Fact(ControlAlertType.UnassignedTrip, Warning, "Trip", 1, Now.AddHours(2), AlertFact.BookingKey(1), [Trip(1, 14, 10)]),
            Fact(ControlAlertType.UnassignedTrip, Warning, "Trip", 2, Now.AddHours(2), AlertFact.BookingKey(2), [Trip(2, 14, 10)]),
        ]);
        Assert.Equal(2, incidents.Count);
    }

    [Fact]
    public void Empty_NoIncidents() => Assert.Empty(ControlIncidents.Group([]));

    [Fact]
    public void Group_WithoutAffectedTrips_ImpactZero()
    {
        var incident = Assert.Single(ControlIncidents.Group([
            Fact(ControlAlertType.DriverNearLimit, Warning, "Driver", 4, Now, AlertFact.DriverKey(4), [])]));
        Assert.Equal((0, 0), (incident.AffectedTripCount, incident.AffectedSeats));
    }

    private static AffectedTripDto Trip(int id, double hour, int seats)
    {
        var start = Now.Date.AddHours(hour);
        return new AffectedTripDto(id, Texts.AsWritten($"Servicio {id}"), start, start.AddHours(1), seats);
    }

    private static AlertFact Fact(ControlAlertType type, ControlAlertSeverity severity, string entityType, int entityId,
        DateTime? deadline, string key, IReadOnlyList<AffectedTripDto> trips, string action = "Cambiar de vehículo") =>
        new(new ControlAlertDto(type, severity, entityType, entityId, Texts.AsWritten($"{entityType} {entityId}"), deadline, Texts.AsWritten(action), null), key, trips);
}
