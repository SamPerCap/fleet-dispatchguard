using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// SPEC 0089 R7. The text fields are read from the endpoints themselves: a new one fails here until it has a valid body to start from.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LongTextHttpTests(PostgreSqlFixture db)
{
    /// <summary>One more than each limit in use, where a check and its column could disagree.</summary>
    private static readonly int[] JustOver = [3, 21, 51, 101, 201, 256, 501, 1001];

    private const int LongerThanAnyLimit = 5000;

    private sealed record World(int Location, int Driver, int DriverEntry, int Vehicle, int VehicleEntry, int Trip, int Booking, int Client, int Base);

    private sealed record Sample(string Url, object Body);

    /// <summary>A body that the endpoint accepts, so that the only thing wrong in each call is its long text.</summary>
    private static readonly Dictionary<string, Func<World, Sample>> Samples = new()
    {
        ["POST /api/bases"] = _ => new("/api/bases", new { Name = Unique() }),
        ["PUT /api/bases/{id:int}"] = w => new($"/api/bases/{w.Base}", new { Name = Unique() }),
        ["POST /api/bookings"] = w => new("/api/bookings", new
        {
            CustomerName = "Cliente", Description = "Excursión", Notes = "Nota", Trips = new[] { TripBody(w.Location) }
        }),
        ["PUT /api/bookings/{id:int}"] = w => new($"/api/bookings/{w.Booking}", new { CustomerName = "Cliente", Description = "Excursión", Notes = "Nota" }),
        ["POST /api/clients"] = _ => new("/api/clients", ClientBody()),
        ["PUT /api/clients/{id:int}"] = w => new($"/api/clients/{w.Client}", ClientBody()),
        ["POST /api/drivers"] = _ => new("/api/drivers", DriverBody()),
        ["PUT /api/drivers/{id:int}"] = w => new($"/api/drivers/{w.Driver}", WithVersion(DriverBody())),
        ["POST /api/drivers/{id:int}/unavailabilities"] = w => new($"/api/drivers/{w.Driver}/unavailabilities", EntryBody("SickLeave")),
        ["PUT /api/drivers/{id:int}/unavailabilities/{unavailabilityId:int}"] = w =>
            new($"/api/drivers/{w.Driver}/unavailabilities/{w.DriverEntry}", EntryBody("SickLeave")),
        ["POST /api/locations"] = _ => new("/api/locations", LocationBody()),
        ["POST /api/trips"] = w => new("/api/trips", TripBody(w.Location)),
        ["POST /api/trips/{tripId:int}/allocate"] = w => new($"/api/trips/{w.Trip}/allocate", new
        {
            DriverId = w.Driver, VehicleId = w.Vehicle, AssignedBy = "Ana", Reason = "Motivo de prueba"
        }),
        ["POST /api/vehicles"] = _ => new("/api/vehicles", VehicleBody()),
        ["PUT /api/vehicles/{id:int}"] = w => new($"/api/vehicles/{w.Vehicle}", WithVersion(VehicleBody())),
        ["POST /api/vehicles/{id:int}/unavailabilities"] = w => new($"/api/vehicles/{w.Vehicle}/unavailabilities", EntryBody("Repair")),
        ["PUT /api/vehicles/{id:int}/unavailabilities/{unavailabilityId:int}"] = w =>
            new($"/api/vehicles/{w.Vehicle}/unavailabilities/{w.VehicleEntry}", EntryBody("Repair")),
        ["POST /api/visits"] = _ => new("/api/visits", new
        {
            Reference = "acme", Events = new[] { new { AgoMs = 0, Kind = "Screen", Name = "Reservas", TraceId = new string('a', 32) } }
        }),
    };

    [Fact]
    public async Task EveryTextField_TooLong_IsABadRequestAndNeverAServerError()
    {
        await using var factory = new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
        });
        using var client = factory.CreateClient();
        var world = await BuildWorldAsync(client);
        var failures = new List<string>();
        var checkedFields = 0;

        foreach (var (operation, fields) in OperationsWithText(factory))
        {
            if (!Samples.TryGetValue(operation, out var sampleOf))
            {
                failures.Add($"{operation}: has text ({string.Join(", ", fields)}) but no valid body in this test");
                continue;
            }

            var method = new HttpMethod(operation.Split(' ')[0]);
            var valid = await SendAsync(client, method, sampleOf(world), field: null, length: 0);
            if ((int)valid is >= 400 and not 409 and not 422) failures.Add($"{operation}: its valid body is answered with {(int)valid}");

            foreach (var field in fields)
            {
                checkedFields++;
                foreach (var length in JustOver)
                {
                    var status = await SendAsync(client, method, sampleOf(world), field, length);
                    if ((int)status >= 500) failures.Add($"{operation} {field} of {length}: {(int)status}");
                }

                var tooLong = await SendAsync(client, method, sampleOf(world), field, LongerThanAnyLimit);
                if (tooLong != HttpStatusCode.BadRequest) failures.Add($"{operation} {field} of {LongerThanAnyLimit}: {(int)tooLong}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.True(checkedFields >= 40, $"Only {checkedFields} text fields were found");
    }

    [Theory]
    [InlineData("/api/engine/attempts?rule={0}")]
    [InlineData("/api/engine/attempts?by={0}")]
    [InlineData("/api/engine/hourly?by={0}")]
    [InlineData("/api/trips?from={0}&to={0}")]
    public async Task TextOfAQuery_TooLong_IsABadRequest(string url)
    {
        await using var factory = new SpanishApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DispatchGuardDb", db.ConnectionString);
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(string.Format(null, url, new string('x', LongerThanAnyLimit)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>"POST /api/bookings" → CustomerName, Description, Notes, Trips[].Description.</summary>
    private static IEnumerable<(string Operation, List<string> Fields)> OperationsWithText(WebApplicationFactory<Program> factory) =>
        from endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
        let body = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.RequestType
        where body is not null
        let fields = TextFields(body).ToList()
        where fields.Count > 0
        from method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
        select ($"{method} /{endpoint.RoutePattern.RawText!.Trim('/')}", fields);

    private static IEnumerable<string> TextFields(Type type)
    {
        foreach (var property in type.GetProperties())
        {
            if (property.PropertyType == typeof(string))
            {
                yield return property.Name;
            }
            else if (ElementTypeOf(property.PropertyType) is { } element)
            {
                foreach (var nested in TextFields(element)) yield return $"{property.Name}[].{nested}";
            }
        }
    }

    private static Type? ElementTypeOf(Type type) =>
        type.IsGenericType && type.IsAssignableTo(typeof(System.Collections.IEnumerable)) ? type.GetGenericArguments()[0] : null;

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, HttpMethod method, Sample sample, string? field, int length)
    {
        var body = JsonSerializer.SerializeToNode(sample.Body)!;
        // It changes every time that one of them is accepted (SPEC 0090).
        if (body["DetailsVersion"] is not null) body["DetailsVersion"] = await client.DetailsVersionAsync(sample.Url);
        if (field is not null) Replace(body, field, length);
        using var request = new HttpRequestMessage(method, sample.Url) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    private static void Replace(JsonNode body, string field, int length)
    {
        var steps = field.Split("[].");
        var holder = steps.SkipLast(1).Aggregate(body, (node, step) => node[step]!.AsArray()[0]!);
        holder[steps[^1]] = UniqueText(length);
    }

    /// <summary>Repeating a text would be refused for being repeated, not for its length.</summary>
    private static string UniqueText(int length) => Guid.NewGuid().ToString("N").PadRight(length, 'x')[..length];

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    private static object DriverBody() => new
    {
        IdentificationNumber = Unique(), FirstName = "Ana", LastName = "Prueba", IsExternal = false, AllowsAutoAssignment = true,
        HomeLocationId = (int?)null, BaseId = 1
    };

    private static object VehicleBody() => new
    {
        LicensePlate = Unique(), Brand = "Marca", Model = "Modelo", SeatingCapacity = 50, IsExternal = false, AllowsAutoAssignment = true,
        HomeLocationId = (int?)null, BaseId = 1
    };

    private static JsonObject WithVersion(object details)
    {
        var body = JsonSerializer.SerializeToNode(details)!.AsObject();
        body["DetailsVersion"] = "the stored one is read when it is sent";
        return body;
    }

    private static object ClientBody() => new { Name = Unique(), TaxId = Unique(), ContactEmail = "ana@example.com", ContactPhone = "600000000" };

    private static object LocationBody() => new
    {
        Name = Unique(), Address = "Calle", Latitude = 40.4m, Longitude = -3.7m, City = "Madrid", Province = "Madrid", CountryCode = "ES"
    };

    private static object TripBody(int location)
    {
        var start = DateTime.UtcNow.Date.AddDays(60).AddHours(9);
        return new
        {
            OriginLocationId = location, DestinationLocationId = location, Start = start, End = start.AddHours(1), RequiredSeats = 10,
            ServiceType = "Regular", Description = "Servicio"
        };
    }

    private static object EntryBody(string type)
    {
        var start = DateTime.UtcNow.Date.AddDays(400);
        return new { Start = start, End = start.AddDays(1), Type = type, Notes = "Nota", ServiceProvider = "Taller" };
    }

    private static async Task<World> BuildWorldAsync(HttpClient client)
    {
        var location = await CreateAsync(client, "/api/locations", LocationBody());
        var driver = await CreateAsync(client, "/api/drivers", DriverBody());
        var vehicle = await CreateAsync(client, "/api/vehicles", VehicleBody());
        var booking = await CreateAsync(client, "/api/bookings", new
        {
            CustomerName = "Cliente", Description = "Excursión", Trips = new[] { TripBody(location) }
        });
        return new World(
            location, driver, await EntryAsync(client, $"/api/drivers/{driver}", "SickLeave"),
            vehicle, await EntryAsync(client, $"/api/vehicles/{vehicle}", "Repair"),
            await CreateAsync(client, "/api/trips", TripBody(location)), booking,
            await CreateAsync(client, "/api/clients", ClientBody()),
            await CreateAsync(client, "/api/bases", new { Name = Unique() }));
    }

    private static async Task<int> EntryAsync(HttpClient client, string resource, string type)
    {
        var created = await PostAsync(client, $"{resource}/unavailabilities", EntryBody(type));
        return created.GetProperty("Unavailabilities")[0].GetProperty("Id").GetInt32();
    }

    private static async Task<int> CreateAsync(HttpClient client, string url, object body) =>
        (await PostAsync(client, url, body)).GetProperty("Id").GetInt32();

    private static async Task<JsonElement> PostAsync(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body);
        Assert.True(response.IsSuccessStatusCode, $"{url}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
