using DispatchGuard.API.Auditing;
using DispatchGuard.API.Extensions;
using DispatchGuard.API.Endpoints;
using DispatchGuard.API.Http;
using DispatchGuard.API.Perimeter;
using DispatchGuard.API.Session;
using DispatchGuard.API.XRay;
using DispatchGuard.Application;
using DispatchGuard.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplicationLayer();
builder.Services.AddApiServices(builder.Configuration, builder.Environment);

var app = builder.Build();

// Before anything that reads the address or the scheme of the request.
app.UseForwardedHeaders();
app.UseMiddleware<RequestLanguage>();
app.UseMiddleware<SecurityHeaders>();
// Outside the step that answers the failures: their result is part of the trace.
app.UseMiddleware<RequestTracing>();

// Every other step is inside: its failures are answered here.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Fleet DispatchGuard API v1");
        options.RoutePrefix = string.Empty;
    });
}

// Here and not at the start, its place by default: the health endpoints answer at this point, and need the headers.
app.UseRouting();
app.UseHttpsRedirection();
// Before the limits: a page is a dozen files, and none of them costs anything.
app.UseWebPages();
app.UseCors(DispatchGuard.API.Extensions.ServiceCollectionExtensions.CorsPolicy);

// No users or permissions: the session only says which browser it is (SPEC 0084). The limits need to know it.
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<SessionGuard>();
// Inside the trace and after the guard: only an answer to a session is sealed for it (SPEC 0097).
app.UseMiddleware<XRayCapture>();

app.MapApiEndpoints();

app.Run();

/// <summary>Public for the WebApplicationFactory of the HTTP tests.</summary>
public partial class Program;

