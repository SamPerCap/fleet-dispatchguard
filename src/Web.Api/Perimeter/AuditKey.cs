namespace DispatchGuard.API.Perimeter;

/// <summary>The key of the author, to see every visit (SPEC 0085 R7). Without one configured, no request has it.</summary>
public sealed class AuditKey
{
    public const string Header = "X-Audit-Key";

    private const string Setting = "Audit:Key";

    private readonly SecretHash? _expected;

    private AuditKey(string? configured) => _expected = SecretHash.Of(configured);

    public static AuditKey From(IConfiguration configuration) => new(configuration[Setting]);

    public bool IsIn(HttpRequest request) =>
        _expected is not null
        && request.Headers.TryGetValue(Header, out var received)
        && _expected.Matches(received.ToString());
}
