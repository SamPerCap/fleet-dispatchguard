using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services;

/// <summary>
/// Marks a forced service leaves on the later ones it makes illegal. A mark only lives while the service that caused it keeps
/// its driver and is not cancelled (SPEC 0063 R8).
/// </summary>
public interface IForcedConsequences
{
    /// <summary>Does not save.</summary>
    Task MarkAsync(Trip source, LaterServicesCheck later, AllocationOverride force, CancellationToken cancellationToken = default);

    /// <summary>The same driver was forced again: the marks of <paramref name="source"/> take the new reason. Does not save.</summary>
    Task RenewAsync(Trip source, AllocationOverride force, CancellationToken cancellationToken = default);

    /// <summary>Does not save; pass the result to <see cref="RevalidateAsync"/> after saving.</summary>
    Task<IReadOnlyList<Trip>> ReleaseAsync(IReadOnlyCollection<int> sourceTripIds, CancellationToken cancellationToken = default);

    /// <summary>Against the saved calendar. Saves.</summary>
    Task RevalidateAsync(IReadOnlyList<Trip> released, CancellationToken cancellationToken = default);
}

public sealed class ForcedConsequences : IForcedConsequences
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILegalValidationService _legal;

    public ForcedConsequences(IUnitOfWork unitOfWork, ILegalValidationService legal)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _legal = legal ?? throw new ArgumentNullException(nameof(legal));
    }

    public async Task MarkAsync(Trip source, LaterServicesCheck later, AllocationOverride force, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(later);

        foreach (var id in later.AffectedTripIds)
        {
            var trip = await _unitOfWork.Trips.GetByIdAsync(id, cancellationToken);
            if (trip is null)
            {
                continue;
            }

            trip.MarkForcedBy(source.Id, force, later.Results[id].ToSnapshot());
            await _unitOfWork.Trips.UpdateAsync(trip, cancellationToken);
        }
    }

    public async Task RenewAsync(Trip source, AllocationOverride force, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        foreach (var trip in await _unitOfWork.Trips.GetForcedByTripsAsync([source.Id], cancellationToken))
        {
            trip.MarkForcedBy(source.Id, force, trip.LegalValidation!);
            await _unitOfWork.Trips.UpdateAsync(trip, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Trip>> ReleaseAsync(IReadOnlyCollection<int> sourceTripIds, CancellationToken cancellationToken = default)
    {
        var marked = await _unitOfWork.Trips.GetForcedByTripsAsync(sourceTripIds, cancellationToken);
        foreach (var trip in marked)
        {
            foreach (var id in sourceTripIds)
            {
                trip.ReleaseForcedBy(id);
            }

            await _unitOfWork.Trips.UpdateAsync(trip, cancellationToken);
        }

        return marked;
    }

    public async Task RevalidateAsync(IReadOnlyList<Trip> released, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(released);

        var any = false;
        foreach (var trip in released.Where(t => t.IsActive && t.AssignedDriverId is not null))
        {
            var driver = await _unitOfWork.Drivers.GetByIdAsync(trip.AssignedDriverId!.Value, cancellationToken);
            if (driver is null)
            {
                continue;
            }

            trip.RefreshLegalValidation(await _legal.ValidateDriverForTrip(driver, trip, cancellationToken));
            await _unitOfWork.Trips.UpdateAsync(trip, cancellationToken);
            any = true;
        }

        if (any)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
