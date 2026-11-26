using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.CandidateSearch;

/// <summary>
/// Only filters and ranks; assigning stays in <see cref="DriverAllocationService"/>. Drivers already covering another trip of
/// the same booking rank first: a preference, not a filter (SPEC 0045 R3).
/// </summary>
public interface IDriverCandidateSearch
{
    /// <summary>
    /// Hard filters: active, auto-assignable, not external, not the driver currently assigned to the trip, no overlapping
    /// unavailability, no overlapping trip, enough daily availability (SPEC 0030) and CE 561/2006 compliance. Returns an empty list
    /// (never throws) when nobody qualifies.
    /// </summary>
    Task<IReadOnlyList<DriverCandidate>> FindCandidates(Trip trip, int maxResults = DriverCandidateSearch.DefaultMaxResults, CancellationToken cancellationToken = default);

    /// <exception cref="KeyNotFoundException">Unknown trip.</exception>
    Task<IReadOnlyList<DriverCandidate>> FindCandidatesForTrip(int tripId, int maxResults = DriverCandidateSearch.DefaultMaxResults, CancellationToken cancellationToken = default);

    /// <summary>Every legal candidate, best first, and every discarded driver with the first filter that excluded it (SPEC 0047 R4).</summary>
    Task<DriverSearchOutcome> FindOptions(Trip trip, CancellationToken cancellationToken = default);

    /// <exception cref="KeyNotFoundException">Unknown trip.</exception>
    Task<DriverSearchOutcome> FindOptionsForTrip(int tripId, CancellationToken cancellationToken = default);
}

public sealed class DriverCandidateSearch : IDriverCandidateSearch
{

    /// <summary>docs/PROJECT_DEFINITION.md §4.</summary>
    public const int DefaultMaxResults = 3;

    /// <summary>As shown in the explanations.</summary>
    public static readonly IReadOnlyList<LocalizedText> HardFilters =
    [
        Texts.FilterActive(),
        Texts.FilterNotAssigned(),
        Texts.FilterAvailable(),
        Texts.FilterNoOverlap(),
        Texts.FilterFitsDay(),
        Texts.FilterCompliant()
    ];

    private const int MaxTripsPerDay = 500;

    /// <summary>In the reason of a discarded driver.</summary>
    private const int MaxListedRules = 3;

    private readonly IDriverRepository _driverRepository;
    private readonly ITripRepository _tripRepository;
    private readonly ILegalValidationService _legalValidationService;
    private readonly ILocationRepository _locationRepository;
    private readonly IDriverRankingPolicy _rankingPolicy;
    private readonly IDriverHoursService? _hours;

    public DriverCandidateSearch(
        IDriverRepository driverRepository,
        ITripRepository tripRepository,
        ILegalValidationService legalValidationService,
        ILocationRepository locationRepository,
        IDriverRankingPolicy? rankingPolicy = null,
        IDriverHoursService? hours = null)
    {
        _hours = hours;
        _driverRepository = driverRepository ?? throw new ArgumentNullException(nameof(driverRepository));
        _tripRepository = tripRepository ?? throw new ArgumentNullException(nameof(tripRepository));
        _legalValidationService = legalValidationService ?? throw new ArgumentNullException(nameof(legalValidationService));
        _locationRepository = locationRepository ?? throw new ArgumentNullException(nameof(locationRepository));
        _rankingPolicy = rankingPolicy ?? new ProximityRankingPolicy();
    }

    public async Task<IReadOnlyList<DriverCandidate>> FindCandidatesForTrip(int tripId, int maxResults = DefaultMaxResults, CancellationToken cancellationToken = default)
    {
        var trip = await _tripRepository.GetByIdAsync(tripId, cancellationToken)
            ?? throw MissingResource.Trip(tripId);
        return await FindCandidates(trip, maxResults, cancellationToken);
    }

