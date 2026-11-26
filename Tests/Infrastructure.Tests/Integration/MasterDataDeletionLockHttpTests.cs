using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MasterDataDeletionLockHttpTests(PostgreSqlFixture db)
{
    private WebApplicationFactory<Program> Factory(string? locked) =>
        new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
            if (locked is not null) builder.UseSetting("Demo:LockMasterDataDeletion", locked);
        });

    [Theory]
    [InlineData("drivers")]
    [InlineData("vehicles")]
    [InlineData("locations")]
    public async Task Delete_Locked_IsForbiddenAndKeepsTheResource(string resource)
    {
        await using var factory = Factory("true");
        using var client = factory.CreateClient();
        var url = $"/api/{resource}/{await CreateAsync(client, resource)}";

        using var response = await client.DeleteAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Acción desactivada en la demo", problem.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        // A vehicle or a driver that was deleted can still be read by id, so its state matters and not only that it answers.
        var listed = await client.GetFromJsonAsync<JsonElement>($"/api/{resource}");
        Assert.Contains(listed.EnumerateArray(), item => url.EndsWith($"/{item.GetProperty("Id").GetInt32()}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("drivers")]
    [InlineData("vehicles")]
    [InlineData("locations")]
    public async Task Delete_LockedUnknownId_IsForbiddenToo(string resource)
    {
        await using var factory = Factory("true");
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync($"/api/{resource}/999999");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("drivers", null)]
    [InlineData("vehicles", null)]
    [InlineData("locations", null)]
    [InlineData("drivers", "false")]
    public async Task Delete_NotLocked_DeletesAsAlways(string resource, string? locked)
    {
        await using var factory = Factory(locked);
        using var client = factory.CreateClient();
        var id = await CreateAsync(client, resource);

        using var response = await client.DeleteAsync($"/api/{resource}/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("drivers", "Vacation")]
    [InlineData("vehicles", "Maintenance")]
    public async Task DeleteAgendaEntry_Locked_StillWorks(string resource, string type)
    {
        await using var factory = Factory("true");
        using var client = factory.CreateClient();
        var url = $"/api/{resource}/{await CreateAsync(client, resource)}/unavailabilities";
        var start = DateTime.UtcNow.AddDays(10);
        using var added = await client.PostAsJsonAsync(url, new { Start = start, End = start.AddDays(1), Type = type });
        var owner = await added.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(added.IsSuccessStatusCode, owner.ToString());
        var entryId = owner.GetProperty("Unavailabilities")[0].GetProperty("Id").GetInt32();

        using var response = await client.DeleteAsync($"{url}/{entryId}");

        Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
    }

    [Fact]
    public async Task Start_LockIsNotABoolean_Fails()
    {
        await using var factory = Factory("yes please");

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    private static async Task<int> CreateAsync(HttpClient client, string resource)
    {
        var unique = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        object body = resource switch
        {
            "drivers" => new { IdentificationNumber = unique, FirstName = "Ana", LastName = "Prueba" },
            "vehicles" => new { LicensePlate = unique, Brand = "Marca", Model = "Modelo", SeatingCapacity = 50 },
            _ => new { Name = unique, Address = "Calle", Latitude = 40.4m, Longitude = -3.7m, City = "Madrid", Province = "Madrid" }
        };
        using var response = await client.PostAsJsonAsync($"/api/{resource}", body);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.StatusCode == HttpStatusCode.Created, created.ToString());
        return created.GetProperty("Id").GetInt32();
    }
}
