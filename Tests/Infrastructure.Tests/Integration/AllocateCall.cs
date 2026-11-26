using System.Net.Http.Json;

namespace Infrastructure.Tests.Integration;

/// <summary>The only place of the tests that knows how the allocate endpoint is called.</summary>
internal static class AllocateCall
{
    public static Task<HttpResponseMessage> AllocateAsync(
        this HttpClient client, int trip, int driver, int vehicle,
        string? assignedBy = null, bool force = false, string? reason = null, string origin = "Manual", bool onlyIfUnassigned = false,
        CancellationToken cancellationToken = default) =>
        client.PostAsJsonAsync(
            $"/api/trips/{trip}/allocate",
            new
            {
                DriverId = driver, VehicleId = vehicle, AssignedBy = assignedBy, Force = force, Reason = reason, Origin = origin,
                OnlyIfUnassigned = onlyIfUnassigned
            },
            cancellationToken);
}
