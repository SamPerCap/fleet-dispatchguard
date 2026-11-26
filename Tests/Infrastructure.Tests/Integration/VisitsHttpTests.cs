using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.API.Auditing;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0085 R1 and R5–R10, as a browser and the author see it.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class VisitsHttpTests(PostgreSqlFixture server) : IAsyncLifetime
{
    private const string Key = "the-audit-key-of-the-test";
    private const string Trace = "0af7651916cd43dd8448eb211c80319c";
    private const string FollowsAnAction = $"00-{Trace}-b7ad6b7169203331-01";
    private const string RefreshesByItself = $"00-{Trace}-b7ad6b7169203331-00";
    private const string Dni = "22222222J";

    private readonly ConcurrencyDatabase _database = new(server);
    private readonly NoticeBoard _notices = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    /// <summary>The service that takes the notices to the author.</summary>
    private sealed class NoticeBoard : HttpMessageHandler
    {
        public ConcurrentQueue<string> Received { get; } = [];

        public bool IsDown { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Received.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
            return IsDown ? throw new HttpRequestException("down") : new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private WebApplicationFactory<Program> Factory(string? key = Key) => new SpanishApiFactory().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Production");
        builder.UseSessionKey();
        builder.UseSetting("ConnectionStrings:DispatchGuardDb", _database.ConnectionString);
        builder.UseSetting("AllowedHosts", "localhost");
        builder.UseSetting("Visits:References", "acme; Globex");
        builder.UseSetting("Visits:NoticeUrl", "https://notices.test/secret-topic");
        if (key is not null) builder.UseSetting("Audit:Key", key);
        builder.ConfigureTestServices(services => services.AddSingleton(provider =>
            new VisitNotice(provider.GetRequiredService<VisitSettings>(), NullLogger<VisitNotice>.Instance, _notices)));
    });

    private static async Task<HttpClient> BrowserAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-DispatchGuard-Request", "1");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/session")).StatusCode);
        return client;
    }

    private static object Screen(string name = "Centro de control", string? trace = null) => new { AgoMs = 10, Kind = "Screen", Name = name, TraceId = trace };

    private static Task<HttpResponseMessage> SendBatchAsync(HttpClient client, string? reference = null, params object[] events) =>
        client.PostAsJsonAsync("/api/visits", new { Reference = reference, Events = events.Length > 0 ? events : [Screen()] });

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body = null, params (string Name, string Value)[] headers)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> AsAuthorAsync(HttpClient client, string url) =>
        await JsonAsync(await SendAsync(client, HttpMethod.Get, url, null, ("X-Audit-Key", Key)));

    /// <summary>Of the only visit there is.</summary>
    private static async Task<JsonElement> TimelineAsync(HttpClient client)
    {
        var visit = Assert.Single((await AsAuthorAsync(client, "/api/audit/visits")).EnumerateArray());
        return await AsAuthorAsync(client, $"/api/audit/visits/{visit.GetProperty("SessionId").GetString()}");
    }

    private static async Task<List<JsonElement>> EntriesAsync(HttpClient client) =>
        [.. (await TimelineAsync(client)).GetProperty("Entries").EnumerateArray()];

    private static object NewDriver(string dni = Dni) => new { IdentificationNumber = dni, FirstName = "Ana", LastName = "Prueba" };

    private DispatchGuardDbContext Context() => _database.CreateContext();

    /// <summary>CA2. The second name comes from a page opened before the rename (SPEC 0094 R3).</summary>
    [Theory]
    [InlineData("X-DispatchGuard-Name")]
    [InlineData("X-LOF-Name")]
    public async Task Action_ItsRequestAndItsChange_ShareTheThread(string nameHeader)
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);
        await SendBatchAsync(client, null, new { AgoMs = 5, Kind = "Click", Name = "Guardar", TraceId = Trace });

        using var created = await SendAsync(
            client, HttpMethod.Post, "/api/drivers", NewDriver(), (RequestThreadHeader, FollowsAnAction), (nameHeader, "Luc%C3%ADa"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var entries = await EntriesAsync(client);
        Assert.All(entries, entry => Assert.Equal(Trace, entry.GetProperty("TraceId").GetString()));
        Assert.Equal(["Click", "Created", "Request"], entries.Select(entry => entry.GetProperty("Kind").GetString()!).Order());
        var change = entries.Single(entry => entry.GetProperty("Kind").GetString() == "Created");
        Assert.Equal("Lucía", change.GetProperty("DeclaredName").GetString());
        Assert.Equal("POST /api/drivers", entries.Single(entry => entry.GetProperty("Kind").GetString() == "Request").GetProperty("Name").GetString());
    }

    private const string RequestThreadHeader = "traceparent";

    /// <summary>CA3.</summary>
    [Fact]
    public async Task Trace_HasTheQueriesAndNotTheirValues()
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);
        await SendAsync(client, HttpMethod.Post, "/api/drivers", NewDriver(), (RequestThreadHeader, FollowsAnAction));
        await SendAsync(client, HttpMethod.Get, $"/api/drivers?search={Dni}", null, (RequestThreadHeader, FollowsAnAction));

        await using var context = Context();
        var traces = await context.RequestTraces.AsNoTracking().ToListAsync();
        Assert.Equal(2, traces.Count);
        Assert.All(traces, trace =>
        {
            Assert.Contains("SELECT", trace.Detail, StringComparison.Ordinal);
            Assert.True(trace.Queries > 0);
            Assert.DoesNotContain(Dni, trace.Path + trace.Detail, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(Dni, string.Concat(context.AuditEvents.Select(e => e.Changes)), StringComparison.Ordinal);
        var id = traces[0].Id;
        var shown = await AsAuthorAsync(client, $"/api/audit/requests/{id}");
        Assert.True(shown.GetProperty("Detail").GetProperty("Queries").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Trace_OfARefresh_IsOnlyKeptWhenItFails()
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);

        await SendAsync(client, HttpMethod.Get, "/api/drivers", null, (RequestThreadHeader, RefreshesByItself));
        await SendAsync(client, HttpMethod.Get, "/api/drivers/999999", null, (RequestThreadHeader, RefreshesByItself));

        await using var context = Context();
        var trace = Assert.Single(context.RequestTraces);
        Assert.Equal(StatusCodes.NotFound, trace.Status);
        Assert.Equal("/api/drivers/999999", trace.Path);
    }

    private static class StatusCodes
    {
        public const int NotFound = 404;
    }

    /// <summary>It would wake up a database that is asleep.</summary>
    [Fact]
    public async Task Trace_OfARequestThatDoesNotUseTheDatabase_IsNotKept()
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);

        await SendAsync(client, HttpMethod.Get, "/api/rules", null, (RequestThreadHeader, FollowsAnAction));
        await SendAsync(client, HttpMethod.Get, "/api/audit/visits", null, (RequestThreadHeader, FollowsAnAction));

        await using var context = Context();
        Assert.Empty(context.RequestTraces);
    }

    [Fact]
    public async Task Trace_ThatCannotBeKept_DoesNotSpoilTheRequest()
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);
        await using (var context = Context())
        {
            await context.Database.ExecuteSqlAsync($"""DROP TABLE "RequestTraces" """);
        }

        using var response = await SendAsync(client, HttpMethod.Get, "/api/drivers", null, (RequestThreadHeader, FollowsAnAction));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Notice_IsSentOnceForEachSession()
    {
        await using var factory = Factory();
        await using var otherInstance = Factory();
        using var client = await BrowserAsync(factory);
        using var another = await BrowserAsync(otherInstance);

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.NoContent, (await SendBatchAsync(client, "acme")).StatusCode);
        await SendBatchAsync(another);

        Assert.Equal(2, _notices.Received.Count);
        Assert.Contains("candidatura: acme", _notices.Received.First(), StringComparison.Ordinal);
        Assert.Contains("sin enlace", _notices.Received.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notice_ThatFails_DoesNotSpoilTheVisit()
    {
        _notices.IsDown = true;
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);

        using var response = await SendBatchAsync(client, "acme");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotEmpty(await EntriesAsync(client));
    }

    [Theory]
    [InlineData("acme", "acme")]
    [InlineData("GLOBEX", "Globex")]
    [InlineData("unknown", null)]
    [InlineData("<script>", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task Reference_OnlyAKnownOneIsKept(string? received, string? kept)
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);

        using var response = await SendBatchAsync(client, received);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var context = Context();
        Assert.Equal(kept, context.Visits.Single().Reference);
    }

    /// <summary>The link was opened later in a browser that already had a session.</summary>
    [Fact]
    public async Task Reference_OfASessionThatHadNone_ReachesTheAuthor()
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);
        await SendBatchAsync(client);

        await SendBatchAsync(client, "acme");

        var visits = await AsAuthorAsync(client, "/api/audit/visits");
        Assert.Equal("acme", visits[0].GetProperty("Reference").GetString());
    }

    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("<b>")]
    [InlineData("")]
    [InlineData("a\nb")]
    public async Task Event_WithANameThatIsNotOfTheList_IsRefused(string name)
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);

        using var response = await SendBatchAsync(client, null, Screen(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Batch_TooLong_IsRefused()
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);

        using var response = await SendBatchAsync(client, null, [.. Enumerable.Repeat(Screen(), 51)]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Batches_HaveAQuotaOfTheirOwn()
    {
        await using var factory = Factory().WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:Session:VisitBatches", "1"));
        using var client = await BrowserAsync(factory);

        Assert.Equal(HttpStatusCode.NoContent, (await SendBatchAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await SendBatchAsync(client)).StatusCode);
        using var created = await SendAsync(client, HttpMethod.Post, "/api/drivers", NewDriver());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    /// <summary>CA4. Not even its own visit, and without a hint that there is something to open.</summary>
    [Fact]
    public async Task Visitor_SeesNothingOfWhatIsRecorded()
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);
        await SendBatchAsync(client);
        await SendAsync(client, HttpMethod.Post, "/api/drivers", NewDriver(), (RequestThreadHeader, FollowsAnAction));
        var own = await TimelineAsync(client);
        var requestId = own.GetProperty("Entries").EnumerateArray()
            .Single(entry => entry.GetProperty("Kind").GetString() == "Request").GetProperty("RequestId").GetInt64();

        string[] urls =
        [
            "/api/audit/session",
            "/api/audit/visits",
            $"/api/audit/visits/{own.GetProperty("SessionId").GetString()}",
            $"/api/audit/requests/{requestId}",
        ];
        foreach (var url in urls)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, url, null, ("X-Audit-Key", Key + "x"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, url, null, ("X-Audit-Key", ""))).StatusCode);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Audit_WithoutKeyConfigured_DoesNotExist(string? key)
    {
        await using var factory = Factory(key);
        using var client = await BrowserAsync(factory);

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(client, HttpMethod.Get, "/api/audit/visits", null, ("X-Audit-Key", key ?? ""))).StatusCode);
    }

    [Fact]
    public async Task List_ShowsEachVisitWithItsTimeline()
    {
        await using var factory = Factory();
        using var client = await BrowserAsync(factory);
        await SendBatchAsync(client, "acme", Screen(), Screen("Planificación"));
        await SendAsync(client, HttpMethod.Post, "/api/drivers", NewDriver(), (RequestThreadHeader, FollowsAnAction));

        var visit = (await AsAuthorAsync(client, "/api/audit/visits"))[0];

        Assert.Equal(3, visit.GetProperty("Events").GetInt32());
        Assert.Equal(1, visit.GetProperty("Changes").GetInt32());
        var timeline = await TimelineAsync(client);
        Assert.Equal(5, timeline.GetProperty("Entries").GetArrayLength());
        Assert.Equal("acme", timeline.GetProperty("Reference").GetString());
    }

    [Theory]
    [InlineData("Visits:NoticeUrl", "http://notices.test/topic")]
    [InlineData("Visits:NoticeUrl", "not an address")]
    [InlineData("Visits:References", "a-code-that-is-much-longer-than-it-can-be")]
    public async Task Start_WithASettingThatWouldNeverWork_Fails(string setting, string value)
    {
        await using var factory = Factory().WithWebHostBuilder(builder => builder.UseSetting(setting, value));

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(setting, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Detail_OfAHugeOperation_FitsInItsSize()
    {
        var operation = new OperationTrace();
        for (var i = 0; i < 100; i++) operation.Queried($"SELECT {new string('x', 900)} {i}", TimeSpan.FromMilliseconds(1));
        operation.Evaluated("daily", passed: true);

        var detail = RequestTraceDetail.Of(operation);

        Assert.InRange(detail.Length, 1_000, RequestTrace.DetailMaxLength);
        Assert.Contains("daily", detail, StringComparison.Ordinal);
        Assert.Equal(100, operation.QueryCount);
    }
}
