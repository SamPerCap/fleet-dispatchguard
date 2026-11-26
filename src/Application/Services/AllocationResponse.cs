using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services;

public sealed record AllocationResponse(
    int TripId,
    int DriverId,
    int VehicleId,
    string Status,
    bool Forced,
    string? ForcedReason,
    AllocationValidationDto Validation,
    DecisionExplanation Explanation)
{
    public static AllocationResponse From(int tripId, AllocationResult result, AllocationOverride? requestedOverride) => new(
        tripId,
        result.Driver.Id,
        result.Vehicle.Id,
        Status: result.Forced ? "Forced" : "Allocated",
        result.Forced,
        // An override requested for a trip that turned out legal is ignored, so its reason is not reported either.
        ForcedReason: result.Forced ? requestedOverride?.Reason : null,
        new AllocationValidationDto(result.Validation.IsCompliant, result.Validation.Violations, result.Validation.Warnings),
        result.Explanation);
}

public sealed record AllocationValidationDto(
    bool IsCompliant,
    IReadOnlyCollection<LocalizedText> Violations,
    IReadOnlyCollection<LocalizedText> Warnings);
