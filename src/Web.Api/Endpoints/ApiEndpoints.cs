namespace DispatchGuard.API.Endpoints;

public static class ApiEndpoints
{
    public static void MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAdminEndpoints();
        app.MapAuditEndpoints();
        app.MapBasesEndpoints();
        app.MapBookingsEndpoints();
        app.MapClientsEndpoints();
        app.MapControlEndpoints();
        app.MapDriversEndpoints();
        app.MapEngineEndpoints();
        app.MapHealthEndpoints();
        app.MapLocationsEndpoints();
        app.MapOptimizationEndpoints();
        app.MapRecommendationEndpoints();
        app.MapRulesEndpoints();
        app.MapSessionEndpoints();
        app.MapTripsEndpoints();
        app.MapVehiclesEndpoints();
        app.MapVisitsEndpoints();
        app.MapXRayEndpoints();
    }
}
