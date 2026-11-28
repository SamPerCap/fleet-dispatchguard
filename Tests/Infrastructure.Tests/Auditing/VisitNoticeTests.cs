using System.Collections.Concurrent;
using System.Net;
using DispatchGuard.API.Auditing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Infrastructure.Tests.Auditing;

/// <summary>SPEC 0103 R3–R4: one notice per visitor every 30 minutes, with the page and nothing else.</summary>
public sealed class VisitNoticeTests : IDisposable
{
    private readonly Board _board = new();
    private readonly Clock _clock = new();
    private readonly VisitNotice _notice;

    public VisitNoticeTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Visits:NoticeUrl"] = "https://notices.test/secret-topic" })
            .Build();
        _notice = new VisitNotice(VisitSettings.From(configuration), NullLogger<VisitNotice>.Instance, _board, _clock);
    }

    public void Dispose() => _notice.Dispose();

    private sealed class Board : HttpMessageHandler
    {
        public ConcurrentQueue<string> Received { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Received.Enqueue(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Theory]
    [InlineData(OpenedPage.Cover, "Visita en Fleet DispatchGuard · portada")]
    [InlineData(OpenedPage.Engineering, "Visita en Fleet DispatchGuard · cómo está hecho")]
    [InlineData(OpenedPage.About, "Visita en Fleet DispatchGuard · sobre mí")]
    [InlineData(OpenedPage.Application, "Visita en Fleet DispatchGuard · herramienta")]
    public async Task Opened_SaysThePageAndNothingElse(OpenedPage page, string text)
    {
        await _notice.SendOpenedAsync(page, "203.0.113.7");

        Assert.Equal([text], _board.Received);
    }

    [Fact]
    public async Task Opened_AgainWithinTheWindow_IsNotSentTwice()
    {
        await _notice.SendOpenedAsync(OpenedPage.Cover, "203.0.113.7");
        _clock.Now += VisitNotice.SameVisitor - TimeSpan.FromMinutes(1);
        await _notice.SendOpenedAsync(OpenedPage.About, "203.0.113.7");

        Assert.Single(_board.Received);
    }

    [Fact]
    public async Task Opened_BackAfterTheWindow_IsSentAgain()
    {
        await _notice.SendOpenedAsync(OpenedPage.Cover, "203.0.113.7");
        _clock.Now += VisitNotice.SameVisitor;
        await _notice.SendOpenedAsync(OpenedPage.Application, "203.0.113.7");

        Assert.Equal(2, _board.Received.Count);
    }

    [Fact]
    public async Task Opened_TwoVisitors_EachIsSent()
    {
        await _notice.SendOpenedAsync(OpenedPage.Cover, "203.0.113.7");
        await _notice.SendOpenedAsync(OpenedPage.Cover, "198.51.100.4");

        Assert.Equal(2, _board.Received.Count);
    }

    [Fact]
    public async Task Opened_WithoutNoticeUrl_SendsNothing()
    {
        using var silent = new VisitNotice(
            VisitSettings.From(new ConfigurationBuilder().Build()), NullLogger<VisitNotice>.Instance, _board, _clock);

        await silent.SendOpenedAsync(OpenedPage.Cover, "203.0.113.7");

        Assert.Empty(_board.Received);
    }
}
