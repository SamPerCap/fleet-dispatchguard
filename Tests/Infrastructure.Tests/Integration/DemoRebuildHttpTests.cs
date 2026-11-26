using System.Net;
using System.Net.Http.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.DemoData;
using DispatchGuard.Infrastructure.Demo;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0086 R3. With a database of its own: it deletes everything.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DemoRebuildHttpTests(PostgreSqlFixture db)
{
    private const string Url = "/api/admin/demo/rebuild";
    private const string Secret = "the-cron-secret-of-the-test";

    private readonly string _connectionString =
        new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = "DispatchGuardRebuild" }.ConnectionString;

    /// <summary>As Vercel runs it: in Production, with the limits it has by default.</summary>
    private WebApplicationFactory<Program> Factory(string? secret = Secret, int resetsThatFail = 0) =>
        new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSessionKey();
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", _connectionString);
            builder.UseSetting("AllowedHosts", "localhost");
            if (secret is not null) builder.UseSetting("CRON_SECRET", secret);
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new FailuresLeft { Count = resetsThatFail });
                services.AddScoped<DemoDataReset>();
                services.AddScoped<IDemoDataReset, ResetThatFails>();
            });
        });

    private static HttpClient Cron(WebApplicationFactory<Program> factory, string? authorization = "Bearer " + Secret)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        if (authorization is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorization);
        return client;
    }

    private DispatchGuardDbContext Context() =>
        new(new DbContextOptionsBuilder<DispatchGuardDbContext>().UseNpgsql(_connectionString).Options);

    private async Task<DispatchGuardDbContext> MigratedAsync()
    {
        var context = Context();
        await context.Database.MigrateAsync();
        return context;
    }

    [Theory]
    [InlineData(null, null, HttpStatusCode.NotFound)]
    [InlineData("", "Bearer ", HttpStatusCode.NotFound)]
    [InlineData(Secret, null, HttpStatusCode.Unauthorized)]
    [InlineData(Secret, "Bearer ", HttpStatusCode.Unauthorized)]
    [InlineData(Secret, "Bearer nope", HttpStatusCode.Unauthorized)]
    [InlineData(Secret, Secret, HttpStatusCode.Unauthorized)]
    [InlineData(Secret, "Basic " + Secret, HttpStatusCode.Unauthorized)]
    public async Task Rebuild_WithoutTheSecret_IsRefused(string? configured, string? authorization, HttpStatusCode expected)
    {
        await using var factory = Factory(configured);
        using var client = Cron(factory, authorization);

        using var response = await client.GetAsync(Url);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Rebuild_AfterOneFailure_LeavesTheDemoOfTheDay()
    {
        await using var before = await MigratedAsync();
        before.Locations.Add(new Location("De un visitante", "Calle", new GeoCoordinate(40.4m, -3.7m), "Madrid", "Madrid"));
        await before.SaveChangesAsync();
        await using var factory = Factory(resetsThatFail: 1);
        factory.UseKestrel(0);
        using var client = Cron(factory);

        using var response = await client.GetAsync(Url);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var report = await response.Content.ReadFromJsonAsync<SeedReport>();
        await using var after = Context();
        Assert.Equal(report!.TripsCreated, await after.Trips.CountAsync());
        Assert.Equal(report.TripsAllocated, await after.Trips.CountAsync(trip => trip.AssignedDriverId != null));
        Assert.True(report.TripsAllocated > 1_000, $"{report}");
        Assert.Equal(DemoDataset.Locations.Count(), await after.Locations.CountAsync());
        Assert.True(await after.Trips.AnyAsync(trip => trip.ScheduledWindow.Start > DateTime.UtcNow && trip.ScheduledWindow.Start < DateTime.UtcNow.AddDays(2)));
    }

    [Fact]
    public async Task Rebuild_FailsTwice_SaysSo()
    {
        await using var _ = await MigratedAsync();
        await using var factory = Factory(resetsThatFail: 2);
        factory.UseKestrel(0);
        using var client = Cron(factory);

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    /// <summary>The cron may call twice, and another instance may answer.</summary>
    [Fact]
    public async Task Rebuild_WhileAnotherIsRunning_DoesNothing()
    {
        await using var context = await MigratedAsync();
        await using var another = await new DemoDataReset(context).TryReserveAsync();
        Assert.NotNull(another);
        await using var factory = Factory();
        factory.UseKestrel(0);
        using var client = Cron(factory);

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private sealed class FailuresLeft
    {
        public int Count { get; set; }
    }

    private sealed class ResetThatFails(DemoDataReset reset, FailuresLeft failures) : IDemoDataReset
    {
        public Task<IAsyncDisposable?> TryReserveAsync(CancellationToken ct = default) => reset.TryReserveAsync(ct);

        public Task ResetAsync(CancellationToken ct = default) =>
            failures.Count-- > 0 ? throw new InvalidOperationException("The reset of the test failed") : reset.ResetAsync(ct);
    }
}
