using System.Data.Common;
using System.Globalization;
using DispatchGuard.Core.Entities;
using DispatchGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// Own database in the shared container, created by the migrations like a real one, and a way to ask the server who is waiting.
/// Tests do not depend on timing: they stop one request at a known point and watch the other (SPEC 0065).
/// </summary>
public sealed class ConcurrencyDatabase : IAsyncLifetime
{
    public ConcurrencyDatabase(PostgreSqlFixture server)
    {
        ConnectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = $"Locks0065_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public string ConnectionString { get; }

    public DispatchGuardDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DispatchGuardDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    /// <summary>Sessions of this database waiting for an advisory lock right now.</summary>
    public async Task<int> WaitersAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*)::int FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())";
        return (int)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Returns when at least <paramref name="count"/> sessions wait for an advisory lock.</summary>
    public async Task UntilWaitingAsync(int count, CancellationToken cancellationToken)
    {
        while (await WaitersAsync() < count)
        {
            await Task.Delay(20, cancellationToken);
        }
    }

    /// <summary>Takes advisory locks from outside the API, like another instance would; disposing the result frees them.</summary>
    public async Task<HeldLocks> HoldAsync(params string[] resources)
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        foreach (var resource in resources)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT pg_try_advisory_xact_lock(@kind, @id)";
            var (kind, id) = KeyOf(resource);
            command.Parameters.AddWithValue("kind", kind);
            command.Parameters.AddWithValue("id", id);
            Assert.Equal(true, await command.ExecuteScalarAsync());
        }

        return new HeldLocks(connection, transaction);
    }

    /// <summary>Written apart from the product, so that a change of its keys does not go unnoticed.</summary>
    private static (int Kind, int Id) KeyOf(string resource)
    {
        var parts = resource.Split(':');
        var kind = parts[1] switch
        {
            "trip" => 0x4C4F4601,
            "driver" => 0x4C4F4602,
            "vehicle" => 0x4C4F4603,
            _ => throw new ArgumentException($"Unknown resource {resource}", nameof(resource))
        };
        return (kind, int.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    public sealed class HeldLocks(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

/// <summary>One API instance: its own host, services, contexts and connections. Its saves can be paused or broken.</summary>
public sealed class ApiInstance : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiInstance(string connectionString, int lockWaitMilliseconds = 60_000)
    {
        _factory = new SpanishApiFactory().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:DispatchGuardDb", connectionString);
            b.UseSetting("Allocation:LockWaitMilliseconds", lockWaitMilliseconds.ToString(CultureInfo.InvariantCulture));
            b.ConfigureTestServices(s => s.ConfigureDbContext<DispatchGuardDbContext>(o => o.AddInterceptors(Saves, Commits)));
        });
        Client = _factory.CreateClient();
    }

    public HttpClient Client { get; }

    public SavePoint Saves { get; } = new();

    public CommitPoint Commits { get; } = new();

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }
}

/// <summary>Stops or breaks a chosen save of business rows (the attempt log is not one).</summary>
public sealed class SavePoint : SaveChangesInterceptor
{
    private readonly object _sync = new();
    private Pause? _pause;
    private Func<int, Exception?>? _failure;
    private int _saves;

    public sealed class Pause
    {
        internal TaskCompletionSource ReachedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource ReleasedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Reached => ReachedSource.Task;

        public void Release() => ReleasedSource.TrySetResult();
    }

    /// <summary>The next save of business rows stops right before writing: everything is already read and decided.</summary>
    public Pause PauseNext()
    {
        lock (_sync)
        {
            return _pause = new Pause();
        }
    }

    /// <summary>From now on <paramref name="failure"/> says what the n-th save (1, 2…) throws; null lets it pass.</summary>
    public void Fail(Func<int, Exception?> failure)
    {
        lock (_sync)
        {
            (_failure, _saves) = (failure, 0);
        }
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        var business = eventData.Context!.ChangeTracker.Entries()
            .Any(e => e.Entity is not AllocationAttempt && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        if (!business || eventData.Context.ChangeTracker.Entries<AllocationAttempt>().Any(e => e.State == EntityState.Added))
        {
            return result;
        }

        Pause? pause;
        Exception? failure;
        lock (_sync)
        {
            (pause, _pause) = (_pause, null);
            failure = _failure?.Invoke(++_saves);
        }

        if (failure is not null)
        {
            throw failure;
        }

        if (pause is not null)
        {
            pause.ReachedSource.TrySetResult();
            await pause.ReleasedSource.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }

        return result;
    }
}

/// <summary>Fails right after a commit reached the server: the client cannot know that it succeeded.</summary>
public sealed class CommitPoint : DbTransactionInterceptor
{
    private Exception? _failure;

    public void FailOnceAfterCommit(Exception failure) => _failure = failure;

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        var failure = Interlocked.Exchange(ref _failure, null);
        return failure is null ? Task.CompletedTask : throw failure;
    }
}
