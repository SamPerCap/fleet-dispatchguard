using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services;

/// <summary>
/// Driving in the current working day, this week, two natural weeks and since the last weekly rest (SPEC 0047 R1). Same source
/// and lookback as <see cref="LegalValidationService"/>, so that both give the same figures.
/// </summary>
public interface IDriverHoursService
{
    Task<DriverHours> MeasureAsync(int driverId, DateTime asOf, CancellationToken cancellationToken = default);
}

public sealed class DriverHoursService : IDriverHoursService
{
    /// <summary>Two natural weeks, as the compliance check (Art. 6.3, SPEC 0064).</summary>
    public static readonly TimeSpan LookbackWindow = LegalComplianceEvaluation.LookbackWindow;

    private readonly IDrivingRecordRepository _drivingRecordRepository;

    public DriverHoursService(IDrivingRecordRepository drivingRecordRepository)
    {
        _drivingRecordRepository = drivingRecordRepository ?? throw new ArgumentNullException(nameof(drivingRecordRepository));
    }

    public async Task<DriverHours> MeasureAsync(int driverId, DateTime asOf, CancellationToken cancellationToken = default)
    {
        var history = await _drivingRecordRepository.GetRecordsAsync(driverId, asOf - LookbackWindow, asOf, cancellationToken);
        return LegalComplianceEvaluation.MeasureHours(history, asOf);
    }
}
