namespace DispatchGuard.API.Perimeter;

/// <summary>
/// What the scheduler of the host says to call the API (SPEC 0086). The names of the setting and of the header are the ones Vercel
/// uses: it sends by itself the variable called like that.
/// </summary>
public sealed class CronSecret
{
    private const string Setting = "CRON_SECRET";
    private const string Scheme = "Bearer ";

    private readonly SecretHash? _expected;

    private CronSecret(string? configured) => _expected = SecretHash.Of(configured);

    public bool IsConfigured => _expected is not null;

    public static CronSecret From(IConfiguration configuration) => new(configuration[Setting]);

    public bool IsIn(HttpRequest request) =>
        _expected is not null
        && request.Headers.Authorization.ToString() is var received
        && received.StartsWith(Scheme, StringComparison.Ordinal)
        && _expected.Matches(received[Scheme.Length..]);
}
