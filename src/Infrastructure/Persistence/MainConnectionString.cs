using Microsoft.Extensions.Configuration;

namespace DispatchGuard.Infrastructure.Persistence;

public static class MainConnectionString
{
    public const string Name = "DispatchGuardDb";

    /// <summary>
    /// A release and the version before it read the same settings, and going back must still find them (SPEC 0094 R3): it leaves with
    /// the release after the one that renamed it.
    /// </summary>
    public const string PreviousName = "BetterBusDb";

    public static string? Read(IConfiguration configuration)
    {
        var current = configuration.GetConnectionString(Name);
        return string.IsNullOrWhiteSpace(current) ? configuration.GetConnectionString(PreviousName) : current;
    }
}
