using System.Net;
using System.Net.Http.Json;
using DispatchGuard.Application.DTOs.Bookings;
using DispatchGuard.Application.DTOs.MasterData;

namespace DispatchGuard.DemoData;

public sealed record SeedReport(
    int BasesCreated, int LocationsCreated, int DriversCreated, int VehiclesCreated, int BookingsCreated, int TripsCreated,
    int TripsAllocated, int AttemptsRejected, int LeavesCreated);

/// <summary>
/// Loads the demo through the public API (SPEC 0053). Idempotent: looks up existing rows by natural key (base and location name,
/// identification number, plate, trip description) and only creates or allocates what is missing.
/// Every allocation goes through <c>POST /api/trips/{id}/allocate</c>, so the backend's CE 561/2006 checks decide; the
/// planned wrong attempts must be rejected and the planned allocations accepted, otherwise the loader aborts.
/// Leaves are registered last, after the allocations they turn into incidents.
/// </summary>
public sealed class ApiSeeder(HttpClient http)
{
    public const string Operator = "Despacho (demo)";

    private const string WriteHeader = "X-DispatchGuard-Request";

    private sealed record BaseRow(int Id, string Name);

    public async Task<SeedReport> SeedAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        await StartSessionAsync(ct);
        var plan = DemoPlan.Build(nowUtc);

        // The default base (Id 1) is renamed; the others are created by name.
        var bases = await Get<List<BaseRow>>("/api/bases", ct);
        var baseIds = new Dictionary<string, int>();
        var basesCreated = 0;
        foreach (var demoBase in DemoDataset.Bases)
        {
            if (demoBase.IsDefault)
            {
                if (bases.First(x => x.Id == 1).Name != demoBase.Name) await Send<BaseRow>(HttpMethod.Put, "/api/bases/1", new BaseRequest(demoBase.Name), ct);
                baseIds[demoBase.Key] = 1;
                continue;
            }
            var known = bases.FirstOrDefault(x => x.Name == demoBase.Name);
            baseIds[demoBase.Key] = known?.Id ?? (await Send<BaseRow>(HttpMethod.Post, "/api/bases", new BaseRequest(demoBase.Name), ct)).Id;
            if (known is null) basesCreated++;
        }

        var locations = (await Get<List<LocationDto>>("/api/locations", ct)).ToDictionary(l => l.Name, l => l.Id);
        var locationsCreated = 0;
        foreach (var location in DemoDataset.Locations.Where(l => !locations.ContainsKey(l.Name)))
        {
            var dto = await Send<LocationDto>(HttpMethod.Post, "/api/locations",
                new CreateLocationRequest(location.Name, location.Address, location.Latitude, location.Longitude, location.City, location.Province), ct);
            locations[location.Name] = dto.Id;
            locationsCreated++;
        }
        int LocationId(string key) => locations[DemoDataset.Location(key).Name];

        var drivers = (await Get<List<DriverDto>>("/api/drivers", ct)).ToDictionary(d => d.IdentificationNumber);
        var driversCreated = 0;
        foreach (var demoBase in DemoDataset.Bases)
            foreach (var driver in demoBase.Drivers.Where(d => !drivers.ContainsKey(d.IdentificationNumber)))
            {
                drivers[driver.IdentificationNumber] = await Send<DriverDto>(HttpMethod.Post, "/api/drivers",
                    new CreateDriverRequest(driver.IdentificationNumber, driver.FirstName, driver.LastName, HomeLocationId: LocationId(driver.HomeKey), BaseId: baseIds[demoBase.Key]), ct);
                driversCreated++;
            }

        var vehicles = (await Get<List<VehicleDto>>("/api/vehicles", ct)).ToDictionary(v => v.LicensePlate);
        var vehiclesCreated = 0;
        foreach (var demoBase in DemoDataset.Bases)
            foreach (var vehicle in demoBase.Vehicles.Where(v => !vehicles.ContainsKey(v.LicensePlate)))
            {
                vehicles[vehicle.LicensePlate] = await Send<VehicleDto>(HttpMethod.Post, "/api/vehicles",
                    new CreateVehicleRequest(vehicle.LicensePlate, vehicle.Brand, vehicle.Model, vehicle.SeatingCapacity, HomeLocationId: LocationId(vehicle.HomeKey), BaseId: baseIds[demoBase.Key]), ct);
                vehiclesCreated++;
            }

        // Each booking is created in one call with the trips it still lacks.
        var existing = await ExistingTripsAsync(plan.Trips.Min(t => t.Start).AddDays(-1), plan.Trips.Max(t => t.End).AddDays(1), ct);
        int bookingsCreated = 0, tripsCreated = 0;
        foreach (var group in plan.Trips.GroupBy(p => p.Booking).OrderBy(g => g.Min(p => p.Start)))
        {
            var missing = group.Where(p => !existing.ContainsKey(p.Description)).OrderBy(p => p.Start).ToList();
            if (missing.Count == 0) continue;
            var booking = group.Key;
            var created = await Send<BookingDto>(HttpMethod.Post, "/api/bookings", new CreateBookingRequest(booking.CustomerName, booking.Description,
                missing.Select(p => new CreateTripRequest(LocationId(p.OriginKey), LocationId(p.DestinationKey), p.Start, p.End,
                    p.RequiredSeats, p.ServiceType, p.Description, baseIds[p.BaseKey])).ToList(), booking.Notes), ct);
            foreach (var trip in created.Trips)
                existing[trip.Description!] = new ExistingTrip(trip.Id, trip.AssignedDriverId, trip.Status);
            bookingsCreated++;
            tripsCreated += missing.Count;
        }

