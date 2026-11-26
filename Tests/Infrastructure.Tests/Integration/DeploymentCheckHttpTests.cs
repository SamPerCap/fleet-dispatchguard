using DispatchGuard.SampleData;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0091 R8.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DeploymentCheckHttpTests(PostgreSqlFixture db) : IDisposable
{
    private readonly DirectoryInfo _webRoot = Directory.CreateTempSubdirectory("dispatchguard-web-");

    public void Dispose() => _webRoot.Delete(recursive: true);

    private WebApplicationFactory<Program> Factory(
        string? connectionString = null, string environment = "Production", string locked = "true", bool withWeb = true)
    {
        if (withWeb) File.WriteAllText(Path.Combine(_webRoot.FullName, "index.html"), "<!doctype html>");
        return new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSessionKey();
            builder.UseSetting("CRON_SECRET", "the-cron-secret-of-the-test");
            builder.UseSetting(WebHostDefaults.WebRootKey, _webRoot.FullName);
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", connectionString ?? db.ConnectionString);
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("Demo:LockMasterDataDeletion", locked);
        });
    }

    private static async Task<string[]> FailuresAsync(WebApplicationFactory<Program> factory)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var results = await new DeploymentCheck(client).RunAsync();
        Assert.Equal(9, results.Count);
        return [.. results.Where(result => !result.Passed).Select(result => result.Name)];
    }

    [Fact]
    public async Task EverythingRight_NoFailures()
    {
        await using var factory = Factory();

        Assert.Empty(await FailuresAsync(factory));
    }

    [Fact]
    public async Task DeletionNotLocked_FailsInEveryResource()
    {
        await using var factory = Factory(locked: "false");

        Assert.Equal(["Deletion is locked: drivers", "Deletion is locked: vehicles", "Deletion is locked: locations"], await FailuresAsync(factory));
    }

    [Fact]
    public async Task WritesWithoutSession_Fails()
    {
        // Testing is the only environment that lets the session be turned off.
        await using var factory = Factory(environment: "Testing", locked: "false");

        var failures = await FailuresAsync(factory);
        Assert.Contains("Reading needs a session", failures);
        Assert.Contains("Writing needs a session", failures);
    }

    [Fact]
    public async Task WithoutWeb_Fails()
    {
        await using var factory = Factory(withWeb: false);

        Assert.Equal(["The web is served"], await FailuresAsync(factory));
    }

    [Fact]
    public async Task DatabaseDown_Fails()
    {
        await using var factory = Factory(UnreachableDatabase.ConnectionString);

        Assert.Contains("The database answers", await FailuresAsync(factory));
    }

    [Fact]
    public async Task NobodyThere_EveryCheckFails()
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };

        var results = await new DeploymentCheck(client).RunAsync();

        Assert.Equal(9, results.Count);
        Assert.All(results, result => Assert.False(result.Passed, result.Name));
    }
}
