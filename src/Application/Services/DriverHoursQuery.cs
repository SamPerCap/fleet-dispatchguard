using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Core.Interfaces.Repositories;

namespace DispatchGuard.Application.Services;

public interface IDriverHoursQuery
{
    Task<DriverHoursDto> GetAsync(int driverId, DateTime asOf, CancellationToken cancellationToken = default);
}

public sealed class DriverHoursQuery : IDriverHoursQuery
{
    private readonly IDriverRepository _drivers;
    private readonly IDriverHoursService _hours;

    public DriverHoursQuery(IDriverRepository drivers, IDriverHoursService hours)
    {
        _drivers = drivers ?? throw new ArgumentNullException(nameof(drivers));
        _hours = hours ?? throw new ArgumentNullException(nameof(hours));
    }

    public async Task<DriverHoursDto> GetAsync(int driverId, DateTime asOf, CancellationToken cancellationToken = default)
    {
        // Measuring an unknown driver would not fail: without trips it answers zero hours.
        _ = await _drivers.GetByIdAsync(driverId, cancellationToken)
            ?? throw MissingResource.Driver(driverId);

        var hours = await _hours.MeasureAsync(driverId, asOf, cancellationToken);
        return DriverHoursDto.From(driverId, asOf, hours);
    }
}
