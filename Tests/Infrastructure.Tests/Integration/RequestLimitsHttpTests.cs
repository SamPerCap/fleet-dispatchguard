using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0089 R1–R3. Each test has its own API, and so its own quotas.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RequestLimitsHttpTests(PostgreSqlFixture db)
{
    private const string Client = "203.0.113.5";
    private const string OtherClient = "203.0.113.6";
    private const string Proxy = "10.1.2.3";
    private const string ResetKey = "s3cret-test-key";
    private const string ReadUrl = "/api/bases";

    /// <summary>The answer to a read without session that the limits let through: they go first (SPEC 0086).</summary>
    private const HttpStatusCode LetThrough = HttpStatusCode.Unauthorized;

    /// <summary>No such booking: the limiter counts the request the same, and the test does not depend on the planning.</summary>
    private const string HeavyUrl = "/api/recommendations";

    private WebApplicationFactory<Program> Factory(string environment, params (string Key, string Value)[] settings) =>
        new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSessionKey();
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
            builder.UseSetting("AllowedHosts", "localhost");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, RemoteIpFromHeader>());
        });

    private WebApplicationFactory<Program> Factory(params (string Key, string Value)[] settings) => Factory("Production", settings);

    private static HttpClient Browser(WebApplicationFactory<Program> factory, string ip = Client)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add(RemoteIpFromHeader.Name, ip);
        client.DefaultRequestHeaders.Add("X-DispatchGuard-Request", "1");
        return client;
    }

    private static async Task<HttpClient> WithSessionAsync(WebApplicationFactory<Program> factory, string ip = Client)
    {
        var client = Browser(factory, ip);
        using var response = await client.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static async Task<HttpStatusCode> ReadAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ReadUrl);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> WriteAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/bases", new { Name = string.Empty });
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> HeavyAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(HeavyUrl, new { BookingId = 99_999_999 });
        return response.StatusCode;
    }

    private static async Task ExhaustAsync(int limit, Func<Task<HttpStatusCode>> call)
    {
        for (var i = 1; i <= limit; i++)
        {
            Assert.True(await call() != HttpStatusCode.TooManyRequests, $"Request {i} of {limit} was refused");
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await call());
    }

    [Fact]
    public async Task Default_HeavyCalculations_TwentyPerSession()
    {
        await using var factory = Factory();
        using var client = await WithSessionAsync(factory);

        await ExhaustAsync(20, () => HeavyAsync(client));

        // Only that quota is exhausted, and only that of this session.
        Assert.Equal(HttpStatusCode.OK, await ReadAsync(client));
        Assert.Equal(HttpStatusCode.BadRequest, await WriteAsync(client));
        using var other = await WithSessionAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, await HeavyAsync(other));
    }

    [Fact]
    public async Task Default_Writes_AHundredAndFiftyPerSession()
    {
        await using var factory = Factory();
        using var client = await WithSessionAsync(factory);

        await ExhaustAsync(150, () => WriteAsync(client));

        Assert.Equal(HttpStatusCode.OK, await ReadAsync(client));
    }

    [Fact]
    public async Task Default_Reads_ThreeHundredPerSession()
    {
        await using var factory = Factory();
        using var client = await WithSessionAsync(factory);

        // The one that created the session had none yet.
        await ExhaustAsync(300, () => ReadAsync(client));

        Assert.Equal(HttpStatusCode.BadRequest, await WriteAsync(client));
    }

    [Fact]
    public async Task Default_SixHundredPerIp_WithOrWithoutSession()
    {
        await using var factory = Factory();
        using var client = Browser(factory);

        await ExhaustAsync(600, () => ReadAsync(client));

        using var other = Browser(factory, OtherClient);
        Assert.Equal(LetThrough, await ReadAsync(other));
    }

    [Fact]
    public async Task Default_HeavyCalculationsWithoutSession_SixtyPerIp()
    {
        await using var factory = Factory();
        using var client = Browser(factory);

        await ExhaustAsync(60, async () =>
        {
            using var response = await client.GetAsync("/api/optimization/vehicle-chaining?from=x&to=y");
            return response.StatusCode;
        });
        Assert.Equal(LetThrough, await ReadAsync(client));
    }

    [Fact]
    public async Task Default_NewSessions_TenPerIp_ButRenewingIsFree()
    {
        await using var factory = Factory();
        using var known = await WithSessionAsync(factory);
        using var newcomer = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });
        newcomer.DefaultRequestHeaders.Add(RemoteIpFromHeader.Name, Client);

        // The known one spent the first of the ten.
        await ExhaustAsync(9, async () =>
        {
            using var response = await newcomer.GetAsync("/api/session");
            return response.StatusCode;
        });

        using var renewed = await known.GetAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        // A forged cookie is no session: it does not get around the limit.
        using var forged = new HttpRequestMessage(HttpMethod.Get, "/api/session");
        forged.Headers.Add("Cookie", "__Host-lof-session=CfDJ8forged");
        using var refused = await newcomer.SendAsync(forged);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/recommendations", true)]
    [InlineData("POST", "/API/Recommendations/", true)]
    [InlineData("POST", "/api/recommendations/check", true)]
    [InlineData("POST", "/api/recommendations/options", true)]
    [InlineData("GET", "/api/optimization/vehicle-chaining?from=x&to=y", true)]
    [InlineData("GET", "/api/trips/99999999/recommendation", false)]
    [InlineData("GET", "/api/trips/99999999/driver-options", false)]
    public async Task HeavyCalculations_AreThese(string method, string url, bool isHeavy)
    {
        await using var factory = Factory(("RateLimiting:Session:Heavy", "1"));
        using var client = await WithSessionAsync(factory);
        await HeavyAsync(client);

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = JsonContent.Create(new { });
        using var response = await client.SendAsync(request);

        Assert.Equal(isHeavy, response.StatusCode == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Refused_IsASpanishProblemThatSaysWhenToRetry()
    {
        await using var factory = Factory(("RateLimiting:Ip:Total", "1"));
        using var client = Browser(factory);
        await ReadAsync(client);

        using var response = await client.GetAsync(ReadUrl);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Demasiadas peticiones", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.InRange(response.Headers.RetryAfter!.Delta!.Value.TotalSeconds, 1, 60);
    }

    [Fact]
    public async Task RefusedWrite_WithoutSession_CountsForTheIp()
    {
        await using var factory = Factory(("RateLimiting:Ip:Total", "2"));
        using var client = Browser(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, await WriteAsync(client));
        Assert.Equal(HttpStatusCode.Unauthorized, await WriteAsync(client));
        Assert.Equal(HttpStatusCode.TooManyRequests, await WriteAsync(client));
    }

    [Theory]
    [InlineData("Testing", "127.0.0.1", true)]
    [InlineData("Testing", "::1", true)]
    [InlineData("Testing", "::ffff:127.0.0.1", true)]
    [InlineData("Testing", Client, false)]
    [InlineData("Production", "127.0.0.1", false)]
    [InlineData("Production", "::1", false)]
    [InlineData("Production", "", false)]
    [InlineData("Staging", "127.0.0.1", false)]
    public async Task Loopback_IsExemptOnlyInDevelopmentAndTesting(string environment, string ip, bool isExempt)
    {
        await using var factory = Factory(environment, ("RateLimiting:Ip:Total", "1"));
        using var client = Browser(factory, ip);
        await ReadAsync(client);

        Assert.Equal(isExempt ? HttpStatusCode.OK : HttpStatusCode.TooManyRequests, await ReadAsync(client));
    }

    [Theory]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:2:ffff::9", true)]
    [InlineData("2001:db8:1:2::1", "2001:db8:1:3::1", false)]
    [InlineData(Client, "::ffff:" + Client, true)]
    public async Task Quota_IsOfTheNetworkAClientOwns(string first, string second, bool isShared)
    {
        await using var factory = Factory(("RateLimiting:Ip:Total", "1"));
        using var one = Browser(factory, first);
        using var two = Browser(factory, second);
        await ReadAsync(one);

        Assert.Equal(isShared ? HttpStatusCode.TooManyRequests : LetThrough, await ReadAsync(two));
    }

    [Theory]
    [InlineData(ResetKey, ResetKey, true)]
    [InlineData(ResetKey, "nope", false)]
    [InlineData(ResetKey, "", false)]
    [InlineData("", "", false)]
    [InlineData("", "anything", false)]
    public async Task ResetKey_ExemptsOnlyWhenItIsTheConfiguredOne(string configured, string received, bool isExempt)
    {
        await using var factory = Factory(("RateLimiting:Ip:Total", "1"), ("Demo:ResetKey", configured));
        using var client = Browser(factory);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Demo-Reset-Key", received);
        await ReadAsync(client);

        Assert.Equal(isExempt ? LetThrough : HttpStatusCode.TooManyRequests, await ReadAsync(client));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task Start_LimitThatLimitsNothing_Fails(string limit)
    {
        await using var factory = Factory(("RateLimiting:Ip:Total", limit));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("RateLimiting", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForwardedFor_NothingConfigured_DoesNotChangeTheQuota()
    {
        // A host may set this variable by itself: on its own it would trust anyone.
        await using var factory = Factory(("RateLimiting:Ip:Total", "1"), ("ForwardedHeaders_Enabled", "true"));
        using var client = Browser(factory);

        Assert.Equal(LetThrough, await ReadForwardedAsync(client, "198.51.100.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await ReadForwardedAsync(client, "198.51.100.2"));
    }

    [Fact]
    public async Task ForwardedFor_FromAnUntrustedNetwork_DoesNotChangeTheQuota()
    {
        await using var factory = Factory(("RateLimiting:Ip:Total", "1"), ("ForwardedHeaders:TrustedNetworks:0", "10.0.0.0/8"));
        using var client = Browser(factory);

        Assert.Equal(LetThrough, await ReadForwardedAsync(client, "198.51.100.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await ReadForwardedAsync(client, "198.51.100.2"));
    }

    [Fact]
    public async Task ForwardedFor_FromTheProxy_IsTheClient()
    {
        await using var factory = Factory(("RateLimiting:Ip:Total", "1"), ("ForwardedHeaders:TrustedNetworks:0", "10.0.0.0/8"));
        using var proxy = Browser(factory, Proxy);

        Assert.Equal(LetThrough, await ReadForwardedAsync(proxy, Client));
        Assert.Equal(LetThrough, await ReadForwardedAsync(proxy, OtherClient));
        Assert.Equal(HttpStatusCode.TooManyRequests, await ReadForwardedAsync(proxy, Client));
    }

    /// <summary>The proxy appends the address it saw; whatever comes before it was written by the client.</summary>
    [Theory]
    [InlineData("198.51.100.{0}, " + Client)]
    [InlineData("10.9.9.{0}, " + Client)]
    public async Task ForwardedFor_WithAnInventedHop_CountsOnlyTheLastOne(string forged)
    {
        await using var factory = Factory(("RateLimiting:Ip:Total", "1"), ("ForwardedHeaders:TrustedNetworks:0", "10.0.0.0/8"));
        using var proxy = Browser(factory, Proxy);

        Assert.Equal(LetThrough, await ReadForwardedAsync(proxy, string.Format(null, forged, 1)));
        Assert.Equal(HttpStatusCode.TooManyRequests, await ReadForwardedAsync(proxy, string.Format(null, forged, 2)));
    }

    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("not a network")]
    [InlineData("")]
    public async Task Start_TrustedNetworkThatTrustsAnyoneOrIsNotOne_Fails(string network)
    {
        await using var factory = Factory(("ForwardedHeaders:TrustedNetworks:0", network));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("ForwardedHeaders:TrustedNetworks", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Behind the proxy the API sees plain HTTP: without the scheme it would redirect forever and give no secure cookie.</summary>
    [Fact]
    public async Task ForwardedProto_FromTheProxy_IsHttps()
    {
        await using var factory = Factory(("ForwardedHeaders:TrustedNetworks:0", "10.0.0.0/8"));
        using var proxy = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        proxy.DefaultRequestHeaders.Add(RemoteIpFromHeader.Name, Proxy);
        proxy.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        using var response = await proxy.GetAsync(ReadUrl);

        Assert.Equal(LetThrough, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    private static async Task<HttpStatusCode> ReadForwardedAsync(HttpClient client, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReadUrl);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}

/// <summary>TestServer has no remote address; each test says where its requests come from. Empty: from nowhere.</summary>
internal sealed class RemoteIpFromHeader : IStartupFilter
{
    public const string Name = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (IPAddress.TryParse(context.Request.Headers[Name], out var address)) context.Connection.RemoteIpAddress = address;
            return nextMiddleware();
        });
        next(app);
    };
}
