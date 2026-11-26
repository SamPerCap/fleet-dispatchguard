using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Entities;

public class TripTests
{
    private static Location NewLocation(string name = "Origin") =>
        new(
            name: name,
            address: "Calle Falsa 123",
            coordinate: new GeoCoordinate(40.4168m, -3.7038m),
            city: "Madrid",
            province: "Madrid");

    private static TimeWindow NewWindow() =>
        new(
            new DateTime(2026, 7, 10, 8, 0, 0),
            new DateTime(2026, 7, 10, 12, 0, 0));

    private static Trip NewTrip(ServiceType serviceType, string? description = null) =>
        new(
            origin: NewLocation("Origin"),
            destination: NewLocation("Destination"),
            scheduledWindow: NewWindow(),
            requiredSeats: 20,
            serviceType: serviceType,
            description: description);

    [Fact]
    public void Constructor_WithRegularServiceType_SetsServiceTypeToRegular()
    {
        var trip = NewTrip(ServiceType.Regular, description: "Morning commuter route");

        Assert.Equal(ServiceType.Regular, trip.ServiceType);
    }

    [Theory]
    [InlineData(ServiceType.Regular)]
    [InlineData(ServiceType.Discretionary)]
    [InlineData(ServiceType.Circuit)]
    [InlineData(ServiceType.AirportTransfer)]
    public void Constructor_WithEachServiceType_RoundTripsToTripServiceType(ServiceType serviceType)
    {
        var trip = NewTrip(serviceType);

        Assert.Equal(serviceType, trip.ServiceType);
    }
}
