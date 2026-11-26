using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;

namespace DispatchGuard.Core.Entities;

/// <summary>
/// One human call to <c>AllocateDriver</c>; candidate searches never create one. It keeps the broken rules, the reason of a
/// forced assignment and the explanation the dispatcher saw, so the attempt can be audited as it was decided (SPEC 0059).
/// </summary>
public class AllocationAttempt : Entity
{
    public const int AttemptedByMaxLength = 100;
    public const int DetailMaxLength = 1000;
    public const int ForcedReasonMaxLength = ValueObjects.AllocationOverride.MaxReasonLength;
    /// <summary>Recorded when the caller gives no name: free text until there are users. The web words it in the language of the reader.</summary>
    public const string Anonymous = "(sin nombre)";
    public const int SessionIdLength = 32;
    public const int ShortSessionIdLength = 8;

    public DateTime AttemptedAt { get; private set; }
    public string AttemptedBy { get; private set; } = Anonymous;
    public int TripId { get; private set; }
    public int DriverId { get; private set; }
    public int VehicleId { get; private set; }
    public int BaseId { get; private set; }
    public AllocationOutcome Outcome { get; private set; }
    /// <summary>Infringement of a rejection; null when assigned.</summary>
    public string? Detail { get; private set; }
    /// <summary>Catalog ids (<c>/api/rules</c>) of the broken rules; empty when it passed every rule.</summary>
    public string[] RuleIds { get; private set; } = [];
    /// <summary>Null unless <see cref="AllocationOutcome.Forced"/>.</summary>
    public string? ForcedReason { get; private set; }
    /// <summary>The engine's explanation as JSON, as it was when the attempt was decided; null for attempts older than SPEC 0059.</summary>
    public string? Explanation { get; private set; }

    /// <summary>Never changes the outcome counts (SPEC 0062 R11).</summary>
    public AttemptOrigin Origin { get; private set; } = AttemptOrigin.Manual;

    /// <summary>The anonymous session that made it; null for attempts older than SPEC 0084 and outside a request.</summary>
    public string? SessionId { get; private set; }

    /// <summary>What others may see of <see cref="SessionId"/>.</summary>
    public string? ShortSessionId => SessionId?[..ShortSessionIdLength];

    public bool IsRejected => Outcome is not (AllocationOutcome.Assigned or AllocationOutcome.Forced);

    private AllocationAttempt() { }

    public AllocationAttempt(
        DateTime attemptedAt, string? attemptedBy, int tripId, int driverId, int vehicleId, int baseId,
        AllocationOutcome outcome, string? detail = null,
        IEnumerable<string>? ruleIds = null, string? forcedReason = null, string? explanation = null,
        AttemptOrigin origin = AttemptOrigin.Manual)
    {
        AttemptedAt = attemptedAt;
        AttemptedBy = string.IsNullOrWhiteSpace(attemptedBy) ? Anonymous : Truncate(attemptedBy.Trim(), AttemptedByMaxLength);
        TripId = tripId;
        DriverId = driverId;
        VehicleId = vehicleId;
        BaseId = baseId;
        Outcome = outcome;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : Truncate(detail.Trim(), DetailMaxLength);
        RuleIds = ruleIds?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        ForcedReason = outcome == AllocationOutcome.Forced && !string.IsNullOrWhiteSpace(forcedReason)
            ? Truncate(forcedReason.Trim(), ForcedReasonMaxLength) : null;
        Origin = origin;
        Explanation = string.IsNullOrWhiteSpace(explanation) ? null : explanation;
    }

    public void MadeBy(string? sessionId)
    {
        if (sessionId is not null && sessionId.Length != SessionIdLength)
        {
            throw new ArgumentException($"A session id has {SessionIdLength} characters", nameof(sessionId));
        }

        SessionId = sessionId;
    }

    private static string Truncate(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];
}
