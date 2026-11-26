using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.CandidateSearch;

public sealed record VehicleCandidateDto(
    int Rank,
    int VehicleId,
    string LicensePlate,
    int SeatingCapacity,
    string Proximity,
    string PositionSource,
    double? DistanceKm,
    LocalizedText Reason,
    DecisionExplanation? Explanation = null)
{
    public static List<VehicleCandidateDto> FromRanked(IReadOnlyList<VehicleCandidate> candidates)
    {
        var explanations = Explanations.DecisionExplainer.ForVehicleCandidates(candidates);
        return candidates.Select((c, i) => From(c) with { Explanation = explanations[i] }).ToList();
    }

    public static VehicleCandidateDto From(VehicleCandidate candidate) => new(
        candidate.Rank,
        candidate.Vehicle.Id,
        candidate.Vehicle.LicensePlate,
        candidate.Vehicle.SeatingCapacity,
        candidate.Proximity.ToString(),
        candidate.PositionSource.ToString(),
        candidate.DistanceKm is double distanceKm ? Math.Round(distanceKm, 1) : null,
        candidate.Reason);
}
