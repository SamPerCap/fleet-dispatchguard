namespace DispatchGuard.Core.ValueObjects;

/// <summary>
/// Single source of the distance steps of a recommendation: straight-line km from the resource's position to the departure,
/// inclusive (SPEC 0062 R5). The manual panel keeps its own 5/10/20/40 bands. Operational, not regulatory.
/// </summary>
public static class RecommendationSteps
{
    public static readonly IReadOnlyList<double> Km = [5, 15, 30];

    /// <summary>Null when <paramref name="km"/> is unknown or beyond the last step.</summary>
    public static double? StepOf(double? km)
    {
        foreach (var step in Km)
        {
            if (km <= step) return step;
        }

        return null;
    }
}
