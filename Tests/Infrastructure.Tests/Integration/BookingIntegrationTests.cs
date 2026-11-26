using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using DispatchGuard.Infrastructure.Persistence;
using DispatchGuard.Infrastructure.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Integration;

[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BookingIntegrationTests : IDisposable
{
    private readonly PostgreSqlFixture _db;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public BookingIntegrationTests(PostgreSqlFixture db)
    {
        _db = db;
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
    public async Task Booking_WithTrips_RoundTrips_AndRangeQueryLoadsTripsDriverAndVehicle()
    {
        var day = new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        int bookingId, soloTripId;
        await using (var write = _db.CreateContext())
        {
            var origin = NewLocation("Colegio");
            var destination = NewLocation("Segovia");
            var driver = new Driver(Unique("ID"), "Ana", "Pérez");
            var vehicle = new Vehicle(Unique("PL")[..12], "Irizar", "i6", 55);
            write.AddRange(origin, destination, driver, vehicle);
            await write.SaveChangesAsync();

            var booking = new Booking("Colegio San José", "Excursión a Segovia", "Merienda incluida");
            var outbound = new Trip(origin, destination, new TimeWindow(day.AddHours(8), day.AddHours(10)), 50, ServiceType.Discretionary, "Ida", booking);
            _ = new Trip(destination, origin, new TimeWindow(day.AddHours(17), day.AddHours(19)), 50, ServiceType.Discretionary, "Vuelta", booking);
            outbound.AssignResources(driver, vehicle);
            var solo = new Trip(origin, destination, new TimeWindow(day.AddDays(40), day.AddDays(40).AddHours(1)), 10, ServiceType.Regular);
            write.Add(booking);
            write.Trips.Add(solo);
            await write.SaveChangesAsync();
            bookingId = booking.Id;
            soloTripId = solo.Id;
        }

        await using var read = _db.CreateContext();
        var repo = new BookingRepository(read);
        var loaded = await repo.GetByIdAsync(bookingId);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Trips.Count);
        Assert.Equal(BookingAssignmentStatus.PartiallyAssigned, loaded.AssignmentStatus);
        Assert.Equal("Ana Pérez", loaded.Trips.Single(t => t.Description == "Ida").AssignedDriver!.FullName);

        var inRange = await repo.GetByRangeAsync(day, day.AddDays(1));
        Assert.Contains(inRange, b => b.Id == bookingId);
        Assert.DoesNotContain(inRange, b => b.Trips.Any(t => t.Id == soloTripId));

        var soloTrip = await read.Trips.AsNoTracking().Include(t => t.Booking).SingleAsync(t => t.Id == soloTripId);
        Assert.Equal(Booking.UnnamedCustomer, soloTrip.Booking.CustomerName);
    }

    [Fact]
    public async Task Api_CreateGetListUpdateDelete_RoundTripsThroughDatabase()
    {
        var (origin, destination) = (await CreateLocationAsync(), await CreateLocationAsync());
        var day = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var created = await _client.PostAsJsonAsync("/api/bookings", new
        {
            CustomerName = "Colegio San José",
            Description = "Excursión a Segovia",
            Notes = "Recoger en la puerta principal",
            Trips = new[]
            {
                TripBody(origin, destination, day.AddHours(8), day.AddHours(10), "Ida"),
                TripBody(destination, origin, day.AddHours(17), day.AddHours(19), "Vuelta")
            }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var booking = await JsonAsync(created);
        var id = booking.GetProperty("Id").GetInt32();
        Assert.Equal("Unassigned", booking.GetProperty("AssignmentStatus").GetString());
        Assert.Equal(["Ida", "Vuelta"], booking.GetProperty("Trips").EnumerateArray().Select(t => t.GetProperty("Description").GetString()));
        var tripId = booking.GetProperty("Trips")[0].GetProperty("Id").GetInt32();
        Assert.Equal(id, (await JsonAsync(await _client.GetAsync($"/api/trips/{tripId}"))).GetProperty("BookingId").GetInt32());

        var list = await JsonAsync(await _client.GetAsync($"/api/bookings?from={day:yyyy-MM-dd}&to={day.AddDays(1):yyyy-MM-dd}"));
        Assert.Contains(list.EnumerateArray(), b => b.GetProperty("Id").GetInt32() == id);

        var updated = await _client.PutAsJsonAsync($"/api/bookings/{id}", new { CustomerName = "Colegio Nuevo", Description = "Excursión a Toledo" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var detail = await JsonAsync(await _client.GetAsync($"/api/bookings/{id}"));
        Assert.Equal("Colegio Nuevo", detail.GetProperty("CustomerName").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("Notes").ValueKind);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/bookings/{id}")).StatusCode);
        var deleted = await JsonAsync(await _client.GetAsync($"/api/bookings/{id}"));
        Assert.False(deleted.GetProperty("IsActive").GetBoolean());
        Assert.All(deleted.GetProperty("Trips").EnumerateArray(), t => Assert.Equal("Cancelled", t.GetProperty("Status").GetString()));
    }

    [Fact]
    public async Task Api_InvalidInput_Returns400Or404AsProblemDetails()
    {
        var location = await CreateLocationAsync();
        var day = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var noTrips = await _client.PostAsJsonAsync("/api/bookings", new { CustomerName = "C", Description = "D", Trips = Array.Empty<object>() });
        var noCustomer = await _client.PostAsJsonAsync("/api/bookings", new { CustomerName = "", Description = "D", Trips = new[] { TripBody(location, location, day, day.AddHours(1), null) } });
        var tooMany = await _client.PostAsJsonAsync("/api/bookings", new
        {
            CustomerName = "C", Description = "D",
            Trips = Enumerable.Range(0, Booking.MaxTrips + 1).Select(i => TripBody(location, location, day.AddHours(i), day.AddHours(i).AddMinutes(30), null)).ToArray()
        });
        var unknownLocation = await _client.PostAsJsonAsync("/api/bookings", new { CustomerName = "C", Description = "D", Trips = new[] { TripBody(999999, location, day, day.AddHours(1), null) } });
        var tooWide = await _client.GetAsync($"/api/bookings?from={day:yyyy-MM-dd}&to={day.AddDays(32):yyyy-MM-dd}");
        var missing = await _client.GetAsync("/api/bookings/999999");

        Assert.Equal(HttpStatusCode.BadRequest, noTrips.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownLocation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooWide.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", noTrips.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task DriverCandidates_ForReturnTrip_PutDriverOfOutboundFirst()
    {
        var origin = await CreateLocationAsync(40.4168m, -3.7038m);
        var destination = await CreateLocationAsync(40.9480m, -4.1184m);
        var farHome = await CreateLocationAsync(40.6868m, -3.7038m); // ~30 km north of the origin
        var sameBookingDriver = await CreateDriverAsync(farHome);
        var baseAtOriginDriver = await CreateDriverAsync(origin);
        var vehicle = await CreateVehicleAsync();
        var day = new DateTime(2027, 4, 5, 0, 0, 0, DateTimeKind.Utc);

        var booking = await JsonAsync(await _client.PostAsJsonAsync("/api/bookings", new
        {
            CustomerName = "Club de montaña",
            Description = "Excursión ida y vuelta",
            Trips = new[]
            {
                TripBody(origin, destination, day.AddHours(7), day.AddHours(9), "Ida"),
                TripBody(destination, origin, day.AddHours(16), day.AddHours(18), "Vuelta")
            }
        }));
        var outboundId = booking.GetProperty("Trips")[0].GetProperty("Id").GetInt32();
        var returnId = booking.GetProperty("Trips")[1].GetProperty("Id").GetInt32();
        var allocated = await _client.AllocateAsync(outboundId, sameBookingDriver, vehicle);
        Assert.Equal(HttpStatusCode.OK, allocated.StatusCode);

        var candidates = await JsonAsync(await _client.GetAsync($"/api/trips/{returnId}/driver-candidates?maxResults=50"));
        var ids = candidates.EnumerateArray().Select(c => c.GetProperty("DriverId").GetInt32()).ToList();

        Assert.Equal(sameBookingDriver, ids[0]);
        Assert.Contains(baseAtOriginDriver, ids);
        Assert.Contains("reserva", candidates[0].GetProperty("Reason").GetString());
        Assert.True(candidates[0].GetProperty("CoversSameBooking").GetBoolean());

        var detail = await JsonAsync(await _client.GetAsync($"/api/bookings/{booking.GetProperty("Id").GetInt32()}"));
        Assert.Equal("PartiallyAssigned", detail.GetProperty("AssignmentStatus").GetString());
        Assert.NotNull(detail.GetProperty("Trips")[0].GetProperty("AssignedDriverName").GetString());
    }

    private static object TripBody(int origin, int destination, DateTime start, DateTime end, string? description) => new
    {
        OriginLocationId = origin,
        DestinationLocationId = destination,
        Start = start,
        End = end,
        RequiredSeats = 20,
        ServiceType = "Discretionary",
        Description = description
    };

    private async Task<int> CreateLocationAsync(decimal lat = 40.4168m, decimal lon = -3.7038m)
    {
        var response = await _client.PostAsJsonAsync("/api/locations", new
        {
            Name = Unique("L"), Address = "Calle 1", Latitude = lat, Longitude = lon, City = "Madrid", Province = "Madrid"
        });
        response.EnsureSuccessStatusCode();
        return (await JsonAsync(response)).GetProperty("Id").GetInt32();
    }

    private async Task<int> CreateDriverAsync(int homeLocationId)
    {
        var response = await _client.PostAsJsonAsync("/api/drivers", new
        {
            IdentificationNumber = Unique("D"), FirstName = "Conductor", LastName = "Prueba", HomeLocationId = homeLocationId
        });
        response.EnsureSuccessStatusCode();
        return (await JsonAsync(response)).GetProperty("Id").GetInt32();
    }

    private async Task<int> CreateVehicleAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/vehicles", new
        {
            LicensePlate = Unique("V")[..10], Brand = "Irizar", Model = "i6", SeatingCapacity = 55
        });
        response.EnsureSuccessStatusCode();
        return (await JsonAsync(response)).GetProperty("Id").GetInt32();
    }

    private static Location NewLocation(string name) =>
        new(name, "Calle 1", new GeoCoordinate(40.4168m, -3.7038m), "Madrid", "Madrid");

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
}
