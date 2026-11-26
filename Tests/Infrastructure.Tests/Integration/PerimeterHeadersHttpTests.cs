using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0089 R4 and R5.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PerimeterHeadersHttpTests(PostgreSqlFixture db)
{
    private WebApplicationFactory<Program> Factory(string environment, string? allowedHosts = "localhost") =>
        new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSessionKey();
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
            if (allowedHosts is not null) builder.UseSetting("AllowedHosts", allowedHosts);
        });

    private static HttpClient Https(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static async Task<HttpClient> WithSessionAsync(WebApplicationFactory<Program> factory)
    {
        var client = Https(factory);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/session")).StatusCode);
        return client;
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single() : "(missing)";

    /// <summary>The exception handler wipes the headers of an answer before writing its own: errors are cases of their own.</summary>
    [Theory]
    [InlineData("GET", "/api/bases", 200)]
    [InlineData("GET", "/api/drivers/999999", 404)]
    [InlineData("GET", "/api/trips?from=2030-01-02&to=2030-01-01", 400)]
    [InlineData("GET", "/nothing/here", 404)]
    [InlineData("POST", "/api/bases", 403)]
    public async Task Production_EveryAnswerCarriesTheHeaders(string method, string url, int expected)
    {
        await using var factory = Factory("Production");
        using var client = await WithSessionAsync(factory);
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request);

        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("no-referrer", Header(response, "Referrer-Policy"));
        Assert.Equal("default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'", Header(response, "Content-Security-Policy"));
        Assert.Contains("geolocation=()", Header(response, "Permissions-Policy"), StringComparison.Ordinal);
        Assert.Equal("max-age=31536000", Header(response, "Strict-Transport-Security"));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Development")]
    public async Task DevelopmentAndTesting_NoHsts(string environment)
    {
        await using var factory = Factory(environment, allowedHosts: null);
        using var client = await WithSessionAsync(factory);

        using var response = await client.GetAsync("/api/bases");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
        Assert.StartsWith("default-src 'none'", Header(response, "Content-Security-Policy"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/swagger-ui.css")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Development_SwaggerIsNotBlockedByTheCsp(string url)
    {
        await using var factory = Factory("Development", allowedHosts: null);
        using var client = Https(factory);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    /// <summary>TestServer never sends it: what is checked is what the real server is told.</summary>
    [Fact]
    public async Task Kestrel_DoesNotSayWhatItIs()
    {
        await using var factory = Factory("Production");

        Assert.False(factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader);
    }

    [Theory]
    [InlineData("Production", "*")]
    [InlineData("Production", "")]
    [InlineData("Production", " ; ")]
    [InlineData("Production", "demo.example;*")]
    [InlineData("Production", "0.0.0.0")]
    [InlineData("Production", "[::]")]
    [InlineData("Production", null)]
    [InlineData("Staging", "*")]
    public async Task Start_AnyHostOutsideDevelopment_Fails(string environment, string? allowedHosts)
    {
        await using var factory = Factory(environment, allowedHosts);

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("AllowedHosts", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("evil.example", HttpStatusCode.BadRequest)]
    public async Task Production_OnlyTheNamedHosts(string host, HttpStatusCode expected)
    {
        await using var factory = Factory("Production");
        using var client = Https(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Host = host;

        using var response = await client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }
}
