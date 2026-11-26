namespace DispatchGuard.Core.ValueObjects;

/// <summary>A legal driver or an eligible vehicle for a service, in the order of its search.</summary>
public sealed record ReachCandidate(int Id, ServiceReach Reach, bool SameBooking = false);

/// <summary>Valid resources of one kind, and why those that cannot be recommended are left out (SPEC 0062 R5).</summary>
public sealed record ReachCounts(int Valid, int Farther, int Late, int UnknownPosition)
{
    public static ReachCounts Of(IReadOnlyCollection<ReachCandidate> candidates) => new(
        candidates.Count,
        candidates.Count(c => !c.SameBooking && c.Reach is { Arrives: true, DistanceKm: not null, StepKm: null }),
        candidates.Count(c => !c.Reach.Arrives),
        candidates.Count(c => !c.SameBooking && c.Reach is { Arrives: true, DistanceKm: null }));
}

/// <summary>
/// Driver and vehicle of a service: the smallest step with a valid pair that arrives and, inside the step, the search's own
/// order, the one of the manual panel. The only place that decides it (SPEC 0062 R5–R7). The driver or vehicle of the same
/// booking goes first and has no distance limit, each on its own.
/// </summary>
public sealed record RecommendationPick(ReachCandidate Driver, ReachCandidate Vehicle, double StepKm)
{
    public static RecommendationPick? Choose(IReadOnlyList<ReachCandidate> drivers, IReadOnlyList<ReachCandidate> vehicles)
    {
        ArgumentNullException.ThrowIfNull(drivers);
        ArgumentNullException.ThrowIfNull(vehicles);
        foreach (var step in RecommendationSteps.Km)
        {
            if (First(drivers, step) is { } driver && First(vehicles, step) is { } vehicle)
            {
                return new RecommendationPick(driver, vehicle, step);
            }
        }

        return null;
    }

    private static ReachCandidate? First(IReadOnlyList<ReachCandidate> candidates, double step) =>
        candidates.FirstOrDefault(c => c.SameBooking && c.Reach.Arrives)
        ?? candidates.FirstOrDefault(c => c.Reach.Arrives && c.Reach.StepKm <= step);
}
