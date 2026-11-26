using DispatchGuard.Application.Services;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Services;

public class VehicleEligibilityServiceTests
{
    private readonly IVehicleEligibilityService _sut = new VehicleEligibilityService();

    private static TimeWindow TripWindow(DateTime start, DateTime end) => new(start, end);

    private static Vehicle NewVehicle(int seatingCapacity = 50) =>
        new(licensePlate: "1234ABC", brand: "Mercedes", model: "Tourismo", seatingCapacity: seatingCapacity);

    [Fact]
    public void CheckEligibility_DecommissionedVehicle_IsIneligible_WithNotActiveViolation()
    {
        var vehicle = NewVehicle();
        var decommissionedOn = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        vehicle.Decommission(decommissionedOn);
        var tripWindow = TripWindow(
            new DateTime(2026, 7, 10, 8, 0, 0),
            new DateTime(2026, 7, 10, 12, 0, 0));

        var result = _sut.CheckEligibility(vehicle, tripWindow, requiredSeats: 20);

        Assert.False(result.IsEligible);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("VehicleEligibility.NotActive", violation.RuleId);
        Assert.Equal(ViolationSeverity.Critical, violation.Severity);
        Assert.Contains("Vehículo no activo", violation.Message.Es());
        Assert.Contains("01/06/2026", violation.Message.Es());
    }

    [Fact]
    public void CheckEligibility_DecommissionedVehicle_MessageIncludesDecommissionedDateSuffix()
    {
        var vehicle = NewVehicle();
        vehicle.Decommission(new DateTime(2026, 5, 15));
        var tripWindow = TripWindow(DateTime.UtcNow, DateTime.UtcNow.AddHours(2));

        var result = _sut.CheckEligibility(vehicle, tripWindow, requiredSeats: 10);

        var violation = Assert.Single(result.Violations);
        Assert.Equal("Vehículo no activo (retirado el 15/05/2026)", violation.Message.Es());
    }

    [Fact]
    public void CheckEligibility_InsufficientCapacity_IsIneligible_WithExactMessage()
    {
        var vehicle = NewVehicle(seatingCapacity: 30);
        var tripWindow = TripWindow(DateTime.UtcNow, DateTime.UtcNow.AddHours(3));

        var result = _sut.CheckEligibility(vehicle, tripWindow, requiredSeats: 40);

        Assert.False(result.IsEligible);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("VehicleEligibility.InsufficientCapacity", violation.RuleId);
        Assert.Equal(ViolationSeverity.Critical, violation.Severity);
        Assert.Equal("Plazas insuficientes: tiene 30, el servicio necesita 40", violation.Message.Es());
    }

    [Fact]
    public void CheckEligibility_ExactCapacityMatch_IsEligible()
    {
        // Exact match: it must pass.
        var vehicle = NewVehicle(seatingCapacity: 30);
        var tripWindow = TripWindow(DateTime.UtcNow, DateTime.UtcNow.AddHours(2));

        var result = _sut.CheckEligibility(vehicle, tripWindow, requiredSeats: 30);

        Assert.True(result.IsEligible);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void CheckEligibility_MaintenanceOverlappingTripWindow_IsIneligible_WithMaintenanceMessage()
    {
        var vehicle = NewVehicle();
        var tripStart = new DateTime(2026, 7, 10, 8, 0, 0);
        var tripEnd = new DateTime(2026, 7, 10, 12, 0, 0);
        var unavailabilityEnd = new DateTime(2026, 7, 11, 0, 0, 0);
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(new DateTime(2026, 7, 9), unavailabilityEnd),
            type: VehicleUnavailabilityType.Maintenance));

        var result = _sut.CheckEligibility(vehicle, TripWindow(tripStart, tripEnd), requiredSeats: 10);

