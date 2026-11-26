using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0091 R2 and R3. Without a database: the pages must be served while it is asleep.</summary>
public sealed class WebPagesHttpTests(WebPagesHttpTests.Installation installation) : IClassFixture<WebPagesHttpTests.Installation>
{
    private const string Index = "<!doctype html><title>Fleet DispatchGuard</title>";

    private const string PagePolicy =
        "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self'; connect-src 'self'; " +
        "base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    private const string ClosedPolicy = "default-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    /// <summary>One API for all the tests that can share it: without database, each start waits for it.</summary>
    public sealed class Installation : IDisposable
    {
        private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("dispatchguard-web-");

        public Installation()
        {
            var webRoot = _folder.CreateSubdirectory("wwwroot").FullName;
            var assets = Directory.CreateDirectory(Path.Combine(webRoot, "assets")).FullName;
            File.WriteAllText(Path.Combine(webRoot, "index.html"), Index);
            File.WriteAllText(Path.Combine(assets, "index-CSc0zpO4.js"), "export const answer = 42;");
            File.WriteAllBytes(Path.Combine(assets, "inter-Dx4kXJAl.woff2"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(_folder.FullName, "appsettings.json"), "secret");
            Api = Start(webRoot);
            WebRoot = webRoot;
        }

        public WebApplicationFactory<Program> Api { get; }

        public string WebRoot { get; }

        public string EmptyFolder => _folder.CreateSubdirectory("empty").FullName;

        public void Dispose()
        {
            Api.Dispose();
            _folder.Delete(recursive: true);
        }
    }

    private static WebApplicationFactory<Program> Start(string webRoot, params (string Key, string Value)[] settings) =>
        new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSessionKey();
            builder.UseSetting(WebHostDefaults.WebRootKey, webRoot);
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", UnreachableDatabase.ConnectionString);
            builder.UseSetting("AllowedHosts", "localhost");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        });

    private static HttpClient Browser(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.Single() : "(missing)";

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/reservas")]
    [InlineData("/reservas/12/viajes")]
    [InlineData("/apifoo")]
    [InlineData("/assets")]
    public async Task Page_AnyPathOutsideTheApi_IsTheIndexAndIsNeverCached(string path)
    {
        using var client = Browser(installation.Api);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Index, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Theory]
    [InlineData("GET", "/api")]
    [InlineData("GET", "/api/nothing")]
    [InlineData("GET", "/API/nothing")]
    [InlineData("GET", "/api/nothing/index.html")]
    [InlineData("GET", "/health/nothing")]
    [InlineData("GET", "/assets/index-of-the-previous-version.js")]
    [InlineData("GET", "/.env")]
    [InlineData("POST", "/reservas")]
    public async Task NotFound_InTheApiOrAMissingFile_IsAProblemAndNotTheIndex(string method, string path)
    {
        using var client = Browser(installation.Api);
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ClosedPolicy, Header(response, "Content-Security-Policy"));
    }

    [Theory]
    [InlineData("/assets/index-CSc0zpO4.js", "text/javascript")]
    [InlineData("/assets/inter-Dx4kXJAl.woff2", "font/woff2")]
    public async Task HashedFile_IsCachedForAYearAndHasTheClosedPolicy(string path, string contentType)
    {
        using var client = Browser(installation.Api);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("public, max-age=31536000, immutable", Header(response, "Cache-Control"));
        Assert.Equal(ClosedPolicy, Header(response, "Content-Security-Policy"));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/reservas")]
    public async Task Page_Policy_AllowsOnlyWhatTheWebIsMadeOf(string path)
    {
        using var client = Browser(installation.Api);

        using var response = await client.GetAsync(path);

        Assert.Equal(PagePolicy, Header(response, "Content-Security-Policy"));
        Assert.Equal("nosniff", Header(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Header(response, "X-Frame-Options"));
        Assert.Equal("max-age=31536000", Header(response, "Strict-Transport-Security"));
    }

    /// <summary>A browser replaces the headers it has stored with those of the 304: the closed policy would break the page on reload.</summary>
    [Fact]
    public async Task Page_NotModified_CarriesThePolicyOfThePage()
    {
        using var client = Browser(installation.Api);
        using var first = await client.GetAsync("/");
        using var again = new HttpRequestMessage(HttpMethod.Get, "/");
        again.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(first.Headers.ETag!.Tag));

        using var response = await client.SendAsync(again);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Equal(PagePolicy, Header(response, "Content-Security-Policy"));
        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Files_AreOutsideTheLimits()
    {
        await using var factory = Start(installation.WebRoot, ("RateLimiting:Ip:Total", "1"));
        using var client = Browser(factory);

        foreach (var path in new[] { "/", "/assets/index-CSc0zpO4.js", "/reservas", "/", "/assets/index-CSc0zpO4.js" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/assets/index-CSc0zpO4.js")]
    public async Task Head_AnswersWithoutBody(string path)
    {
        using var client = Browser(installation.Api);
        using var request = new HttpRequestMessage(HttpMethod.Head, path);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("/../appsettings.json")]
    [InlineData("/%2e%2e/appsettings.json")]
    [InlineData("/assets/..%2f..%2fappsettings.json")]
    public async Task File_OutsideTheWeb_IsNeverServed(string path)
    {
        using var client = Browser(installation.Api);

        using var response = await client.GetAsync(path);

        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>As in development, where Vite serves the web.</summary>
    [Fact]
    public async Task WithoutWeb_UnknownPathIsNotFound()
    {
        await using var factory = Start(installation.EmptyFolder);
        using var client = Browser(factory);

        using var response = await client.GetAsync("/reservas");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ClosedPolicy, Header(response, "Content-Security-Policy"));
    }
}
