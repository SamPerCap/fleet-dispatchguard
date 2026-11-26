using DispatchGuard.Core.Interfaces;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Infrastructure.Auditing;
using DispatchGuard.Infrastructure.Demo;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DispatchGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DispatchGuardDbContext>(options =>
            options.UseNpgsql(
                MainConnectionString.Read(configuration),
                npgsqlOptions =>
                {
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorCodesToAdd: null
                    );
                    npgsqlOptions.MigrationsAssembly(typeof(DispatchGuardDbContext).Assembly.FullName);
                }
            ).AddInterceptors(new QueryRecorder(), new WriteRecorder(), new TransactionRecorder())
        );

        services.AddSingleton<IDatabaseReadiness>(provider => new DatabaseReadiness(
            MainConnectionString.Read(configuration), provider.GetRequiredService<ILogger<DatabaseReadiness>>()));

        // Total wait for the resources another operation holds; 5 s by default (SPEC 0065 R5).
        services.AddSingleton(ResourceLockSettings.From(configuration[ResourceLockSettings.Key]));

        services.AddScoped<IDemoDataReset, DemoDataReset>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IVisitLog, VisitLog>();
        services.AddScoped<IVisitReader, VisitReader>();
        services.AddScoped<IAllocationAttemptLog, AllocationAttemptLog>();
        services.AddScoped<IControlCenterReadModel, ControlCenterReadModel>();
        services.AddScoped<IVehicleLastServiceReader, VehicleLastServiceReader>();
        services.AddScoped<IAssignedTripReader, AssignedTripReader>();
        services.AddScoped<IAllocationAttemptAudit, AllocationAttemptAudit>();

        services.AddScoped<ILocationRepository, LocationRepository>();
        services.AddScoped<ITripRepository, TripRepository>();
        services.AddScoped<IBookingRepository, BookingRepository>();
        services.AddScoped<IDriverRepository, DriverRepository>();
        services.AddScoped<IVehicleRepository, VehicleRepository>();
        services.AddScoped<ICircuitRepository, CircuitRepository>();
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<ICachedRouteRepository, CachedRouteRepository>();
        services.AddScoped<IDrivingRecordRepository, DrivingRecordRepository>();
        services.AddScoped<IPlanningSnapshotSource, PlanningSnapshotSource>();

        return services;
    }
}