using DispatchGuard.Core.Entities;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>Behaviour shared by every repository through <see cref="EfRepository{T}"/> (SPEC 0034).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public class GenericRepositoryTests
{
    private readonly PostgreSqlFixture _db;

    public GenericRepositoryTests(PostgreSqlFixture db) => _db = db;

    [Fact]
    public async Task AddAsync_WithoutSaveChanges_DoesNotPersist()
    {
        var driver = TripRepositoryTests.NewDriver();
        using (var uow = new UnitOfWork(_db.CreateContext()))
        {
            await uow.Drivers.AddAsync(driver);
        }

        await using var read = _db.CreateContext();
        Assert.False(await read.Drivers.AnyAsync(d => d.IdentificationNumber == driver.IdentificationNumber));
    }

    [Fact]
    public async Task AddAsync_ThenSaveChanges_IsReadableFromNewContext()
    {
        var driver = TripRepositoryTests.NewDriver();
        using (var uow = new UnitOfWork(_db.CreateContext()))
        {
            var returned = await uow.Drivers.AddAsync(driver);
            Assert.Same(driver, returned);
            await uow.SaveChangesAsync();
        }

        Assert.True(driver.Id > 0);
        using var readUow = new UnitOfWork(_db.CreateContext());
        var loaded = await readUow.Drivers.GetByIdAsync(driver.Id);

        Assert.NotNull(loaded);
        Assert.Equal(driver.IdentificationNumber, loaded.IdentificationNumber);
        Assert.True(loaded.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_IsSoftDelete_RowKeptInactiveAndStillLoadableById()
    {
        var client = new Client($"Cliente {Guid.NewGuid():N}");
        using (var uow = new UnitOfWork(_db.CreateContext()))
        {
            await uow.Clients.AddAsync(client);
            await uow.SaveChangesAsync();
        }

        // Delete with an entity coming from another (already disposed) context: detached path.
        using (var uow = new UnitOfWork(_db.CreateContext()))
        {
            await uow.Clients.DeleteAsync(client);
            await uow.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        var row = await read.Clients.SingleAsync(c => c.Id == client.Id);
        Assert.False(row.IsActive);

        using var readUow = new UnitOfWork(_db.CreateContext());
        var byId = await readUow.Clients.GetByIdAsync(client.Id);
        Assert.NotNull(byId);
        Assert.False(byId.IsActive);
    }

    [Fact]
    public async Task GetAllAsync_DoesNotReturnInactiveEntities()
    {
        var active = TripRepositoryTests.NewLocation("Activa");
        var inactive = TripRepositoryTests.NewLocation("Inactiva");
        using (var uow = new UnitOfWork(_db.CreateContext()))
        {
            await uow.Locations.AddAsync(active);
            await uow.Locations.AddAsync(inactive);
            await uow.SaveChangesAsync();

            // Tracked path: delete an entity the same context already tracks.
            await uow.Locations.DeleteAsync(inactive);
            await uow.SaveChangesAsync();
        }

        using var readUow = new UnitOfWork(_db.CreateContext());
        var all = await readUow.Locations.GetAllAsync();

        Assert.Contains(all, l => l.Id == active.Id);
        Assert.DoesNotContain(all, l => l.Id == inactive.Id);
        Assert.All(all, l => Assert.True(l.IsActive));
    }

    [Fact]
    public async Task UpdateAsync_DetachedEntity_PersistsChanges()
    {
        var client = new Client($"Cliente {Guid.NewGuid():N}");
        using (var uow = new UnitOfWork(_db.CreateContext()))
        {
            await uow.Clients.AddAsync(client);
            await uow.SaveChangesAsync();
        }

        client.Deactivate(); // any state change on a detached instance
        using (var uow = new UnitOfWork(_db.CreateContext()))
        {
            await uow.Clients.UpdateAsync(client);
            await uow.SaveChangesAsync();
        }

        await using var read = _db.CreateContext();
        Assert.False((await read.Clients.SingleAsync(c => c.Id == client.Id)).IsActive);
    }

    [Fact]
    public async Task ClientQueries_FilterByActiveAndExternal()
    {
        var internalActive = new Client($"Interno {Guid.NewGuid():N}");
        var externalActive = new Client($"Externo {Guid.NewGuid():N}", isExternal: true);
        var internalInactive = new Client($"Baja {Guid.NewGuid():N}");
        internalInactive.Deactivate();
        using (var uow = new UnitOfWork(_db.CreateContext()))
        {
            await uow.Clients.AddAsync(internalActive);
            await uow.Clients.AddAsync(externalActive);
            await uow.Clients.AddAsync(internalInactive);
            await uow.SaveChangesAsync();
        }

        using var readUow = new UnitOfWork(_db.CreateContext());
        var activeClients = (await readUow.Clients.GetActiveClientsAsync(CancellationToken.None)).ToList();
        var externalClients = (await readUow.Clients.GetExternalClientsAsync(CancellationToken.None)).ToList();

        Assert.Contains(activeClients, c => c.Id == internalActive.Id);
        Assert.Contains(activeClients, c => c.Id == externalActive.Id);
        Assert.DoesNotContain(activeClients, c => c.Id == internalInactive.Id);

        Assert.Contains(externalClients, c => c.Id == externalActive.Id);
        Assert.DoesNotContain(externalClients, c => c.Id == internalActive.Id);
        Assert.All(externalClients, c => Assert.True(c.IsExternal));
    }
}
