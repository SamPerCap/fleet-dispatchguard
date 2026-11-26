using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Infrastructure.Tests.Integration;

/// <summary>SPEC 0091 R7: the scripts of deploy/sql, and the API running as the user they create.</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LeastPrivilegeTests : IAsyncLifetime
{
    private const string ResetKey = "s3cret-test-key";
    private const string Password = "Appl1cation!Test";

    private readonly ConcurrencyDatabase _database;
    private readonly string _login = $"lof_{Guid.NewGuid():N}";

    public LeastPrivilegeTests(PostgreSqlFixture server) => _database = new ConcurrencyDatabase(server);

    private string AsApplication =>
        new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Username = _login, Password = Password }.ConnectionString;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        // Twice, because both can be repeated.
        for (var i = 0; i < 2; i++)
        {
            await RunAsync(_database.ConnectionString, Script("application-role.sql"));
            await RunAsync(_database.ConnectionString, Script("application-login.sql"));
        }
    }

    public async Task DisposeAsync()
    {
        var server = new NpgsqlConnectionStringBuilder(_database.ConnectionString) { Database = "postgres" }.ConnectionString;
        await _database.DisposeAsync();
        await RunAsync(server, $"DROP ROLE {_login}");
    }

    /// <summary>As psql does: the variables are replaced by a quoted text.</summary>
    private string Script(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sql", name))
            .Replace(":'ApplicationLogin'", $"'{_login}'", StringComparison.Ordinal)
            .Replace(":'ApplicationPassword'", $"'{Password}'", StringComparison.Ordinal);

    private static async Task RunAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData("CREATE TABLE public.intruder (id int)")]
    [InlineData("ALTER TABLE public.\"Trips\" ADD intruder int NULL")]
    [InlineData("DROP TABLE public.\"TripStops\"")]
    [InlineData("TRUNCATE TABLE public.\"AllocationAttempts\"")]
    [InlineData("CREATE ROLE intruder NOLOGIN")]
    [InlineData("GRANT pg_write_all_data TO lof_application")]
    [InlineData("DELETE FROM public.\"__EFMigrationsHistory\"")]
    [InlineData("INSERT INTO public.\"__EFMigrationsHistory\" VALUES ('intruder', '1')")]
    [InlineData("CREATE DATABASE intruder")]
    [InlineData("UPDATE public.\"AuditEvents\" SET \"Changes\" = '{}'")]
    [InlineData("DELETE FROM public.\"AuditEvents\"")]
    [InlineData("TRUNCATE TABLE public.\"AuditEvents\"")]
    [InlineData("UPDATE public.\"Visits\" SET \"Reference\" = NULL")]
    [InlineData("DELETE FROM public.\"Visits\"")]
    [InlineData("UPDATE public.\"VisitEvents\" SET \"Name\" = ''")]
    [InlineData("DELETE FROM public.\"VisitEvents\"")]
    [InlineData("UPDATE public.\"RequestTraces\" SET \"Detail\" = ''")]
    [InlineData("DELETE FROM public.\"RequestTraces\"")]
    [InlineData("CREATE OR REPLACE FUNCTION public.delete_expired_records() RETURNS void LANGUAGE sql AS 'SELECT NULL::void'")]
    public async Task Application_AnythingButRows_IsRefused(string sql)
    {
        var refused = await Assert.ThrowsAsync<PostgresException>(() => RunAsync(AsApplication, sql));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
    }

    [Fact]
    public async Task Application_AllocatesAndResetsTheDemo()
    {
        await using var factory = new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSessionKey();
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", AsApplication);
            builder.UseSetting("AllowedHosts", "localhost");
            builder.UseSetting("Demo:ResetKey", ResetKey);
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-DispatchGuard-Request", "1");
        client.DefaultRequestHeaders.Add("X-Demo-Reset-Key", ResetKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        var origin = await CreateAsync(client, "/api/locations", Site("Origen"));
        var destination = await CreateAsync(client, "/api/locations", Site("Destino"));
        var driver = await CreateAsync(client, "/api/drivers", new { IdentificationNumber = "11111111H", FirstName = "Ana", LastName = "Prueba" });
        var vehicle = await CreateAsync(client, "/api/vehicles", new { LicensePlate = "0000BBB", Brand = "Marca", Model = "Modelo", SeatingCapacity = 50 });
        var start = DateTime.UtcNow.Date.AddDays(40).AddHours(9);
        var trip = await CreateAsync(client, "/api/trips", new
        {
            OriginLocationId = origin, DestinationLocationId = destination, Start = start, End = start.AddHours(1), RequiredSeats = 10, ServiceType = 1
        });

        using var allocated = await client.AllocateAsync(trip, driver, vehicle, "Ana");
        using var reset = await client.PostAsync("/api/admin/demo/reset", null);

        Assert.True(allocated.IsSuccessStatusCode, await allocated.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        await using var context = _database.CreateContext();
        Assert.Empty(context.Trips);
    }

    /// <summary>SPEC 0085 R8: a function the application can only ask for.</summary>
    [Fact]
    public async Task Application_DeletesWhatExpired_AndNothingElse()
    {
        await RunAsync(AsApplication, """
            INSERT INTO "AuditEvents" ("At", "Action", "Entity", "Changes") VALUES
                (now() - interval '31 days', 'Changed', 'Old', '{}'), (now() - interval '29 days', 'Changed', 'Recent', '{}');
            INSERT INTO "Visits" ("SessionId", "StartedAt") VALUES ('old', now() - interval '31 days'), ('recent', now() - interval '29 days');
            INSERT INTO "VisitEvents" ("SessionId", "At", "Kind", "Name") VALUES
                ('old', now() - interval '31 days', 'Screen', 'x'), ('recent', now() - interval '29 days', 'Screen', 'x');
            INSERT INTO "RequestTraces" ("SessionId", "At", "TraceId", "Method", "Path", "Status", "DurationMs", "Queries", "QueriesMs", "Detail")
            VALUES ('old', now() - interval '31 days', 't', 'GET', '/', 200, 1, 1, 1, '{}'),
                ('recent', now() - interval '29 days', 't', 'GET', '/', 200, 1, 1, 1, '{}');
            """);

        await RunAsync(AsApplication, "SELECT public.delete_expired_records()");

        await using var context = _database.CreateContext();
        Assert.Equal("Recent", context.AuditEvents.Single().Entity);
        Assert.Equal("recent", context.Visits.Single().SessionId);
        Assert.Equal("recent", context.VisitEvents.Single().SessionId);
        Assert.Equal("recent", context.RequestTraces.Single().SessionId);
    }

    /// <summary>As in the published database: the role, with all the permissions by default, is there before the migration.</summary>
    [Fact]
    public async Task Migration_OverTheRoleThatAlreadyExists_OnlyLetsAdd()
    {
        await using (var context = _database.CreateContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("DropSessionKeys");
            await context.Database.MigrateAsync();
        }

        await RunAsync(AsApplication, """INSERT INTO "Visits" ("SessionId", "StartedAt") VALUES ('a', now())""");
        await RunAsync(AsApplication, "SELECT public.delete_expired_records()");
        var refused = await Assert.ThrowsAsync<PostgresException>(() => RunAsync(AsApplication, """DELETE FROM "AuditEvents" """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, refused.SqlState);
    }

    private static object Site(string name) =>
        new { Name = name, Address = "Calle", Latitude = 40.4m, Longitude = -3.7m, City = "Madrid", Province = "Madrid" };

    private static async Task<int> CreateAsync(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("Id").GetInt32();
    }
}
