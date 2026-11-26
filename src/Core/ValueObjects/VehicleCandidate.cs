using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.ValueObjects;

public enum VehiclePositionSource
{
    /// <summary>Of its latest non-cancelled trip ending before the trip starts.</summary>
    LastTripDestination = 0,

    /// <summary>No recent trip: its declared depot.</summary>
    HomeBase = 1,

    /// <summary>Ranks last.</summary>
    Unknown = 2
}

/// <summary>
/// A vehicle that passed every hard filter for a trip. <see cref="Proximity"/> reuses the driver tiers measured from the vehicle's
/// position (SPEC 0032); <see cref="CandidateProximity.NoHomeLocation"/> means "position unknown".
/// </summary>
public sealed class VehicleCandidate
{
    public Vehicle Vehicle { get; }

    /// <summary>1-based.</summary>
    public int Rank { get; }

    public CandidateProximity Proximity { get; }

    public VehiclePositionSource PositionSource { get; }

    /// <summary>Straight line from the vehicle's position to the trip origin; null if unknown.</summary>
    public double? DistanceKm { get; }

    /// <summary>For the dispatcher.</summary>
    public LocalizedText Reason { get; }

    /// <summary>Null if unknown.</summary>
    public int? RequiredSeats { get; }

    public VehicleCandidate(
        Vehicle vehicle,
        int rank,
        CandidateProximity proximity,
        VehiclePositionSource positionSource,
        double? distanceKm,
        LocalizedText reason,
        int? requiredSeats = null)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);
        ArgumentNullException.ThrowIfNull(reason);

        Vehicle = vehicle;
        Rank = rank;
        Proximity = proximity;
        PositionSource = positionSource;
        DistanceKm = distanceKm;
        Reason = reason;
        RequiredSeats = requiredSeats;
    }
}