        // Chronological within each base: the legal history builds up in order. Wrong attempts first, only on a trip still
        // unassigned. Bases share no driver or vehicle, so they are loaded in parallel.
        var rejectionsByTrip = plan.Rejections.ToLookup(r => r.TripDescription);
        int allocated = 0, rejected = 0;
        async Task AllocateBaseAsync(IEnumerable<PlannedTrip> trips)
        {
            foreach (var planned in trips)
            {
                var trip = existing[planned.Description];
                if (trip.AssignedDriverId is not null || trip.Status == "Cancelled") continue;
                // Seeding again after the leave was registered: the service stays unassigned, as it would for a dispatcher (SPEC 0066).
                if (drivers[planned.DriverId!].Unavailabilities.Any(u => u.Start < planned.End && planned.Start < u.End)) continue;

                foreach (var rejection in rejectionsByTrip[planned.Description])
                {
                    using var wrong = await AllocateAsync(trip.Id, drivers[rejection.DriverId].Id, vehicles[rejection.VehiclePlate].Id, ct);
                    if (wrong.IsSuccessStatusCode)
                        throw new InvalidOperationException($"The API accepted an attempt that it had to reject ({rejection.Kind}) in '{planned.Description}'.");
                    // A busy answer is not a rejection (SPEC 0065).
                    if (wrong.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity))
                        throw new InvalidOperationException($"The API did not get to decide ({rejection.Kind}) in '{planned.Description}': {(int)wrong.StatusCode}.");
                    Interlocked.Increment(ref rejected);
                }

                HttpResponseMessage response;
                try
                {
                    response = await AllocateAsync(trip.Id, drivers[planned.DriverId!].Id, vehicles[planned.VehiclePlate!].Id, ct);
                }
                catch (TaskCanceledException ex)
                {
                    throw new InvalidOperationException($"The API did not answer when allocating '{planned.Description}' (service {trip.Id}, {planned.Start:u}).", ex);
                }
                using var _ = response;
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(
                        $"The API refused to allocate '{planned.Description}' ({planned.Start:u}): {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
                Interlocked.Increment(ref allocated);
            }
        }
        await Task.WhenAll(plan.Trips.Where(t => t.Covered).GroupBy(t => t.BaseKey).Select(g => AllocateBaseAsync(g.ToList())));

        var leaves = 0;
        foreach (var leave in plan.DriverLeaves)
        {
            var id = drivers[leave.DriverId].Id;
            var current = await Get<DriverDto>($"/api/drivers/{id}", ct);
            if (current.Unavailabilities.Any(u => u.Notes == leave.Notes)) continue;
            await Send<object>(HttpMethod.Post, $"/api/drivers/{id}/unavailabilities", new CreateDriverUnavailabilityRequest(leave.Start, leave.End, leave.Type, leave.Notes), ct);
            leaves++;
        }
        foreach (var leave in plan.VehicleLeaves)
        {
            var id = vehicles[leave.VehiclePlate].Id;
            var current = await Get<VehicleDto>($"/api/vehicles/{id}", ct);
            if (current.Unavailabilities.Any(u => u.Notes == leave.Notes)) continue;
            await Send<object>(HttpMethod.Post, $"/api/vehicles/{id}/unavailabilities",
                new CreateVehicleUnavailabilityRequest(leave.Start, leave.End, leave.Type, leave.Notes, "Taller Hermanos Ruiz (demo)"), ct);
            leaves++;
        }

        return new SeedReport(basesCreated, locationsCreated, driversCreated, vehiclesCreated, bookingsCreated, tripsCreated, allocated, rejected, leaves);
    }

    /// <summary>The API writes nothing without a session (SPEC 0084); the cookie stays in the handler of the client.</summary>
    private async Task StartSessionAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync("/api/session", ct);
        response.EnsureSuccessStatusCode();
        if (!http.DefaultRequestHeaders.Contains(WriteHeader)) http.DefaultRequestHeaders.Add(WriteHeader, "1");
    }

    private Task<HttpResponseMessage> AllocateAsync(int tripId, int driverId, int vehicleId, CancellationToken ct) =>
        http.PostAsJsonAsync($"/api/trips/{tripId}/allocate", new { DriverId = driverId, VehicleId = vehicleId, AssignedBy = Operator }, ct);

    private sealed record ExistingTrip(int Id, int? AssignedDriverId, string Status);

    /// <summary>GET /api/trips accepts at most 31 days (SPEC 0044): the plan of 5 weeks is read in 30-day slices.</summary>
    private async Task<Dictionary<string, ExistingTrip>> ExistingTripsAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var result = new Dictionary<string, ExistingTrip>();
        for (var sliceFrom = from; sliceFrom < to; sliceFrom = sliceFrom.AddDays(30))
        {
            var sliceTo = sliceFrom.AddDays(30) < to ? sliceFrom.AddDays(30) : to;
            for (var page = 1; ; page++)
            {
                var batch = await Get<PagedDto<TripDto>>($"/api/trips?from={sliceFrom:s}&to={sliceTo:s}&page={page}&pageSize=200", ct);
                foreach (var trip in batch.Items.Where(t => t.Description is not null && t.Status != "Cancelled"))
                    result.TryAdd(trip.Description!, new ExistingTrip(trip.Id, trip.AssignedDriverId, trip.Status));
                if (page * batch.PageSize >= batch.TotalCount) break;
            }
        }
        return result;
    }

    private async Task<T> Get<T>(string url, CancellationToken ct) =>
        await http.GetFromJsonAsync<T>(url, ct) ?? throw new InvalidOperationException($"Empty answer from {url}");

    private async Task<T> Send<T>(HttpMethod method, string url, object body, CancellationToken ct)
    {
        using var response = await http.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body, body.GetType()) }, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{method} {url} → {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new InvalidOperationException($"Empty answer from {url}");
    }
}
