using DispatchGuard.Application.Services.Engine;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Exceptions;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// Forcing a CE 561/2006 rejection with a reason (SPEC 0056). Compliance is faked (the rule engine is covered elsewhere);
/// persistence, eligibility and conflicts are real.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public class ForcedAllocationIntegrationTests
{
    private const string Reason = "Único conductor con carné D disponible";
    private readonly PostgreSqlFixture _db;

    public ForcedAllocationIntegrationTests(PostgreSqlFixture db) => _db = db;

    [Fact]
    public async Task AllocateDriver_ForcedComplianceRejection_AssignsMarksTripAndAuditsRules()
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(_db, new DateTime(2036, 3, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: false);

        await using (var context = _db.CreateContext())
        {
            var (service, uow) = Create(context, compliant: false);
            var result = await service.AllocateDriver(tripId, driverId, vehicleId, default, "otro", AllocationOverride.Create(Reason, "Lucía"));
            Assert.True(result.Forced);
            Assert.False(result.Validation.IsCompliant);
            // The explanation says it was forced and keeps the failed rule.
            Assert.StartsWith("Asignación FORZADA por Lucía", result.Explanation.Summary.Es());
            Assert.Contains(result.Explanation.Business, i => i.Outcome == ExplanationOutcome.Failed);
            Assert.Equal("Trip.ForceAssign", result.Explanation.Flow[^1].Component);
        }

        await using var read = _db.CreateContext();
        var trip = await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId);
        Assert.Equal(driverId, trip.AssignedDriverId);
        Assert.Equal(TripStatus.Assigned, trip.Status);
        Assert.True(trip.IsForced);
        Assert.Equal(Reason, trip.ForcedReason);
        Assert.Equal("Lucía", trip.ForcedBy);

        var attempt = await read.AllocationAttempts.AsNoTracking().SingleAsync(a => a.TripId == tripId);
        Assert.Equal(AllocationOutcome.Forced, attempt.Outcome);
        Assert.False(attempt.IsRejected);
        Assert.Equal("Lucía", attempt.AttemptedBy);
        Assert.Contains(Reason, attempt.Detail);
        Assert.Contains("Pausa insuficiente", attempt.Detail);
        // The skipped rule as a catalog id, the reason and the explanation as the dispatcher saw it (SPEC 0059).
        Assert.Equal(["continuous"], attempt.RuleIds);
        Assert.Equal(Reason, attempt.ForcedReason);
        Assert.StartsWith("Asignación FORZADA por Lucía", AttemptTexts.Of(attempt).Explanation!.Summary.Es());

        // The Control Center shows it as a warning and counts it apart from the blocked ones.
        var control = new ControlCenterService(new ControlCenterReadModel(read), new DriverHoursService(new DrivingRecordRepository(read)));
        var start = new DateTime(2036, 3, 6, 8, 0, 0, DateTimeKind.Utc);
        var alerts = await control.AlertsAsync(trip.BaseId, start.Date, start.AddHours(-3), 1, 100);
        var alert = Assert.Single(alerts.Items, a => a.Type == ControlAlertType.ForcedAssignment && a.EntityId == tripId);
        Assert.Equal(ControlAlertSeverity.Warning, alert.Severity);
        Assert.Contains(Reason, alert.Detail.Es());
        var metrics = await control.EngineMetricsAsync(trip.BaseId, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1));
        Assert.True(metrics.Forced >= 1);
        Assert.False(metrics.BlockedByReason.ContainsKey(nameof(AllocationOutcome.Forced)));
    }

    [Fact]
    public async Task AllocateDriver_WithoutOverride_ComplianceStillBlocks()
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(_db, new DateTime(2036, 4, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: false);

        await using (var context = _db.CreateContext())
        {
            var (service, uow) = Create(context, compliant: false);
            await Assert.ThrowsAsync<ComplianceException>(async () => await service.AllocateDriver(tripId, driverId, vehicleId));
        }

        await using var read = _db.CreateContext();
        Assert.Null((await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId)).AssignedDriverId);
        Assert.Equal(AllocationOutcome.RejectedCompliance, (await read.AllocationAttempts.AsNoTracking().SingleAsync(a => a.TripId == tripId)).Outcome);
    }

    [Fact]
    public async Task AllocateDriver_ForcedButVehicleIneligible_ThrowsAndPersistsNothing()
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(_db, new DateTime(2036, 5, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: true);

        await using (var context = _db.CreateContext())
        {
            var (service, uow) = Create(context, compliant: false);
            await Assert.ThrowsAsync<VehicleIneligibleException>(async () => await service.AllocateDriver(tripId, driverId, vehicleId, default, null, AllocationOverride.Create(Reason, "Lucía")));
        }

        await using var read = _db.CreateContext();
        var trip = await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId);
        Assert.Null(trip.AssignedDriverId);
        Assert.False(trip.IsForced);
        Assert.Equal(AllocationOutcome.RejectedVehicle, (await read.AllocationAttempts.AsNoTracking().SingleAsync(a => a.TripId == tripId)).Outcome);
    }

    [Fact]
    public async Task AllocateDriver_OverrideOnLegalTrip_AssignsWithoutMark_AndNormalReassignClearsMark()
    {
        var (driverId, vehicleId, tripId) = await DriverAllocationIntegrationTests.SeedForLogAsync(_db, new DateTime(2036, 6, 6, 8, 0, 0, DateTimeKind.Utc), unavailableVehicle: false);

        // Forced first (fake non-compliant), then a normal reassignment of the same trip clears the mark.
        await using (var context = _db.CreateContext())
        {
            var (service, uow) = Create(context, compliant: false);
            await service.AllocateDriver(tripId, driverId, vehicleId, default, null, AllocationOverride.Create(Reason, "Lucía"));
        }
        await using (var context = _db.CreateContext())
        {
            var (service, uow) = Create(context, compliant: true);
            var result = await service.AllocateDriver(tripId, driverId, vehicleId, default, null, AllocationOverride.Create(Reason, "Lucía"));
            Assert.False(result.Forced); // Legal: a normal assignment even with the flag.
        }

        await using var read = _db.CreateContext();
        var trip = await read.Trips.AsNoTracking().SingleAsync(t => t.Id == tripId);
        Assert.False(trip.IsForced);
        Assert.Null(trip.ForcedReason);
        Assert.Null(trip.ForcedBy);
        Assert.Equal(TripStatus.Assigned, trip.Status);
    }

    private static (DriverAllocationService, UnitOfWork) Create(DispatchGuardDbContext context, bool compliant)
    {
        var uow = new UnitOfWork(context);
        return (new DriverAllocationService(
            new FakeLegal(compliant),
            new VehicleEligibilityService(),
            new ScheduleConflictService(new TripRepository(context)),
            uow, new AllocationAttemptLog(context)), uow);
    }

    private sealed class FakeLegal(bool compliant) : ILegalValidationService
    {
        public async Task<LegalValidation> ValidateDriverForTrip(Driver driver, Trip trip, CancellationToken cancellationToken = default) =>
            (await ValidateDriverForTripDetailed(driver, trip, cancellationToken)).ToSnapshot();

        public Task<ComplianceCheckResult> ValidateDriverForTripDetailed(Driver driver, Trip trip, CancellationToken cancellationToken = default) =>
            Task.FromResult(compliant
                ? ComplianceCheckResult.Compliant(TimeSpan.FromHours(9), TimeSpan.FromHours(56), TimeSpan.FromHours(90))
                : new ComplianceCheckResult(false, [ComplianceViolation.Critical("CE_561_2006.ContinuousDriving.4_5hours", Texts.AsWritten("Pausa insuficiente"))], TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero));
    }
}
