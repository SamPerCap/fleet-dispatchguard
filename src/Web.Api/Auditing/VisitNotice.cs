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

/// <summary>
/// Tells the author that a visit has started (SPEC 0085 R9). A plain text to an address that is itself the secret, so it is never
/// written in a log.
/// </summary>
public sealed class VisitNotice(VisitSettings settings, ILogger<VisitNotice> logger, HttpMessageHandler? handler = null) : IDisposable
{
    /// <summary>Whoever starts sessions in bulk does not fill the telephone of the author.</summary>
    private const int MostInAnHour = 30;

    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(3);

    private readonly HttpClient _client = new(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = LongestWait };

    private readonly FixedWindowRateLimiter _limit = new(new FixedWindowRateLimiterOptions
    {
        PermitLimit = MostInAnHour, Window = TimeSpan.FromHours(1), QueueLimit = 0
    });

    /// <summary>It never fails: a visit does not depend on it.</summary>
    public async Task SendAsync(string shortSessionId, string? reference)
    {
        using var permit = _limit.AttemptAcquire();
        if (settings.NoticeUrl is not { } address || !permit.IsAcquired)
        {
            return;
        }

        try
        {
            var origin = reference is null ? "sin enlace de candidatura" : $"candidatura: {reference}";
            using var content = new StringContent($"Visita nueva en Fleet DispatchGuard · {origin} · sesión {shortSessionId}");
            using var response = await _client.PostAsync(address, content);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("The notice of a visit was answered {Status}", (int)response.StatusCode);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning("The notice of a visit could not be sent: {Reason}", exception.GetType().Name);
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _limit.Dispose();
    }
}
