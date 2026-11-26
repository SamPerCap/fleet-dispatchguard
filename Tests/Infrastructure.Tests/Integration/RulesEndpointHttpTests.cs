using System.Net;
using System.Text.Json;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Application.Services.Rules;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RulesEndpointHttpTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public RulesEndpointHttpTests(PostgreSqlFixture db)
    {
        _factory = new SpanishApiFactory().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task GetRules_ReturnsGroupsWithKindsAsStringsAndRealFigures()
    {
        var response = await _client.GetAsync("/api/rules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var groups = json.RootElement.EnumerateArray().ToList();
        Assert.Equal(7, groups.Count);
        var rules = groups.SelectMany(g => g.GetProperty("Rules").EnumerateArray()).ToList();
        var daily = rules.Single(r => r.GetProperty("Id").GetString() == "daily");
        Assert.Equal(RulesCatalog.Hours(DrivingLimits.MaxDailyDriving).Es(), daily.GetProperty("Figure").GetString());
        Assert.Equal("Blocking", daily.GetProperty("Kind").GetString());
        Assert.Contains(rules, r => r.GetProperty("Kind").GetString() == "Ranking");
    }
}
