using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Entities.Base;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Tests.Integration;

/// <summary>The tax ID of a customer and the name of a base answer as a repeated DNI or plate does (SPEC 0094 R4).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RepeatedValuesHttpTests : IAsyncLifetime
{
    private readonly ConcurrencyDatabase _db;
    private readonly ApiInstance _api;

    public RepeatedValuesHttpTests(PostgreSqlFixture server)
    {
        _db = new ConcurrencyDatabase(server);
        _api = new ApiInstance(_db.ConnectionString);
    }

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync()
    {
        _api.Dispose();
        return _db.DisposeAsync();
    }

    public static TheoryData<string, string> Resources => new() { { "clients", "TaxId" }, { "bases", "Name" } };

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Created_WithTheOneOfAnother_Is409WithTheField(string kind, string field)
    {
        await CreateAsync(kind, "REPETIDO");

        await ExpectTakenAsync(field, "REPETIDO", await PostAsync(_api, kind, "REPETIDO"));
        await ExpectTakenAsync(field, "repetido", await PostAsync(_api, kind, " repetido "));
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task Changed_ToTheOneOfAnother_Is409AndChangesNothing(string kind, string field)
    {
        await CreateAsync(kind, "REPETIDO");
        var id = await CreateAsync(kind, "PROPIO");

        await ExpectTakenAsync(field, "REPETIDO", await PutAsync(_api, kind, id, "REPETIDO"));

        Assert.Equal("PROPIO", await StoredAsync(kind, id));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync(_api, kind, id, "propio")).StatusCode); // its own one is not taken
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task TakenByAnInactiveOne_Is409(string kind, string field)
    {
        await DeactivateAsync(kind, await CreateAsync(kind, "REPETIDO"));
        var id = await CreateAsync(kind, "PROPIO");

        await ExpectTakenAsync(field, "REPETIDO", await PostAsync(_api, kind, "REPETIDO"));
        await ExpectTakenAsync(field, "REPETIDO", await PutAsync(_api, kind, id, "REPETIDO"));
    }

    /// <summary>Both pass the check before any of them is saved: the index stops the second one.</summary>
    [Theory]
    [MemberData(nameof(Resources))]
    public async Task CreatedByTwoAtOnce_TheSecondIs409(string kind, string field)
    {
        using var other = new ApiInstance(_db.ConnectionString);
        var pause = _api.Saves.PauseNext();

        var first = PostAsync(_api, kind, "REPETIDO");
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var second = await PostAsync(other, kind, "REPETIDO");
        pause.Release();

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        await ExpectTakenAsync(field, "REPETIDO", await first);
    }

    [Theory]
    [MemberData(nameof(Resources))]
    public async Task ChangedByTwoAtOnce_TheSecondIs409(string kind, string field)
    {
        var (oneId, otherId) = (await CreateAsync(kind, "PRIMERO"), await CreateAsync(kind, "SEGUNDO"));
        using var other = new ApiInstance(_db.ConnectionString);
        var pause = _api.Saves.PauseNext();

        var first = PutAsync(_api, kind, oneId, "REPETIDO");
        await pause.Reached.WaitAsync(TimeSpan.FromSeconds(30));
        var second = await PutAsync(other, kind, otherId, "REPETIDO");
        pause.Release();

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        await ExpectTakenAsync(field, "REPETIDO", await first);
        Assert.Equal("PRIMERO", await StoredAsync(kind, oneId));
    }

    private static object Body(string kind, string value) => kind == "clients" ? new { Name = "Cliente", TaxId = value } : new { Name = value };

    private static Task<HttpResponseMessage> PostAsync(ApiInstance api, string kind, string value) =>
        api.Client.PostAsJsonAsync($"/api/{kind}", Body(kind, value));

    private static Task<HttpResponseMessage> PutAsync(ApiInstance api, string kind, int id, string value) =>
        api.Client.PutAsJsonAsync($"/api/{kind}/{id}", Body(kind, value));

    private async Task<int> CreateAsync(string kind, string value)
    {
        using var response = await PostAsync(_api, kind, value);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Id").GetInt32();
    }

    private async Task<string?> StoredAsync(string kind, int id)
    {
        await using var read = _db.CreateContext();
        return kind == "clients"
            ? (await read.Set<Client>().SingleAsync(client => client.Id == id)).TaxId
            : (await read.Set<OperatingBase>().SingleAsync(operatingBase => operatingBase.Id == id)).Name;
    }

    /// <summary>In the database: the API has no way to deactivate a base.</summary>
    private async Task DeactivateAsync(string kind, int id)
    {
        await using var write = _db.CreateContext();
        Entity entity = kind == "clients"
            ? await write.Set<Client>().SingleAsync(client => client.Id == id)
            : await write.Set<OperatingBase>().SingleAsync(operatingBase => operatingBase.Id == id);
        entity.Deactivate();
        await write.SaveChangesAsync();
    }

    private static async Task ExpectTakenAsync(string field, string value, HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"{(int)response.StatusCode}: {text}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = JsonDocument.Parse(text).RootElement;
        Assert.Equal("Dato repetido", problem.GetProperty("title").GetString());
        Assert.Equal(field, problem.GetProperty("field").GetString());
        Assert.Contains($"{value} ya es de otr", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }
}
