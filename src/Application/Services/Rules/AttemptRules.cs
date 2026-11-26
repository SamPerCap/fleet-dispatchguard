using DispatchGuard.Application.Services.Explanations;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Rules;

/// <summary>
/// Which catalog rules (<see cref="RulesCatalog"/> ids) an allocation broke; warnings are not broken rules. The engine's own ids
/// stay in the engine: the register speaks the catalog's language, so that the web can filter by rule and show it (SPEC 0059).
/// </summary>
public static class AttemptRules
{
    public const string DriverOverlap = "driver-overlap";
    public const string VehicleOverlap = "vehicle-overlap";
    /// <summary>A later, already assigned service would stop meeting CE 561/2006 (SPEC 0063); goes with the article it breaks.</summary>
    public const string LaterServices = "later-services";

    /// <summary>Prefixes of the ids of <see cref="LegalComplianceEvaluation"/> and of the driver and vehicle checks.</summary>
    private static readonly (string Prefix, string CatalogId)[] Map =
    [
        (DriverEligibilityCheck.NotActiveRule, "driver-active"),
        (DriverEligibilityCheck.UnavailableRulePrefix, "driver-unavailable"),
        ("CE_561_2006.ContinuousDriving.", "continuous"),
        ("CE_561_2006.DailyDriving.", "daily"),
        ("CE_561_2006.BiWeeklyDriving.", "biweekly"),
        ("CE_561_2006.WeeklyDriving.", "weekly"),
        ("CE_561_2006.DailyRest.ReducedLimit", "daily-rest-reduced"),
        ("CE_561_2006.DailyRest.", "daily-rest"),
        ("CE_561_2006.WeeklyRest.45hours", "weekly-rest"),
        ("VehicleEligibility.NotActive", "vehicle-active"),
        ("VehicleEligibility.InsufficientCapacity", "vehicle-seats"),
        ("VehicleEligibility.Unavailable.", "vehicle-unavailable"),
    ];

    /// <summary>Every catalog id an attempt can carry: the only ones worth filtering the register by.</summary>
    public static readonly IReadOnlyList<string> Audited =
        Map.Select(m => m.CatalogId).Append(LaterServices).Append(DriverOverlap).Append(VehicleOverlap).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Null when the catalog has no such rule.</summary>
    public static string? CatalogId(string engineRuleId) =>
        Map.FirstOrDefault(m => engineRuleId.StartsWith(m.Prefix, StringComparison.Ordinal)).CatalogId;

    /// <summary>In the order the engine checks them, without repeats.</summary>
    public static IReadOnlyList<string> Of(AllocationTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var ids = new List<string>();

        ids.AddRange(trace.Driver.Violations.Select(v => CatalogId(v.RuleId)).OfType<string>());

        if (trace.Compliance is { } compliance)
        {
            var legal = compliance.RuleChecks.Count > 0
                ? compliance.RuleChecks.Where(c => c.Outcome == RuleCheckOutcome.Violated).Select(c => c.RuleId)
                : compliance.Violations.Where(v => v.Severity != ViolationSeverity.Warning).Select(v => v.RuleId);
            ids.AddRange(legal.Select(CatalogId).OfType<string>());
        }

        if (trace.Compliance?.LaterServices is { HasImpacts: true } later)
        {
            ids.Add(LaterServices);
            ids.AddRange(later.Impacts.Select(i => CatalogId(i.With.RuleId)).OfType<string>());
        }

        if (trace.Eligibility is { IsEligible: false } eligibility)
            ids.AddRange(eligibility.Violations.Select(v => CatalogId(v.RuleId)).OfType<string>());
        if (trace.DriverConflict is { HasConflict: true }) ids.Add(DriverOverlap);
        if (trace.VehicleConflict is { HasConflict: true }) ids.Add(VehicleOverlap);

        return ids.Distinct(StringComparer.Ordinal).ToList();
    }
}