        Assert.False(result.IsEligible);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("VehicleEligibility.Unavailable.Maintenance", violation.RuleId);
        Assert.Equal(ViolationSeverity.Critical, violation.Severity);
        Assert.Equal("Vehículo no disponible (mantenimiento) del 09/07/2026 02:00 al 11/07/2026 02:00", violation.Message.Es());
    }

    [Fact]
    public void CheckEligibility_TechnicalInspectionOverlappingTripWindow_UsesCorrectRuleIdAndMessage()
    {
        var vehicle = NewVehicle();
        var tripStart = new DateTime(2026, 8, 1, 9, 0, 0);
        var tripEnd = new DateTime(2026, 8, 1, 11, 0, 0);
        var unavailabilityEnd = new DateTime(2026, 8, 2, 0, 0, 0);
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(new DateTime(2026, 8, 1, 0, 0, 0), unavailabilityEnd),
            type: VehicleUnavailabilityType.TechnicalInspection));

        var result = _sut.CheckEligibility(vehicle, TripWindow(tripStart, tripEnd), requiredSeats: 10);

        Assert.False(result.IsEligible);
        var violation = Assert.Single(result.Violations);
        Assert.Equal("VehicleEligibility.Unavailable.TechnicalInspection", violation.RuleId);
        Assert.Equal("Vehículo no disponible (ITV) del 01/08/2026 02:00 al 02/08/2026 02:00", violation.Message.Es());
    }

    [Theory]
    [InlineData(VehicleUnavailabilityType.Maintenance, "VehicleEligibility.Unavailable.Maintenance", "mantenimiento")]
    [InlineData(VehicleUnavailabilityType.TechnicalInspection, "VehicleEligibility.Unavailable.TechnicalInspection", "ITV")]
    [InlineData(VehicleUnavailabilityType.Repair, "VehicleEligibility.Unavailable.Repair", "reparación en taller")]
    [InlineData(VehicleUnavailabilityType.Cleaning, "VehicleEligibility.Unavailable.Cleaning", "limpieza")]
    [InlineData(VehicleUnavailabilityType.Administrative, "VehicleEligibility.Unavailable.Administrative", "gestión administrativa")]
    [InlineData(VehicleUnavailabilityType.Other, "VehicleEligibility.Unavailable.Other", "otra indisponibilidad")]
    public void CheckEligibility_EachUnavailabilityType_ProducesExpectedRuleIdAndHumanReadableMessage(
        VehicleUnavailabilityType type, string expectedRuleId, string expectedHumanText)
    {
        var vehicle = NewVehicle();
        var tripStart = new DateTime(2026, 9, 1, 8, 0, 0);
        var tripEnd = new DateTime(2026, 9, 1, 10, 0, 0);
        var unavailabilityEnd = new DateTime(2026, 9, 2, 0, 0, 0);
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(new DateTime(2026, 9, 1, 0, 0, 0), unavailabilityEnd),
            type: type));

        var result = _sut.CheckEligibility(vehicle, TripWindow(tripStart, tripEnd), requiredSeats: 10);

        Assert.False(result.IsEligible);
        var violation = Assert.Single(result.Violations);
        Assert.Equal(expectedRuleId, violation.RuleId);
        Assert.Equal(ViolationSeverity.Critical, violation.Severity);
        Assert.Equal($"Vehículo no disponible ({expectedHumanText}) del 01/09/2026 02:00 al 02/09/2026 02:00", violation.Message.Es());
    }

    [Fact]
    public void CheckEligibility_UnavailabilityEndingExactlyWhenTripStarts_DoesNotOverlap_IsEligible()
    {
        // Ends exactly at the trip start: adjacent, not overlapping.
        var vehicle = NewVehicle();
        var tripStart = new DateTime(2026, 7, 10, 8, 0, 0);
        var tripEnd = new DateTime(2026, 7, 10, 12, 0, 0);
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(new DateTime(2026, 7, 9, 0, 0, 0), tripStart),
            type: VehicleUnavailabilityType.Maintenance));

        var result = _sut.CheckEligibility(vehicle, TripWindow(tripStart, tripEnd), requiredSeats: 10);

        Assert.True(result.IsEligible);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void CheckEligibility_UnavailabilityStartingExactlyWhenTripEnds_DoesNotOverlap_IsEligible()
    {
        // Starts exactly at the trip end: adjacent, not overlapping.
        var vehicle = NewVehicle();
        var tripStart = new DateTime(2026, 7, 10, 8, 0, 0);
        var tripEnd = new DateTime(2026, 7, 10, 12, 0, 0);
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(tripEnd, new DateTime(2026, 7, 11, 0, 0, 0)),
            type: VehicleUnavailabilityType.Cleaning));

        var result = _sut.CheckEligibility(vehicle, TripWindow(tripStart, tripEnd), requiredSeats: 10);

        Assert.True(result.IsEligible);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void CheckEligibility_InsufficientCapacityAndOverlappingMaintenance_BothViolationsReported()
    {
        // Too small AND under maintenance during the trip.
        var vehicle = NewVehicle(seatingCapacity: 30);
        var tripStart = new DateTime(2026, 7, 10, 8, 0, 0);
        var tripEnd = new DateTime(2026, 7, 10, 12, 0, 0);
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(new DateTime(2026, 7, 9), new DateTime(2026, 7, 11)),
            type: VehicleUnavailabilityType.Maintenance));

        var result = _sut.CheckEligibility(vehicle, TripWindow(tripStart, tripEnd), requiredSeats: 40);

        // BOTH violations must be present, not only the first one.
        Assert.False(result.IsEligible);
        Assert.Equal(2, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.RuleId == "VehicleEligibility.InsufficientCapacity");
        Assert.Contains(result.Violations, v => v.RuleId == "VehicleEligibility.Unavailable.Maintenance");
        Assert.All(result.Violations, v => Assert.Equal(ViolationSeverity.Critical, v.Severity));
    }

    [Fact]
    public void CheckEligibility_DecommissionedAndInsufficientCapacityAndOverlapping_AllThreeViolationsReported()
    {
        var vehicle = NewVehicle(seatingCapacity: 10);
        vehicle.Decommission(new DateTime(2026, 6, 20));
        var tripStart = new DateTime(2026, 7, 10, 8, 0, 0);
        var tripEnd = new DateTime(2026, 7, 10, 12, 0, 0);
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(new DateTime(2026, 7, 9), new DateTime(2026, 7, 11)),
            type: VehicleUnavailabilityType.Repair));

        var result = _sut.CheckEligibility(vehicle, TripWindow(tripStart, tripEnd), requiredSeats: 50);

        Assert.False(result.IsEligible);
        Assert.Equal(3, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.RuleId == "VehicleEligibility.NotActive");
        Assert.Contains(result.Violations, v => v.RuleId == "VehicleEligibility.InsufficientCapacity");
        Assert.Contains(result.Violations, v => v.RuleId == "VehicleEligibility.Unavailable.Repair");
    }

    [Fact]
    public void CheckEligibility_MultipleOverlappingUnavailabilities_EachReportedSeparately()
    {
        var vehicle = NewVehicle();
        var tripStart = new DateTime(2026, 7, 10, 8, 0, 0);
        var tripEnd = new DateTime(2026, 7, 10, 20, 0, 0);
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(new DateTime(2026, 7, 10, 6, 0, 0), new DateTime(2026, 7, 10, 10, 0, 0)),
            type: VehicleUnavailabilityType.Cleaning));
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(new DateTime(2026, 7, 10, 18, 0, 0), new DateTime(2026, 7, 10, 22, 0, 0)),
            type: VehicleUnavailabilityType.Administrative));

        var result = _sut.CheckEligibility(vehicle, TripWindow(tripStart, tripEnd), requiredSeats: 10);

        Assert.False(result.IsEligible);
        Assert.Equal(2, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.RuleId == "VehicleEligibility.Unavailable.Cleaning");
        Assert.Contains(result.Violations, v => v.RuleId == "VehicleEligibility.Unavailable.Administrative");
    }

    [Fact]
    public void CheckEligibility_ActiveVehicle_SufficientCapacity_NoOverlappingUnavailability_IsEligible()
    {
        var vehicle = NewVehicle(seatingCapacity: 50);
        var tripStart = new DateTime(2026, 7, 10, 8, 0, 0);
        var tripEnd = new DateTime(2026, 7, 10, 12, 0, 0);
        // Far outside the trip window.
        vehicle.AddUnavailability(new VehicleUnavailability(
            vehicleId: 1,
            period: new TimeWindow(new DateTime(2026, 1, 1), new DateTime(2026, 1, 2)),
            type: VehicleUnavailabilityType.Maintenance));

        var result = _sut.CheckEligibility(vehicle, TripWindow(tripStart, tripEnd), requiredSeats: 40);

        Assert.True(result.IsEligible);
        Assert.Empty(result.Violations);
    }
}
