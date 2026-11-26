using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services;

/// <summary>Reads the driving history and leaves every CE 561/2006 rule to <see cref="LegalComplianceEvaluation"/>.</summary>
public interface ILegalValidationService
{
    /// <summary>The snapshot to persist on the trip.</summary>
    Task<LegalValidation> ValidateDriverForTrip(
        Driver driver,
        Trip trip,
        CancellationToken cancellationToken = default);

    /// <summary>With structured violations and remaining-time metrics.</summary>
    Task<ComplianceCheckResult> ValidateDriverForTripDetailed(
        Driver driver,
        Trip trip,
        CancellationToken cancellationToken = default);
}

public sealed class LegalValidationService : ILegalValidationService
{
    /// <summary>
    /// The later services that see the trip (the engine's lookback after its end), plus what each of them sees ahead.
    /// The extra day covers the duration of the last one (SPEC 0063).
    /// </summary>
    private static readonly TimeSpan LaterServicesWindow =
        LegalComplianceEvaluation.LookbackWindow + LegalComplianceEvaluation.LookaheadWindow + TimeSpan.FromDays(1);

    /// <summary>Read to decide about a service; anything that copies the calendar must cover it (SPEC 0062).</summary>
    public static (DateTime From, DateTime To) CalendarFor(TimeWindow window) =>
        (window.Start - LegalComplianceEvaluation.LookbackWindow, window.End + LaterServicesWindow);

    private readonly IDrivingRecordRepository _drivingRecordRepository;

    public LegalValidationService(IDrivingRecordRepository drivingRecordRepository)
    {
        _drivingRecordRepository = drivingRecordRepository
            ?? throw new ArgumentNullException(nameof(drivingRecordRepository));
    }

    public async Task<LegalValidation> ValidateDriverForTrip(
        Driver driver,
        Trip trip,
        CancellationToken cancellationToken = default)
    {
        var result = await ValidateDriverForTripDetailed(driver, trip, cancellationToken);
        return result.ToSnapshot();
    }

    public async Task<ComplianceCheckResult> ValidateDriverForTripDetailed(
        Driver driver,
        Trip trip,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(trip);

        var tripStart = trip.ScheduledWindow.Start;
        var tripDuration = trip.ScheduledWindow.Duration;

        // One calendar for both questions: is the trip legal, and do the later services stay legal (SPEC 0063)?
        var (from, to) = CalendarFor(trip.ScheduledWindow);
        var calendar = await _drivingRecordRepository.GetRecordsAsync(driver.Id, from, to, cancellationToken);

        // The trip itself only sees ahead what its daily-rest window needs (SPEC 0041).
        var visible = calendar.Where(p => p.Start < trip.ScheduledWindow.End + LegalComplianceEvaluation.LookaheadWindow).ToList();

        var own = LegalComplianceEvaluation.Validate(visible, tripStart, tripDuration);
        var later = LaterServicesEvaluation.Evaluate(
            calendar, new DrivingPeriod(driver.Id, tripStart, trip.ScheduledWindow.End, trip.Id));
        return own.WithLaterServices(later);
    }
}
