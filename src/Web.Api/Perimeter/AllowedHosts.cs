namespace DispatchGuard.API.Perimeter;

/// <summary>The framework filters the hosts; here it is only checked that there is something to filter.</summary>
public static class AllowedHosts
{
    private const string Setting = "AllowedHosts";

    /// <summary>All of them mean any host to the framework.</summary>
    private static readonly string[] AnyHost = ["*", "[::]", "0.0.0.0"];

    /// <exception cref="InvalidOperationException">Outside Development and Testing the API would answer to any host.</exception>
    public static void Require(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopmentOrTesting())
        {
            return;
        }

        var hosts = (configuration[Setting] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0 || hosts.Any(host => AnyHost.Contains(host, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException($"{Setting} must name the hosts of the API in the {environment.EnvironmentName} environment");
        }
    }
}
