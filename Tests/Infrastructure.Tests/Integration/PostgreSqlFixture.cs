using DispatchGuard.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// Real PostgreSQL 17 in a throwaway container (Testcontainers), with all the migrations applied. Shared by every test in the
/// <see cref="PostgreSqlCollection"/> so that the container starts only once.
///
/// Requires Docker. Tests that use it carry <c>[Trait("Category", "Integration")]</c>
/// so the fast loop can exclude them: <c>dotnet test --filter "Category!=Integration"</c>.
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private const string Image = "postgres:17@sha256:87c8b42f423b45d5247e6aa8fdb2e7762fec164c68a50fe3eb07e7bfcaf4dbcf";

    /// <summary>Each database and each API instance of the tests has a pool of its own.</summary>
    private const string MaxConnections = "max_connections=500";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).WithCommand("-c", MaxConnections).Build();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "No se pudo arrancar PostgreSQL en Docker para los tests de integración. " +
                "¿Está Docker en marcha? Para ejecutar solo los tests rápidos: " +
                "dotnet test --filter \"Category!=Integration\"", ex);
        }

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>New context per call: tests must not share change trackers.</summary>
    public DispatchGuardDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DispatchGuardDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options);

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSql";
}
