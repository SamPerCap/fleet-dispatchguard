using DispatchGuard.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DispatchGuard.Infrastructure.Persistence;

/// <summary>
/// Whoever asks while another answer is on its way gets that one: while the database wakes up every visitor asks at once
/// (SPEC 0091), and each question must not cost a connection.
/// </summary>
internal sealed class DatabaseReadiness(string? connectionString, ILogger<DatabaseReadiness> logger) : IDatabaseReadiness
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(ImpatientConnection.TimeoutSeconds);

    private readonly Lock _gate = new();
    private Task<bool>? _answer;

    public Task<bool> IsReadyAsync()
    {
        lock (_gate)
        {
            if (_answer is null or { IsCompleted: true })
            {
                _answer = Task.Run(AskAsync);
            }

            return _answer;
        }
    }

    private async Task<bool> AskAsync()
    {
        using var deadline = new CancellationTokenSource(Limit);
        try
        {
            // The driver does not look at the token in every step of a connection.
            await QueryAsync(deadline.Token).WaitAsync(Limit);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning("The database is not ready: {Reason}", exception.Message);
            return false;
        }
    }

    private async Task QueryAsync(CancellationToken cancellationToken)
    {
        // Without a pool: an idle connection would look like a user to a database that goes to sleep when nobody uses it.
        var settings = ImpatientConnection.To(connectionString);
        settings.Pooling = false;
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
