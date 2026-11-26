namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// Why a trip is non-compliant without anyone having forced it: an earlier service of its driver was forced knowing
/// that this one would stop meeting CE 561/2006 (SPEC 0063 R8). Keeps the source so that the mark can be released with it.
/// </summary>
public sealed record ForcedByTrip(int SourceTripId, string Reason, string By);
