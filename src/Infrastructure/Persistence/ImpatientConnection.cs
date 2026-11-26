using Npgsql;

namespace DispatchGuard.Infrastructure.Persistence;

/// <summary>
/// For what is asked while the database may be waking up: it fails at once instead of retrying for a minute, as the business context does.
/// </summary>
internal static class ImpatientConnection
{
    public const int TimeoutSeconds = 5;

    public static NpgsqlConnectionStringBuilder To(string? connectionString) => new(connectionString)
    {
        Timeout = TimeoutSeconds
    };
}
