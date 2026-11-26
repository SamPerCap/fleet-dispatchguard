using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0086 R4: without a person in front, nothing opens a connection. Without PostgreSQL: the database only counts.</summary>
public sealed partial class DatabaseStaysAsleepTests : IDisposable
{
    private const string Ready = "/health/ready";
    private const string Session = "/api/session";
    private const string OtherKey = "/////////////////////////////////////////38=";

    private readonly SilentDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private WebApplicationFactory<Program> Factory(string? key = TestSessionKey.Value, string environment = "Production") =>
        new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", _database.ConnectionString);
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("Demo:ResetKey", "the-reset-key-of-the-test");
            builder.UseSetting("CRON_SECRET", "the-cron-secret-of-the-test");
            builder.UseSetting("RateLimiting:Ip:Total", "100000");
            builder.UseSetting("RateLimiting:Ip:Heavy", "100000");
            if (key is not null) builder.UseSetting(TestSessionKey.Setting, key);
        });

    private static HttpClient Browser(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false
        });

    private static async Task<string> StartSessionAsync(HttpClient client)
    {
        using var response = await client.GetAsync(Session);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, string method, string url, string? cookie = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        request.Headers.Add("X-DispatchGuard-Request", "1");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (method is "POST" or "PUT") request.Content = JsonContent.Create(new { });
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>From the routes themselves: a new endpoint that forgets the session fails here.</summary>
    private static List<(string Method, string Url)> Operations(WebApplicationFactory<Program> factory) =>
        [.. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => (Method: method, Url: RouteParameter().Replace(endpoint.RoutePattern.RawText!, "1"))))
            .Where(operation => operation.Url is not (Session or "/health/live"))];

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();

    [Fact]
    public async Task WithoutSession_NoOperationOpensAConnection()
    {
        await using var factory = Factory();
        using var client = Browser(factory);
        var operations = Operations(factory);
        (string Method, string Url)[] others =
            [("GET", "/health/live"), ("GET", Session), ("GET", "/"), ("GET", "/api/nothing"), ("HEAD", "/api/drivers")];
        var requests = others.Concat(operations).ToList();
        while (requests.Count < 100) requests.AddRange(operations);

        foreach (var (method, url) in requests)
        {
            var status = await SendAsync(client, method, url);
            if (operations.Contains((method, url))) Assert.True(status == HttpStatusCode.Unauthorized, $"{method} {url} → {status}");
        }

        Assert.Equal(0, await _database.TakeConnectionsAsync());
    }

    /// <summary>That the count above can be other than zero.</summary>
    [Fact]
    public async Task WithSession_ReadyAsksTheDatabase()
    {
        await using var factory = Factory();
        using var client = Browser(factory);

        var status = await SendAsync(client, "GET", Ready, await StartSessionAsync(client));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal(1, await _database.TakeConnectionsAsync());
    }

    [Fact]
    public async Task ForgedSession_IsNotOne()
    {
        await using var factory = Factory();
        using var client = Browser(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(client, "GET", Ready, "__Host-lof-session=anything"));
        Assert.Equal(0, await _database.TakeConnectionsAsync());
    }

    [Fact]
    public async Task Session_AnotherInstanceWithTheSameKey_RecognisesIt()
    {
        await using var first = Factory();
        await using var second = Factory();
        using var here = Browser(first);
        using var there = Browser(second);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, await SendAsync(there, "GET", Ready, await StartSessionAsync(here)));
    }

    [Fact]
    public async Task Session_AnotherKey_DoesNotRecogniseIt()
    {
        await using var first = Factory();
        await using var second = Factory(OtherKey);
        using var here = Browser(first);
        using var there = Browser(second);

        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(there, "GET", Ready, await StartSessionAsync(here)));
    }

    [Fact]
    public async Task Key_IsOneAndDoesNotExpire()
    {
        await using var factory = Factory();

        var key = Assert.Single(factory.Services.GetRequiredService<IKeyManager>().GetAllKeys());

        Assert.True(key.ExpirationDate.Year > 9000, $"{key.ExpirationDate}");
        Assert.True(key.ActivationDate < DateTimeOffset.UtcNow, $"{key.ActivationDate}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("this is not base64")]
    [InlineData("c2hvcnQ=")]
    public async Task Start_InProductionWithoutAProperKey_Fails(string? key)
    {
        await using var factory = Factory(key);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(TestSessionKey.Setting, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_InDevelopmentWithoutKey_MakesOneOfItsOwn()
    {
        await using var factory = Factory(key: null, environment: "Development");
        using var client = Browser(factory);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, await SendAsync(client, "GET", Ready, await StartSessionAsync(client)));
    }

    /// <summary>The web tells it from the 401 of a wrong key.</summary>
    [Fact]
    public async Task Read_WithoutSession_SaysWhatIsMissing()
    {
        await using var factory = Factory();
        using var client = Browser(factory);

        using var response = await client.GetAsync("/api/drivers");

        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionMissing").GetBoolean());
    }
}
