namespace DispatchGuard.API.Perimeter;

/// <summary>Marks an endpoint that evaluates the whole fleet for many services at once.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HeavyCalculationAttribute : Attribute;

/// <summary>Marks the endpoint that gives a session to whoever has none.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StartsSessionAttribute : Attribute;

/// <summary>Marks the endpoint where the web says what is done in it: it must not use up the writes of the visitor.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RecordsVisitAttribute : Attribute;

internal static class RequestKinds
{
    public static bool IsHeavyCalculation(this HttpContext context) => context.HasMark<HeavyCalculationAttribute>();

    public static bool StartsSession(this HttpContext context) => context.HasMark<StartsSessionAttribute>();

    public static bool RecordsVisit(this HttpContext context) => context.HasMark<RecordsVisitAttribute>();

    /// <summary>By the endpoint and not by the path, which has many spellings.</summary>
    private static bool HasMark<TAttribute>(this HttpContext context) where TAttribute : Attribute =>
        context.GetEndpoint()?.Metadata.GetMetadata<TAttribute>() is not null;
}
