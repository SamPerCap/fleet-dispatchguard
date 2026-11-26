namespace DispatchGuard.API.Perimeter;

/// <summary>The key of whoever resets and seeds the demo (ADR 0014). Without one configured, no request has it.</summary>
public sealed class DemoResetKey
{
    private const string Header = "X-Demo-Reset-Key";
    private const string Setting = "Demo:ResetKey";

    private readonly SecretHash? _expected;

    private DemoResetKey(string? configured) => _expected = SecretHash.Of(configured);

    public bool IsConfigured => _expected is not null;

    public static DemoResetKey From(IConfiguration configuration) => new(configuration[Setting]);

    public bool IsIn(HttpRequest request) =>
        _expected is not null
        && request.Headers.TryGetValue(Header, out var received)
        && _expected.Matches(received.ToString());
}
