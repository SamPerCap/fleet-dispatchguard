using DispatchGuard.API.Auditing;
using DispatchGuard.API.Demo;
using DispatchGuard.API.Endpoints;
using DispatchGuard.API.Errors;
using DispatchGuard.API.Json;
using DispatchGuard.API.Perimeter;
using DispatchGuard.API.Session;
using DispatchGuard.API.XRay;
using DispatchGuard.Application.Services.Control;
using DispatchGuard.Core.Interfaces;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.OpenApi;

namespace DispatchGuard.API.Extensions;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicy = "Configured";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddAnonymousSession(SessionSettings.From(configuration, environment));
        SessionKey.From(configuration, environment).AddTo(services);
        services.AddPerimeter(configuration, environment);
        services.AddScoped<IAuditOrigin, RequestOrigin>();
        services.AddSingleton(VisitSettings.From(configuration));
        services.AddSingleton(provider => new VisitNotice(
            provider.GetRequiredService<VisitSettings>(), provider.GetRequiredService<ILogger<VisitNotice>>()));
        services.AddSingleton<OwnApi>();
        services.AddSingleton<XRayTokens>();
        services.AddScoped<DemoRebuild>();
        services.AddSingleton((configuration.GetSection(OperatingHoursSettings.Section)
            .Get<OperatingHoursSettings>() ?? new()).Validate());
        services.AddSingleton(MasterDataDeletionLock.From(configuration));

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
            // Dates of a request body as UTC instants (SPEC 0067).
            options.SerializerOptions.Converters.Add(new UtcDateTimeConverter());
            options.SerializerOptions.Converters.Add(new StorableTextConverter());
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        // Without this call Minimal APIs ignore the DTO annotations, unlike controllers with [ApiController].
        services.AddValidation();

        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemTexts.Customize);
        services.AddExceptionHandler<DomainExceptionHandler>();

        services.AddEndpointsApiExplorer();

        services.AddSwagger();

        // Only to the configured origins; none by default (SPEC 0044 R5).
        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(CorsPolicy, p => p.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().WithExposedHeaders(XRayTokens.Header)));

        return services;
    }

    private static void AddAnonymousSession(this IServiceCollection services, SessionSettings settings)
    {
        services.AddSingleton(settings);
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentSession, HttpCurrentSession>();
        services.AddAuthentication(AnonymousSession.Scheme)
            .AddCookie(AnonymousSession.Scheme, options => AnonymousSession.Configure(options, settings));
    }

    private static IServiceCollection AddSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            // Records of different areas share a name (AffectedTripDto), and the default takes it as a conflict.
            options.CustomSchemaIds(type => Regex.Replace(type.ToString(), @"[^\w.]+", "_"));
            options.MapType<LocalizedText>(() => new OpenApiSchema { Type = JsonSchemaType.String });
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Fleet DispatchGuard API",
                Version = "v1",
                Description = "Fleet optimization and logistics management system",
                Contact = new OpenApiContact
                {
                    Name = "Fleet DispatchGuard"
                }
            });
        });

        return services;
    }
}