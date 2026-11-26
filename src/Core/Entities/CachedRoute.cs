using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Entities;

public class CachedRoute : Entity
{
    public int OriginLocationId { get; private set; }
    public Location OriginLocation { get; private set; } = null!;

    public int DestinationLocationId { get; private set; }
    public Location DestinationLocation { get; private set; } = null!;

    /// <summary>JSON array of ids, e.g. [1,5,9].</summary>
    public string IntermediateLocationIdsJson { get; private set; } = "[]";

    public RouteMetrics Metrics { get; private set; } = null!;

    public string? RawServiceResponse { get; private set; }

    public string ServiceProvider { get; private set; } = "GoogleMaps";

    public DateTime? LastUsedAt { get; private set; }

    public int UsageCount { get; private set; }

    private CachedRoute() { }

    public CachedRoute(
        Location origin,
        Location destination,
        RouteMetrics metrics,
        string intermediateLocationIdsJson = "[]",
        string? rawServiceResponse = null,
        string serviceProvider = "GoogleMaps")
    {
        ValidateOrigin(origin);
        ValidateDestination(destination);
        ValidateMetrics(metrics);
        ValidateServiceProvider(serviceProvider);

        OriginLocation = origin;
        OriginLocationId = origin.Id;

        DestinationLocation = destination;
        DestinationLocationId = destination.Id;

        Metrics = metrics;
        IntermediateLocationIdsJson = intermediateLocationIdsJson ?? "[]";
        RawServiceResponse = rawServiceResponse;
        ServiceProvider = serviceProvider;
    }

    public void MarkAsUsed()
    {
        LastUsedAt = DateTime.UtcNow;
        UsageCount++;
        MarkAsUpdated();
    }

    /// <summary>Age counts from <c>Audit.CreatedAt</c>.</summary>
    public bool IsStale(TimeSpan maxAge)
    {
        var age = DateTime.UtcNow - Audit.CreatedAt;
        return age > maxAge;
    }

    public List<int> GetIntermediateLocationIds()
    {
        if (string.IsNullOrWhiteSpace(IntermediateLocationIdsJson) || IntermediateLocationIdsJson == "[]")
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<int>>(IntermediateLocationIdsJson) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public string GenerateRouteKey()
    {
        var intermediateIds = GetIntermediateLocationIds();
        var intermediateKey = intermediateIds.Count != 0
            ? string.Join("-", intermediateIds)
            : "direct";

        return $"{OriginLocationId}_{DestinationLocationId}_{intermediateKey}";
    }

    public static string GenerateRouteKey(int originId, int destinationId, IEnumerable<int>? intermediateIds = null)
    {
        var intermediateKey = intermediateIds?.Any() == true
            ? string.Join("-", intermediateIds)
            : "direct";

        return $"{originId}_{destinationId}_{intermediateKey}";
    }

    private static void ValidateOrigin(Location? origin)
    {
        EntityValidator.ValidateNotNull(origin, nameof(origin));
    }

    private static void ValidateDestination(Location? destination)
    {
        EntityValidator.ValidateNotNull(destination, nameof(destination));
    }

    private static void ValidateMetrics(RouteMetrics? metrics)
    {
        EntityValidator.ValidateNotNull(metrics, nameof(metrics));
    }

    private static void ValidateServiceProvider(string? serviceProvider)
    {
        EntityValidator.ValidateRequiredString(serviceProvider, nameof(serviceProvider));
    }
}