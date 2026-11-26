using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ClientsEndpointsHttpTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ClientsEndpointsHttpTests(PostgreSqlFixture db)
    {
        _factory = new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Theory]
    [InlineData(200, HttpStatusCode.Created)]
    [InlineData(201, HttpStatusCode.BadRequest)]
    public async Task Create_NameLengthLimit_IsEnforced(int length, HttpStatusCode expected)
    {
        var response = await _client.PostAsJsonAsync("/api/clients", new { Name = NameOfLength(length) });

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(200, HttpStatusCode.OK)]
    [InlineData(201, HttpStatusCode.BadRequest)]
    public async Task Update_NameLengthLimit_IsEnforced(int length, HttpStatusCode expected)
    {
        var id = await CreateClientAsync();

        var response = await _client.PutAsJsonAsync($"/api/clients/{id}", new { Name = NameOfLength(length) });

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Create_SeveralInvalidFields_ReturnsOneSpanishProblemListingAllOfThem()
    {
        var response = await _client.PostAsJsonAsync("/api/clients", new { Name = "", ContactEmail = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await ReadJsonAsync(response);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("Petición no válida", problem.GetProperty("title").GetString());
        var detail = problem.GetProperty("detail").GetString()!;
        Assert.Contains("El nombre es obligatorio", detail);
        Assert.Contains("El correo electrónico no es válido", detail);
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Name", out _));
        Assert.True(errors.TryGetProperty("ContactEmail", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_BlankOptionalFields_AreStoredAsAbsent(string blank)
    {
        // Twice, because a blank tax ID stored as text would collide in its unique index.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var response = await _client.PostAsJsonAsync("/api/clients",
                new { Name = NameOfLength(40), TaxId = blank, ContactEmail = blank, ContactPhone = blank });

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await ReadJsonAsync(response);
            Assert.Equal(JsonValueKind.Null, created.GetProperty("TaxId").ValueKind);
            Assert.Equal(JsonValueKind.Null, created.GetProperty("ContactEmail").ValueKind);
            Assert.Equal(JsonValueKind.Null, created.GetProperty("ContactPhone").ValueKind);
        }
    }

    [Fact]
    public async Task Update_BlankOptionalFields_AreAccepted()
    {
        var id = await CreateClientAsync();

        var response = await _client.PutAsJsonAsync($"/api/clients/{id}",
            new { Name = NameOfLength(40), TaxId = "", ContactEmail = "", ContactPhone = "" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesTheName()
    {
        var id = await CreateClientAsync();
        var newName = NameOfLength(40);

        var response = await _client.PutAsJsonAsync($"/api/clients/{id}", new { Name = newName });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = await ReadJsonAsync(await _client.GetAsync($"/api/clients/{id}"));
        Assert.Equal(newName, stored.GetProperty("Name").GetString());
    }

    private async Task<int> CreateClientAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/clients", new { Name = NameOfLength(40) });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJsonAsync(response)).GetProperty("Id").GetInt32();
    }

    private static string NameOfLength(int length) => Guid.NewGuid().ToString("N").PadRight(length, 'x');

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
