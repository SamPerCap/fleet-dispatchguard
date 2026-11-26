using DispatchGuard.Application.DTOs.Bookings;
using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Application.Services.MasterData;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Interfaces.Repositories;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Application.Services.Bookings;

public interface IBookingService
{
    Task<IReadOnlyList<BookingDto>> GetByRangeAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<BookingDto> GetAsync(int id, CancellationToken ct = default);
    Task<BookingDto> CreateAsync(CreateBookingRequest request, CancellationToken ct = default);
    Task<BookingDto> UpdateAsync(int id, UpdateBookingRequest request, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

/// <summary>Assigning stays per trip, through <c>allocate</c>: CE 561/2006 is validated trip by trip.</summary>
public sealed class BookingService(IUnitOfWork uow, IForcedConsequences consequences) : IBookingService
{
    private readonly IUnitOfWork _uow = uow ?? throw new ArgumentNullException(nameof(uow));
    private readonly IForcedConsequences _consequences = consequences ?? throw new ArgumentNullException(nameof(consequences));

    public async Task<IReadOnlyList<BookingDto>> GetByRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (to <= from) throw new InvalidRequestException(Texts.FieldAfter(nameof(to), nameof(from)));
        if (to - from > TripService.MaxRange) throw new InvalidRequestException(Texts.RangeTooLong(TripService.MaxRange.TotalDays));

        var bookings = await _uow.Bookings.GetByRangeAsync(from, to, ct);
        return bookings.Select(b => b.ToDto()).ToList();
    }

    public async Task<BookingDto> GetAsync(int id, CancellationToken ct = default) => (await LoadAsync(id, ct)).ToDto();

    public async Task<BookingDto> CreateAsync(CreateBookingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Trips is null || request.Trips.Count == 0)
            throw new InvalidRequestException(Texts.BookingNeedsTrips());
        if (request.Trips.Count > Booking.MaxTrips)
            throw new InvalidRequestException(Texts.BookingTooManyTrips(Booking.MaxTrips));

        var booking = new Booking(request.CustomerName, request.Description, request.Notes);
        foreach (var tripRequest in request.Trips)
        {
            if (tripRequest is null) throw new InvalidRequestException(Texts.NullTrip());
            MasterDataLookups.EnsureDefined(tripRequest.ServiceType, nameof(tripRequest.ServiceType));
            var origin = await MasterDataLookups.ActiveLocationAsync(_uow, tripRequest.OriginLocationId, ct);
            var destination = await MasterDataLookups.ActiveLocationAsync(_uow, tripRequest.DestinationLocationId, ct);
            var trip = new Trip(origin, destination, new TimeWindow(tripRequest.Start, tripRequest.End), tripRequest.RequiredSeats, tripRequest.ServiceType, tripRequest.Description, booking);
            if (tripRequest.BaseId is int baseId)
                trip.MoveToBase(await MasterDataLookups.ActiveBaseIdAsync(_uow, baseId, ct));
        }

        await _uow.Bookings.AddAsync(booking, ct);
        await _uow.SaveChangesAsync(ct);
        return booking.ToDto();
    }

    public async Task<BookingDto> UpdateAsync(int id, UpdateBookingRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var booking = await LoadAsync(id, ct);
        booking.UpdateDetails(request.CustomerName, request.Description, request.Notes);
        await _uow.Bookings.UpdateAsync(booking, ct);
        await _uow.SaveChangesAsync(ct);
        return booking.ToDto();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        // Read outside the exclusive step because the services of a booking are fixed when it is created (SPEC 0065).
        var trips = (await LoadAsync(id, ct)).Trips.Select(t => t.Id).ToList();
        await _uow.ExecuteExclusiveAsync(ResourceClaim.Trips(trips), async token =>
        {
            var booking = await LoadAsync(id, token);
            await _uow.Bookings.DeleteAsync(booking, token);
            var released = await _consequences.ReleaseAsync(trips, token);
            await _uow.SaveChangesAsync(token);
            await _consequences.RevalidateAsync(released, token);
            return booking;
        }, booking => booking, ct);
    }

    private async Task<Booking> LoadAsync(int id, CancellationToken ct) =>
        await _uow.Bookings.GetByIdAsync(id, ct) ?? throw MissingResource.Booking(id);
}
