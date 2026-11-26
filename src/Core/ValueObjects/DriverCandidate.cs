using DispatchGuard.Core.Entities;

namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// Distance from the driver's home base to a trip; the lower value ranks better. Drivers beyond the 40 km radius or without a
/// base are never excluded, they rank last (SPEC 0031 decision A).
/// </summary>
public enum CandidateProximity
{
    BaseAtOriginAndDestination = 0,
    BaseAtOrigin = 1,
    Within5Km = 2,
    Within10Km = 3,
    Within20Km = 4,
    Within40Km = 5,
    Beyond40Km = 6,
    NoHomeLocation = 7
}

/// <summary>A driver that passed every hard filter for a trip.</summary>
public sealed class DriverCandidate
{
    public Driver Driver { get; }

    /// <summary>1-based.</summary>
    public int Rank { get; }

    public CandidateProximity Proximity { get; }

    /// <summary>Straight line from the driver's base to the trip origin; null without base.</summary>
    public double? DistanceKm { get; }

    /// <summary>For the dispatcher.</summary>
    public LocalizedText Reason { get; }

    /// <summary>Non-blocking, e.g. weekly rest not verifiable (SPEC 0040).</summary>
    public IReadOnlyCollection<ComplianceViolation> Warnings { get; }

    /// <summary>Null if not available.</summary>
    public ComplianceCheckResult? Compliance { get; }

    /// <summary>
    /// Already drives another non-cancelled trip of the same booking: ranks first, but only after passing every hard filter. A
    /// preference, never a filter (SPEC 0045 R3).
    /// </summary>
    public bool CoversSameBooking { get; }

    /// <summary>Driving since the last weekly rest, at the trip start; null if not measured.</summary>
    public TimeSpan? HoursSinceWeeklyRest { get; init; }

    /// <summary>Lowest id of the other trips of the same booking this driver already covers.</summary>
    public int? SameBookingTripId { get; init; }

    /// <summary>
    /// Ranks ahead of another listed candidate tied on booking, band and distance only because it has fewer hours since its last
    /// weekly rest (CE 561/2006 Art. 8.6, SPEC 0047 R2). A tie-break, never a filter.
    /// </summary>
    public bool WonTieBreak { get; init; }

    public DriverCandidate(
        Driver driver,
        int rank,
        CandidateProximity proximity,
        double? distanceKm,
        LocalizedText reason,
        IReadOnlyCollection<ComplianceViolation> warnings,
        ComplianceCheckResult? compliance = null,
        bool coversSameBooking = false)
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentOutOfRangeException.ThrowIfLessThan(rank, 1);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(warnings);

        Driver = driver;
        Rank = rank;
        Proximity = proximity;
        DistanceKm = distanceKm;
        Reason = reason;
        Warnings = warnings;
        Compliance = compliance;
        CoversSameBooking = coversSameBooking;
    }
}

public enum DiscardCause
{
    NotEligible = 0,
    Unavailable = 1,
    Overlap = 2,
    NoAvailability = 3,
    NotCompliant = 4
}

/// <summary>
/// The first hard filter that excluded the driver: <see cref="Cause"/> for the code, <see cref="Reason"/> for people.
/// <see cref="Broken"/>: the rules of CE 561/2006, when that is the cause.
/// </summary>
public sealed record DiscardedDriver(Driver Driver, DiscardCause Cause, LocalizedText Reason, LocalizedText? Broken = null);

/// <summary>Candidates, best first.</summary>
public sealed record DriverSearchOutcome(IReadOnlyList<DriverCandidate> Candidates, IReadOnlyList<DiscardedDriver> Discarded);
