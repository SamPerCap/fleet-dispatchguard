using DispatchGuard.API.Http;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Application.Services.Engine;
using DispatchGuard.Core.Enums;

namespace DispatchGuard.API.Endpoints;

/// <param name="Rule">Id of a rule in the catalog of <c>/api/rules</c>.</param>
/// <param name="By">Exact name of whoever made the attempt.</param>
public sealed record EngineAuditFilter(
    DateTime? Date,
    int? Days,
    int? BaseId,
    string? Rule,
    int? DriverId,
    int? VehicleId,
    string? By,
    AttemptOrigin? Origin)
{
    private const int DefaultDays = 1;

    public EngineAuditQuery ToQuery(ActivityKind kind) => new(
        Date.AsUtc() ?? DateTime.UtcNow,
        Days ?? DefaultDays,
        BaseId,
        kind,
        Rule,
        DriverId,
        VehicleId,
        By,
        Origin);
}
