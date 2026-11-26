using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.CandidateSearch;

public sealed record DriverCandidateDto(
    int Rank,
    int DriverId,
    string FullName,
    string Proximity,
    double? DistanceKm,
    LocalizedText Reason,
    IReadOnlyList<LocalizedText> Warnings,
    DecisionExplanation? Explanation = null,
    bool CoversSameBooking = false,
    double? HoursSinceWeeklyRest = null,
    bool WonTieBreak = false)
{
    public static List<DriverCandidateDto> FromRanked(IReadOnlyList<DriverCandidate> candidates)
    {
        var explanations = Explanations.DecisionExplainer.ForDriverCandidates(candidates);
        return candidates.Select((c, i) => From(c) with { Explanation = explanations[i] }).ToList();
    }

    public static DriverCandidateDto From(DriverCandidate candidate) => new(
        candidate.Rank,
        candidate.Driver.Id,
        candidate.Driver.FullName,
        candidate.Proximity.ToString(),
        candidate.DistanceKm is double distanceKm ? Math.Round(distanceKm, 1) : null,
        candidate.Reason,
        candidate.Warnings.Select(w => w.Message).ToList(),
        CoversSameBooking: candidate.CoversSameBooking,
        HoursSinceWeeklyRest: candidate.HoursSinceWeeklyRest is TimeSpan sinceRest ? Math.Round(sinceRest.TotalHours, 1) : null,
        WonTieBreak: candidate.WonTieBreak);
}

/// <summary>The first hard filter that excluded the driver. <c>Broken</c>: the rules of CE 561/2006, when that is the cause.</summary>
public sealed record DiscardedDriverDto(int DriverId, string FullName, LocalizedText Reason, DiscardCause Cause, LocalizedText? Broken);

public sealed record DriverOptionsDto(IReadOnlyList<DriverCandidateDto> Candidates, IReadOnlyList<DiscardedDriverDto> Discarded)
{
    public static DriverOptionsDto From(DispatchGuard.Core.ValueObjects.DriverSearchOutcome outcome) => new(
        DriverCandidateDto.FromRanked(outcome.Candidates),
        outcome.Discarded.OrderBy(d => d.Driver.FullName).Select(d => new DiscardedDriverDto(d.Driver.Id, d.Driver.FullName, d.Reason, d.Cause, d.Broken)).ToList());
}

/// <summary>
/// Real driving, in hours, with the CE 561/2006 limits. With <c>WeeklyRestInProgress</c> the rest reaches the instant asked for:
/// by schedule, it has not ended (SPEC 0039).
/// </summary>
public sealed record DriverHoursDto(
    int DriverId, DateTime At, double Today, double ThisWeek, double LastTwoWeeks, double SinceLastWeeklyRest,
    DateTime? LastWeeklyRestEnd, double MaxDaily, double MaxWeekly, double MaxBiWeekly, bool WeeklyRestInProgress)
{
    public static DriverHoursDto From(int driverId, DateTime at, DispatchGuard.Core.ValueObjects.DriverHours hours) => new(
        driverId, at, RoundedHours(hours.Today), RoundedHours(hours.ThisWeek), RoundedHours(hours.LastTwoWeeks), RoundedHours(hours.SinceLastWeeklyRest), hours.LastWeeklyRestEnd,
        RoundedHours(DriverHours.MaxDaily), RoundedHours(DriverHours.MaxWeekly), RoundedHours(DriverHours.MaxBiWeekly), hours.LastWeeklyRestEnd == at);

    private static double RoundedHours(TimeSpan time) => Math.Round(time.TotalHours, 2);
}
