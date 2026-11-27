using Microsoft.Extensions.Configuration;

namespace DispatchGuard.Infrastructure.Persistence;

public static class MainConnectionString
{
    public const string Name = "DispatchGuardDb";

    public static string? Read(IConfiguration configuration)
    {
        var current = configuration.GetConnectionString(Name);
        return string.IsNullOrWhiteSpace(current) ? null : current;
    }
}
