using DispatchGuard.API.Session;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace DispatchGuard.API.Perimeter;

public static class PerimeterServices
{
    public static bool IsDevelopmentOrTesting(this IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment(SessionSettings.TestingEnvironment);

    public static IServiceCollection AddPerimeter(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        AllowedHosts.Require(configuration, environment);

        var resetKey = DemoResetKey.From(configuration);
        services.AddSingleton(resetKey);
        var cronSecret = CronSecret.From(configuration);
        services.AddSingleton(cronSecret);
        services.AddSingleton(AuditKey.From(configuration));

        var limits = new RequestLimits(RequestLimitSettings.From(configuration), resetKey, cronSecret, environment.IsDevelopmentOrTesting());
        services.AddRateLimiter(limits.Configure);

        var networks = TrustedProxies.From(configuration);
        services.PostConfigure<ForwardedHeadersOptions>(options => TrustedProxies.Configure(options, networks));

        services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);
        return services;
    }
}
