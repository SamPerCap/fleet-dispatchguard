using System.Diagnostics;
using System.Net;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0091 R4.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class HealthHttpTests(PostgreSqlFixture db)
{
    private const string Live = "/health/live";
    private const string Ready = "/health/ready";

    private static WebApplicationFactory<Program> Factory(string connectionString, params (string Key, string Value)[] settings) =>
        new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSessionKey();
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", connectionString);
            builder.UseSetting("AllowedHosts", "localhost");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        });

    private static HttpClient Probe(WebApplicationFactory<Program> factory, string scheme = "https") =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri($"{scheme}://localhost"), AllowAutoRedirect = false, HandleCookies = false
        });

    private static async Task<HttpClient> VisitorAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/session")).StatusCode);
        return client;
    }

    private static async Task<(HttpStatusCode Status, TimeSpan Took)> AskAsync(HttpClient client, string url)
    {
        var clock = Stopwatch.StartNew();
        using var response = await client.GetAsync(url);
        return (response.StatusCode, clock.Elapsed);
    }

    [Fact]
    public async Task DatabaseUp_BothAnswer()
    {
        await using var factory = Factory(db.ConnectionString);
        using var client = await VisitorAsync(factory);

        Assert.Equal(HttpStatusCode.OK, (await AskAsync(client, Live)).Status);
        Assert.Equal(HttpStatusCode.OK, (await AskAsync(client, Ready)).Status);
    }

    [Fact]
    public async Task DatabaseDown_LiveIsUpAndReadyIsNot()
    {
        await using var factory = Factory(UnreachableDatabase.ConnectionString);
        using var client = await VisitorAsync(factory);

        Assert.Equal(HttpStatusCode.OK, (await AskAsync(client, Live)).Status);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await AskAsync(client, Ready)).Status);
    }

    [Fact]
    public async Task DatabaseSilent_ReadyGivesUpInTimeAndLiveDoesNotAskIt()
    {
        using var silent = new SilentDatabase();
        await using var factory = Factory(silent.ConnectionString);
        using var client = await VisitorAsync(factory);

        var live = await AskAsync(client, Live);
        var asked = await silent.TakeConnectionsAsync();
        var ready = await AskAsync(client, Ready);

        Assert.Equal(HttpStatusCode.OK, live.Status);
        Assert.Equal(0, asked);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.Status);
        Assert.True(ready.Took < TimeSpan.FromSeconds(8), $"It took {ready.Took}");
    }

    [Fact]
    public async Task DatabaseBack_ReadyNoticesAtOnce()
    {
        var later = new NpgsqlConnectionStringBuilder(db.ConnectionString) { Database = $"Later{Guid.NewGuid():N}" }.ConnectionString;
        await using var factory = Factory(later);
        using var client = await VisitorAsync(factory);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await AskAsync(client, Ready)).Status);

        await using var context = new DispatchGuardDbContext(new DbContextOptionsBuilder<DispatchGuardDbContext>().UseNpgsql(later).Options);
        await context.Database.MigrateAsync();
        var back = await AskAsync(client, Ready);
        await context.Database.EnsureDeletedAsync();

        Assert.Equal(HttpStatusCode.OK, back.Status);
    }

    /// <summary>The probes of the host come every few seconds from one address, and inside the host there is no HTTPS.</summary>
    [Fact]
    public async Task Probe_IsOutsideTheLimitsAndIsNotRedirected()
    {
        await using var factory = Factory(db.ConnectionString, ("RateLimiting:Ip:Total", "1"), ("https_port", "443"));
        using var client = Probe(factory, "http");

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await AskAsync(client, Live)).Status);
        }
    }

    /// <summary>It wakes the database up: only for whoever is in front of the web, and with the quota of a read (SPEC 0086).</summary>
    [Fact]
    public async Task Ready_NeedsASessionAndHasAQuota()
    {
        await using var factory = Factory(db.ConnectionString, ("RateLimiting:Session:Reads", "1"));
        using var probe = Probe(factory);
        using var visitor = await VisitorAsync(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await AskAsync(probe, Ready)).Status);
        Assert.Equal(HttpStatusCode.OK, (await AskAsync(visitor, Ready)).Status);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await AskAsync(visitor, Ready)).Status);
    }

    [Fact]
    public async Task Ready_ManyAtOnce_ShareOneConnection()
    {
        using var silent = new SilentDatabase();
        await using var factory = Factory(silent.ConnectionString);
        using var client = await VisitorAsync(factory);

        var answers = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => AskAsync(client, Ready)));

        Assert.All(answers, answer => Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.Status));
        Assert.Equal(1, await silent.TakeConnectionsAsync());
    }

    [Theory]
    [InlineData(Live)]
    [InlineData(Ready)]
    public async Task Answer_IsNotCachedAndHasTheHeaders(string url)
    {
        await using var factory = Factory(db.ConnectionString);
        using var client = await VisitorAsync(factory);

        using var response = await client.GetAsync(url);

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}
