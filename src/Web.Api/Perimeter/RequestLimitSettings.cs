namespace DispatchGuard.API.Perimeter;

/// <summary>Requests of one session in each window.</summary>
public sealed record SessionLimits
{
    public int Reads { get; init; } = 300;

    /// <summary>Enough to apply the 100 services of "Asignar semana" without waiting.</summary>
    public int Writes { get; init; } = 150;

    public int Heavy { get; init; } = 20;

    /// <summary>The web sends a few; whoever sends more is not the web.</summary>
    public int VisitBatches { get; init; } = 30;
}

/// <summary>Requests of one address in each window: the real ceiling, because a session can be thrown away.</summary>
public sealed record IpLimits
{
    public int Total { get; init; } = 600;

    public int Heavy { get; init; } = 60;

    public int NewSessions { get; init; } = 10;
}

public sealed record RequestLimitSettings
{
    private const string Section = "RateLimiting";

    public int WindowSeconds { get; init; } = 60;

    public SessionLimits Session { get; init; } = new();

    public IpLimits Ip { get; init; } = new();

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);

    /// <exception cref="InvalidOperationException">A value under 1: it would not limit anything, or would let nothing through.</exception>
    public static RequestLimitSettings From(IConfiguration configuration)
    {
        var settings = configuration.GetSection(Section).Get<RequestLimitSettings>() ?? new();
        int[] values =
        [
            settings.WindowSeconds, settings.Session.Reads, settings.Session.Writes, settings.Session.Heavy, settings.Session.VisitBatches,
            settings.Ip.Total, settings.Ip.Heavy, settings.Ip.NewSessions
        ];
        return values.All(value => value >= 1)
            ? settings
            : throw new InvalidOperationException($"Every value of {Section} must be 1 or more");
    }
}
