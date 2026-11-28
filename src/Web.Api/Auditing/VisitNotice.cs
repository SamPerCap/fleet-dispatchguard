using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using DispatchGuard.Core.Auditing;

namespace DispatchGuard.API.Auditing;

public sealed class VisitSettings
{
    private const string ReferencesSetting = "Visits:References";
    private const string NoticeUrlSetting = "Visits:NoticeUrl";

    private readonly HashSet<string> _references;

    private VisitSettings(HashSet<string> references, Uri? noticeUrl)
    {
        _references = references;
        NoticeUrl = noticeUrl;
    }

    /// <summary>Null when the author does not want to be told.</summary>
    public Uri? NoticeUrl { get; }

    /// <exception cref="InvalidOperationException">A setting that would never work.</exception>
    public static VisitSettings From(IConfiguration configuration)
    {
        var references = (configuration[ReferencesSetting] ?? string.Empty)
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (references.Any(reference => reference.Length > Visit.ReferenceMaxLength))
        {
            throw new InvalidOperationException($"The codes of {ReferencesSetting} have {Visit.ReferenceMaxLength} characters at most");
        }

        var url = configuration[NoticeUrlSetting];
        Uri? address = null;
        if (!string.IsNullOrWhiteSpace(url) && !(Uri.TryCreate(url, UriKind.Absolute, out address) && address.Scheme == Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{NoticeUrlSetting} must be an https address");
        }

        return new VisitSettings(references.ToHashSet(StringComparer.OrdinalIgnoreCase), address);
    }

    /// <summary>As it is written in the settings; null if it is not one of them.</summary>
    public string? KnownReference(string? received) =>
        received is not null && _references.TryGetValue(received.Trim(), out var known) ? known : null;
}

/// <summary>The pages a visitor can open; anything else is refused (SPEC 0103 R7).</summary>
public enum OpenedPage
{
    Cover,
    Engineering,
    About,
    Application
}

/// <summary>
/// Tells the author that someone opened the site (SPEC 0103). A plain text to an address that is itself the secret, so it is never
/// written in a log. It reads no database: opening the cover must not wake it up.
/// </summary>
public sealed class VisitNotice : IDisposable
{
    /// <summary>The same visitor within this time is the same visit; after it, coming back is news (SPEC 0103 R3).</summary>
    public static readonly TimeSpan SameVisitor = TimeSpan.FromMinutes(30);

    /// <summary>Whoever opens the site in bulk does not fill the telephone of the author.</summary>
    private const int MostInAnHour = 30;

    /// <summary>Past this many visitors remembered, the stale ones are forgotten: memory stays small whatever the traffic.</summary>
    private const int VisitorsBeforeCleaning = 1_000;

    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(3);

    private readonly VisitSettings _settings;
    private readonly ILogger<VisitNotice> _logger;
    private readonly TimeProvider _clock;
    private readonly HttpClient _client;
    private readonly FixedWindowRateLimiter _limit;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastNoticeOf = new();

    // Only to recognise the same address within this process: it is never stored nor sent, and changes on every start.
    private readonly byte[] _salt = RandomNumberGenerator.GetBytes(32);

    public VisitNotice(VisitSettings settings, ILogger<VisitNotice> logger, HttpMessageHandler? handler = null, TimeProvider? clock = null)
    {
        _settings = settings;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = LongestWait };
        _limit = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = MostInAnHour, Window = TimeSpan.FromHours(1), QueueLimit = 0
        });
    }

    /// <summary>It never fails: opening a page does not depend on it.</summary>
    public async Task SendOpenedAsync(OpenedPage page, string visitorAddress)
    {
        if (_settings.NoticeUrl is not { } address || !IsNewVisit(VisitorOf(visitorAddress)))
        {
            return;
        }

        using var permit = _limit.AttemptAcquire();
        if (!permit.IsAcquired)
        {
            return;
        }

        try
        {
            using var content = new StringContent($"Visita en Fleet DispatchGuard · {NameOf(page)}");
            using var response = await _client.PostAsync(address, content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("The notice of a visit was answered {Status}", (int)response.StatusCode);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning("The notice of a visit could not be sent: {Reason}", exception.GetType().Name);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _limit.Dispose();
    }

    // Only the author reads it, so it is in Spanish like the rest of the private audit.
    private static string NameOf(OpenedPage page) => page switch
    {
        OpenedPage.Cover => "portada",
        OpenedPage.Engineering => "cómo está hecho",
        OpenedPage.About => "sobre mí",
        _ => "herramienta"
    };

    private string VisitorOf(string address) =>
        Convert.ToHexString(SHA256.HashData([.. _salt, .. Encoding.UTF8.GetBytes(address)]));

    private bool IsNewVisit(string visitor)
    {
        var now = _clock.GetUtcNow();
        if (_lastNoticeOf.Count > VisitorsBeforeCleaning)
        {
            ForgetVisitorsBefore(now - SameVisitor);
        }

        var isNew = true;
        _lastNoticeOf.AddOrUpdate(
            visitor,
            now,
            (_, last) =>
            {
                isNew = now - last >= SameVisitor;
                return isNew ? now : last;
            });
        return isNew;
    }

    private void ForgetVisitorsBefore(DateTimeOffset limit)
    {
        foreach (var (visitor, last) in _lastNoticeOf)
        {
            if (last < limit)
            {
                _lastNoticeOf.TryRemove(visitor, out _);
            }
        }
    }
}
