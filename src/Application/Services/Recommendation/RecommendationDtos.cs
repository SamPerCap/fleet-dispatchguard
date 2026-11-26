using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Recommendation;

/// <summary>One booking, or the pending trips overlapping [From, To) of a base (or of all of them) (SPEC 0062 R1).</summary>
public sealed record RecommendationRequest(int? BookingId = null, int? BaseId = null, DateTime? From = null, DateTime? To = null);

/// <summary>A line of a proposal as the dispatcher has it on screen. Without driver and vehicle it is only context.</summary>
/// <param name="Manual">Changed by hand: only the engine's rules decide. As proposed, it must also still arrive (SPEC 0062 R6, R9).</param>
public sealed record RecommendationLineInput(int TripId, int? DriverId, int? VehicleId, bool Manual = false);

/// <summary>In the order of the proposal; every line is validated again (SPEC 0062 R9).</summary>
public sealed record RecommendationCheckRequest(IReadOnlyList<RecommendationLineInput>? Lines);

/// <summary>What fits one line, given the others.</summary>
public sealed record RecommendationOptionsRequest(int TripId, IReadOnlyList<RecommendationLineInput>? Lines);

/// <summary>A pair the engine accepts, no pair within the steps, or a pair the engine now rejects.</summary>
public enum RecommendationLineStatus
{
    Proposed = 0,
    NoSolution = 1,
    Invalid = 2
}

/// <param name="Where">Where the distance is measured from, in the dispatcher's words; the estimate of arriving in <paramref name="Late"/>.</param>
public sealed record RecommendedResourceDto(int Id, LocalizedText Name, double? DistanceKm, double? StepKm, LocalizedText Where, bool SameBooking, LocalizedText? Late);

public sealed record RecommendationLineDto(
    int TripId, int BookingId, string CustomerName, int BaseId, DateTime Start, DateTime End, string Origin, string Destination, int RequiredSeats,
    RecommendationLineStatus Status, RecommendedResourceDto? Driver, RecommendedResourceDto? Vehicle, LocalizedText Reason,
    IReadOnlyList<LocalizedText> Warnings, DecisionExplanation? Explanation, ReachCounts? Drivers = null, ReachCounts? Vehicles = null);

/// <param name="LeftOut">Pending trips beyond the limits (SPEC 0062 R2).</param>
public sealed record RecommendationDto(
    DateTime CalculatedAt, IReadOnlyList<RecommendationLineDto> Lines, int LeftOut, int MaxTrips, int MaxDays, IReadOnlyList<double> StepsKm, LocalizedText Note, LocalizedText ArrivalNote);

public sealed record RecommendationOptionsDto(IReadOnlyList<RecommendedResourceDto> Drivers, IReadOnlyList<RecommendedResourceDto> Vehicles);

/// <summary>Who would be recommended for one trip right now; null = no badge in the manual panel (SPEC 0062 R12).</summary>
public sealed record TripRecommendationDto(int TripId, DateTime CalculatedAt, int? DriverId, int? VehicleId);