    public async Task<IReadOnlyList<DriverCandidate>> FindCandidates(Trip trip, int maxResults = DefaultMaxResults, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trip);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        return (await Search(trip, maxResults, collectDiscarded: false, cancellationToken)).Candidates;
    }

    public async Task<DriverSearchOutcome> FindOptionsForTrip(int tripId, CancellationToken cancellationToken = default)
    {
        var trip = await _tripRepository.GetByIdAsync(tripId, cancellationToken)
            ?? throw MissingResource.Trip(tripId);
        return await FindOptions(trip, cancellationToken);
    }

    public Task<DriverSearchOutcome> FindOptions(Trip trip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trip);
        return Search(trip, int.MaxValue, collectDiscarded: true, cancellationToken);
    }

    private async Task<DriverSearchOutcome> Search(Trip trip, int maxResults, bool collectDiscarded, CancellationToken cancellationToken)
    {
        var window = trip.ScheduledWindow;
        var locations = new Dictionary<int, Location?>();
        var origin = await ResolveLocation(trip.OriginLocationId, trip.OriginLocation, locations, cancellationToken);
        var destination = await ResolveLocation(trip.DestinationLocationId, trip.DestinationLocation, locations, cancellationToken);
        if (origin is null || destination is null)
        {
            throw new InvalidOperationException($"Trip {trip.Id} has no origin/destination location");
        }

        var discarded = new List<DiscardedDriver>();

        var drivers = new List<Driver>();
        foreach (var driver in await _driverRepository.GetAllAsync(cancellationToken))
        {
            if (driver.Id == trip.AssignedDriverId)
            {
                continue;
            }

            var exclusion = !driver.IsEligibleForAutoAssignment()
                ? new DiscardedDriver(driver, DiscardCause.NotEligible, Texts.DiscardedNotEligible())
                : DriverEligibilityCheck.For(driver, window) is { IsEligible: false } check
                    ? new DiscardedDriver(driver, DiscardCause.Unavailable, Texts.Listed(check.Violations.Select(v => v.Message).ToList()))
                    : null;
            if (exclusion is null) drivers.Add(driver);
            else if (collectDiscarded) discarded.Add(exclusion);
        }

        // Rank first (straight-line prefilter, SPEC 0033), so that the expensive checks run in rank order. Drivers of the same
        // booking go first but still face every hard filter below (SPEC 0045 R3). Ties on booking, band and distance go to fewer
        // hours since the last weekly rest (Art. 8.6, SPEC 0047 R2).
        var sameBooking = await DriversOfSameBooking(trip, cancellationToken);
        var ranked = new List<(Driver Driver, DriverRanking Ranking, TimeSpan? Hours)>(drivers.Count);
        foreach (var driver in drivers)
        {
            var home = driver.HomeLocationId is int homeId
                ? await ResolveLocation(homeId, driver.HomeLocation, locations, cancellationToken)
                : null;
            TimeSpan? hours = _hours is null ? null : (await _hours.MeasureAsync(driver.Id, window.Start, cancellationToken)).SinceLastWeeklyRest;
            ranked.Add((driver, _rankingPolicy.Rank(home, origin, destination), hours));
        }

        var ordered = ranked
            .OrderBy(r => sameBooking.ContainsKey(r.Driver.Id) ? 0 : 1)
            .ThenBy(r => r.Ranking.Proximity)
            .ThenBy(r => r.Ranking.DistanceKm ?? double.MaxValue)
            .ThenBy(r => r.Hours ?? TimeSpan.Zero)
            .ThenBy(r => r.Driver.Id); // Deterministic tie-break.

        // The expensive checks (schedule, availability, compliance) stop at N candidates.
        var found = new List<(Driver Driver, DriverRanking Ranking, TimeSpan? Hours, ComplianceCheckResult Compliance, int? CoveredTrip)>();
        foreach (var (driver, ranking, hours) in ordered)
        {
            var committed = await CommittedWork(driver.Id, trip, cancellationToken);
            if (committed.Any(w => w.OverlapsWith(window)))
            {
                if (collectDiscarded) discarded.Add(new DiscardedDriver(driver, DiscardCause.Overlap, Texts.DiscardedOverlap()));
                continue;
            }

            if (!driver.HasAvailabilityFor(window, committed))
            {
                if (collectDiscarded) discarded.Add(new DiscardedDriver(driver, DiscardCause.NoAvailability, Texts.DiscardedNoAvailability()));
                continue;
            }

            var compliance = await _legalValidationService.ValidateDriverForTripDetailed(driver, trip, cancellationToken);
            if (!compliance.IsCompliant)
            {
                if (collectDiscarded)
                {
                    // One later service may break several rules, so the list is cut; the full one is in the rejection (SPEC 0063).
                    var broken = compliance.Violations.Where(v => v.Severity != ViolationSeverity.Warning).Select(v => v.Message).ToList();
                    var listed = broken.Count > MaxListedRules
                        ? Texts.ListedAndMore(broken.Take(MaxListedRules).ToList(), broken.Count - MaxListedRules)
                        : Texts.Listed(broken);
                    discarded.Add(new DiscardedDriver(driver, DiscardCause.NotCompliant, Texts.DiscardedNotCompliant(listed), listed));
                }
                continue;
            }

            found.Add((driver, ranking, hours, compliance, sameBooking.TryGetValue(driver.Id, out var covered) ? covered : null));
            if (found.Count == maxResults)
            {
                break;
            }
        }

        var result = new List<DriverCandidate>(found.Count);
        for (var i = 0; i < found.Count; i++)
        {
            var (driver, ranking, hours, compliance, coveredTrip) = found[i];
            var covers = coveredTrip is not null;
            var wonTieBreak = hours is TimeSpan ownHours && found.Skip(i + 1).Any(o =>
                (o.CoveredTrip is not null) == covers && o.Ranking.Proximity == ranking.Proximity && o.Ranking.DistanceKm == ranking.DistanceKm
                && o.Hours is TimeSpan other && other > ownHours);
            var warnings = compliance.Violations.Where(v => v.Severity == ViolationSeverity.Warning).ToList().AsReadOnly();
            var reason = coveredTrip is int bookingTrip ? Texts.SameBookingReason(bookingTrip, ranking.Reason) : ranking.Reason;
            if (wonTieBreak)
            {
                reason = Texts.WonTieBreakReason(reason, hours!.Value.TotalHours);
            }

            result.Add(new DriverCandidate(driver, i + 1, ranking.Proximity, ranking.DistanceKm, reason, warnings, compliance, covers)
            {
                HoursSinceWeeklyRest = hours,
                WonTieBreak = wonTieBreak,
                SameBookingTripId = coveredTrip
            });
        }

        return new DriverSearchOutcome(result, discarded);
    }

    /// <summary>Over the other non-cancelled trips of the booking.</summary>
    private async Task<Dictionary<int, int>> DriversOfSameBooking(Trip trip, CancellationToken cancellationToken)
    {
        if (trip.BookingId <= 0)
        {
            return [];
        }

        var siblings = await _tripRepository.GetByBookingAsync(trip.BookingId, cancellationToken);
        return siblings
            .Where(t => t.Id != trip.Id && t.Status != TripStatus.Cancelled && t.AssignedDriverId.HasValue)
            .GroupBy(t => t.AssignedDriverId!.Value)
            .ToDictionary(g => g.Key, g => g.Min(t => t.Id));
    }

    /// <summary>Other non-cancelled trips of the driver on the trip's start day or overlapping it.</summary>
    private async Task<List<TimeWindow>> CommittedWork(int driverId, Trip trip, CancellationToken cancellationToken)
    {
        var window = trip.ScheduledWindow;
        var from = window.Start.Date;
        var to = window.End > from.AddDays(1) ? window.End : from.AddDays(1);
        var page = await _tripRepository.GetByDriverAsync(driverId, from, to, 1, MaxTripsPerDay, trip.Id, cancellationToken);
        return page.Items
            .Where(t => t.Status != TripStatus.Cancelled)
            .Select(t => t.ScheduledWindow)
            .ToList();
    }

    private async Task<Location?> ResolveLocation(int id, Location? loaded, Dictionary<int, Location?> cache, CancellationToken cancellationToken)
    {
        if (loaded is not null)
        {
            return loaded;
        }

        if (!cache.TryGetValue(id, out var location))
        {
            location = await _locationRepository.GetByIdAsync(id, cancellationToken);
            cache[id] = location;
        }

        return location;
    }
}
