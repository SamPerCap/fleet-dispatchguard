namespace DispatchGuard.Core.Exceptions;

/// <summary>An attempt without its record does not count (SPEC 0085 R4): nothing was assigned, and it can be tried again.</summary>
public sealed class AttemptNotRecordedException(int tripId, Exception cause)
    : Exception($"The attempt to allocate trip {tripId} could not be recorded", cause);
