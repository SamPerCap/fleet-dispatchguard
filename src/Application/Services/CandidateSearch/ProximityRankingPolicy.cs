using DispatchGuard.Core.Entities;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.CandidateSearch;

public sealed record DriverRanking(CandidateProximity Proximity, double? DistanceKm, LocalizedText Reason);

/// <summary>
/// Ranking never excludes: that is the job of the hard filters. Pluggable because a tenant may reorder the priorities in the
/// future (SPEC 0031).
/// </summary>
public interface IDriverRankingPolicy
{
    DriverRanking Rank(Location? driverHome, Location tripOrigin, Location tripDestination);
}

/// <summary>
/// (1) base = origin and = destination (2) base = origin (3) straight-line distance in progressive bands of 5/10/20/40 km.
/// Beyond 40 km, then without base, go last (SPEC 0031 decision A). Road distance arrives with SPEC 0033.
/// </summary>
public sealed class ProximityRankingPolicy : IDriverRankingPolicy
{
    /// <summary>Upper bounds, inclusive.</summary>
    public static readonly IReadOnlyList<(double MaxKm, CandidateProximity Proximity)> RadiusBands =
    [
        (5, CandidateProximity.Within5Km),
        (10, CandidateProximity.Within10Km),
        (20, CandidateProximity.Within20Km),
        (40, CandidateProximity.Within40Km)
    ];

    public DriverRanking Rank(Location? driverHome, Location tripOrigin, Location tripDestination)
    {
        ArgumentNullException.ThrowIfNull(tripOrigin);
        ArgumentNullException.ThrowIfNull(tripDestination);

        if (driverHome is null)
        {
            return new DriverRanking(CandidateProximity.NoHomeLocation, null, Texts.NoBaseReason());
        }

        var distance = driverHome.Coordinate.DistanceTo(tripOrigin.Coordinate);

        if (driverHome.Id == tripOrigin.Id)
        {
            return driverHome.Id == tripDestination.Id
                ? new DriverRanking(CandidateProximity.BaseAtOriginAndDestination, distance, Texts.BaseIsBothReason(driverHome.Name))
                : new DriverRanking(CandidateProximity.BaseAtOrigin, distance, Texts.BaseIsOriginReason(driverHome.Name));
        }

        foreach (var (maxKm, proximity) in RadiusBands)
        {
            if (distance <= maxKm)
            {
                return new DriverRanking(proximity, distance, Texts.BaseWithinReason(distance, maxKm));
            }
        }

        return new DriverRanking(CandidateProximity.Beyond40Km, distance, Texts.BaseBeyondReason(distance, RadiusBands[^1].MaxKm));
    }
}
