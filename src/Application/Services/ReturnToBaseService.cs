using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services;

/// <summary>
/// Return-to-base rule (Amendment 2020/1054, SPEC 0016): only says whether the driver returned to, or departed from, the home
/// location within the last 4 weeks. Blocking or alerting is up to the consumer.
/// </summary>
public interface IReturnToBaseService
{
    /// <summary>At least one completed trip to or from the home location in the 28 days before <paramref name="asOf"/>.</summary>
    Task<ReturnToBaseCheck> CheckCompliance(int driverId, DateTime asOf, CancellationToken cancellationToken = default);
}

public sealed class ReturnToBaseService : IReturnToBaseService
{
    /// <summary>EU 2020/1054 (CE 561/2006 Art. 8.8a): the driver must return to base within 4 weeks.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(28);

    private readonly IDriverRepository _driverRepository;
    private readonly ITripRepository _tripRepository;

    public ReturnToBaseService(IDriverRepository driverRepository, ITripRepository tripRepository)
    {
        _driverRepository = driverRepository ?? throw new ArgumentNullException(nameof(driverRepository));
        _tripRepository = tripRepository ?? throw new ArgumentNullException(nameof(tripRepository));
    }

    public async Task<ReturnToBaseCheck> CheckCompliance(int driverId, DateTime asOf, CancellationToken cancellationToken = default)
    {
        var driver = await _driverRepository.GetByIdAsync(driverId, cancellationToken);
        if (driver?.HomeLocationId is not int homeLocationId)
        {
            return ReturnToBaseCheck.NotVerifiable();
        }

        var from = asOf - Window;
        var paged = await _tripRepository.GetByDriverAsync(driverId, from, asOf, page: 1, pageSize: int.MaxValue, excludeTripId: null, cancellationToken);

        var returnTrips = paged.Items
            .Where(t => t.Status == TripStatus.Completed)
            .Where(t => t.ScheduledWindow.Start >= from && t.ScheduledWindow.Start <= asOf)
            // Arriving at the home location and departing from it both count as a return: the regulation does not distinguish between
            // the two.
            .Where(t => t.OriginLocationId == homeLocationId || t.DestinationLocationId == homeLocationId)
            .ToList();

        if (returnTrips.Count == 0)
        {
            return ReturnToBaseCheck.NonCompliant(lastReturnDate: null, daysSinceLastReturn: null);
        }

        var lastReturn = returnTrips.Max(t => t.ScheduledWindow.Start);
        var daysSince = (int)(asOf.Date - lastReturn.Date).TotalDays;
        return ReturnToBaseCheck.Compliant(lastReturn, daysSince);
    }
}
