using DispatchGuard.Application.Mappings;
using DispatchGuard.Application.Services;
using DispatchGuard.Application.Services.Bookings;
using DispatchGuard.Application.Services.CandidateSearch;
using DispatchGuard.Application.Services.Clients;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Application.Services.Engine;
using DispatchGuard.Application.Services.MasterData;
using DispatchGuard.Application.Services.Optimization;
using DispatchGuard.Application.Services.Recommendation;
using DispatchGuard.Application.Services.Resources;
using DispatchGuard.Application.Services.TravelTime;
using DispatchGuard.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DispatchGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<ClientMappingProfile>();
        });

        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<ILegalValidationService, LegalValidationService>();
        services.AddScoped<IDriverHoursService, DriverHoursService>();
        services.AddScoped<IDriverHoursQuery, DriverHoursQuery>();
        services.AddScoped<IVehicleEligibilityService, VehicleEligibilityService>();
        services.AddScoped<IScheduleConflictService, ScheduleConflictService>();
        services.AddScoped<IReturnToBaseService, ReturnToBaseService>();
        services.AddScoped<IForcedConsequences, ForcedConsequences>();
        services.AddScoped<IDriverAllocationService, DriverAllocationService>();
        services.AddScoped<IRecommendationService, RecommendationService>();
        services.AddScoped<IControlCenterService, ControlCenterService>();
        services.AddScoped<IControlInsightsService, ControlInsightsService>();
        services.AddScoped<IEngineAuditService, EngineAuditService>();
        services.AddScoped<IResourceHistoryService, ResourceHistoryService>();
        services.AddScoped<IAffectedTripsQuery, AffectedTripsQuery>();

        services.AddScoped<IDriverCandidateSearch, DriverCandidateSearch>();
        services.AddScoped<IVehicleCandidateSearch, VehicleCandidateSearch>();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDriverService, DriverService>();
        services.AddScoped<IVehicleService, VehicleService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<ITripService, TripService>();
        services.AddScoped<IBaseService, BaseService>();
        services.AddScoped<IBookingService, BookingService>();

        services.AddSingleton<TripDurationCache>();
        services.AddSingleton<ITripDurationCache>(sp => sp.GetRequiredService<TripDurationCache>());
        services.AddScoped<ITravelTimeService, TravelTimeService>();
        services.AddScoped<IVehicleChainingService, VehicleChainingService>();

        return services;
    }
}