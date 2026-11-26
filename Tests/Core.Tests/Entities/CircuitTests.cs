using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Entities;

public class CircuitTests
{
    private static Location NewLocation(string name = "Origin") =>
        new(
            name: name,
            address: "Calle Falsa 123",
            coordinate: new GeoCoordinate(40.4168m, -3.7038m),
            city: "Madrid",
            province: "Madrid");

    private static TimeWindow CircuitDuration() =>
        new(
            new DateTime(2026, 7, 10, 0, 0, 0),
            new DateTime(2026, 7, 12, 0, 0, 0));

    private static Circuit NewCircuit() =>
        new(
            name: "Multi-day tour",
            duration: CircuitDuration());

    /// <summary>The window genuinely overlaps <see cref="CircuitDuration"/>.</summary>
    private static Trip NewOverlappingTrip(ServiceType serviceType) =>
        new(
            origin: NewLocation("Origin"),
            destination: NewLocation("Destination"),
            scheduledWindow: new TimeWindow(
                new DateTime(2026, 7, 10, 8, 0, 0),
                new DateTime(2026, 7, 10, 12, 0, 0)),
            requiredSeats: 20,
            serviceType: serviceType);

    [Theory]
    [InlineData(ServiceType.Regular)]
    [InlineData(ServiceType.Discretionary)]
    [InlineData(ServiceType.AirportTransfer)]
    public void AddTrip_WithNonCircuitServiceType_ThrowsWithExactMessage(ServiceType serviceType)
    {
        var circuit = NewCircuit();
        var trip = NewOverlappingTrip(serviceType);

        var exception = Assert.Throws<InvalidOperationException>(() => circuit.AddTrip(trip));

        Assert.Equal("Trip added to a circuit must have ServiceType.Circuit", exception.Message);
        Assert.Empty(circuit.Trips);
    }

    [Fact]
    public void AddTrip_WithNonCircuitServiceTypeAndNonOverlappingWindow_ThrowsServiceTypeMessage_NotOverlapMessage()
    {
        // The window is entirely outside the circuit duration AND the service type is wrong.
        var circuit = NewCircuit();
        var trip = new Trip(
            origin: NewLocation("Origin"),
            destination: NewLocation("Destination"),
            scheduledWindow: new TimeWindow(
                new DateTime(2026, 8, 1, 8, 0, 0),
                new DateTime(2026, 8, 1, 12, 0, 0)),
            requiredSeats: 20,
            serviceType: ServiceType.Regular);

        var exception = Assert.Throws<InvalidOperationException>(() => circuit.AddTrip(trip));

        // The service-type guard must run before the overlap check (SPEC 0017).
        Assert.Equal("Trip added to a circuit must have ServiceType.Circuit", exception.Message);
    }

    [Fact]
    public void AddTrip_WithCircuitServiceTypeAndOverlappingWindow_AddsSuccessfully()
    {
        var circuit = NewCircuit();
        var trip = NewOverlappingTrip(ServiceType.Circuit);

        circuit.AddTrip(trip);

        Assert.Single(circuit.Trips);
        Assert.Contains(trip, circuit.Trips);
    }

    [Fact]
    public void AddTrip_WithCircuitServiceTypeButNonOverlappingWindow_ThrowsOverlapMessage()
    {
        var circuit = NewCircuit();
        var trip = new Trip(
            origin: NewLocation("Origin"),
            destination: NewLocation("Destination"),
            scheduledWindow: new TimeWindow(
                new DateTime(2026, 8, 1, 8, 0, 0),
                new DateTime(2026, 8, 1, 12, 0, 0)),
            requiredSeats: 20,
            serviceType: ServiceType.Circuit);

        var exception = Assert.Throws<InvalidOperationException>(() => circuit.AddTrip(trip));

        Assert.Equal("Trip must fall within circuit duration", exception.Message);
    }
}
