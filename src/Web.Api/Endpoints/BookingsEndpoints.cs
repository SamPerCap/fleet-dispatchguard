using DispatchGuard.Application.DTOs.Bookings;
using DispatchGuard.Application.Services.Bookings;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DispatchGuard.API.Endpoints;

public static class BookingsEndpoints
{
    private const string GetBookingByIdRoute = "GetBookingById";

    public static void MapBookingsEndpoints(this IEndpointRouteBuilder app)
    {
        // 503: another operation may be holding one of its trips; the client can retry (SPEC 0065).
        var group = app.MapGroup("/api/bookings")
            .WithTags("Bookings")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/", GetBookings);
        group.MapGet("/{id:int}", GetBooking).WithName(GetBookingByIdRoute);
        group.MapPost("/", CreateBooking);
        group.MapPut("/{id:int}", UpdateBooking);
        group.MapDelete("/{id:int}", DeleteBooking);
    }

    private static async Task<Ok<IReadOnlyList<BookingDto>>> GetBookings(
        DateTime from, DateTime to, IBookingService bookings, CancellationToken cancellationToken) =>
        TypedResults.Ok(await bookings.GetByRangeAsync(from, to, cancellationToken));

    private static async Task<Ok<BookingDto>> GetBooking(
        int id, IBookingService bookings, CancellationToken cancellationToken) =>
        TypedResults.Ok(await bookings.GetAsync(id, cancellationToken));

    private static async Task<CreatedAtRoute<BookingDto>> CreateBooking(
        CreateBookingRequest request, IBookingService bookings, CancellationToken cancellationToken)
    {
        var created = await bookings.CreateAsync(request, cancellationToken);
        return TypedResults.CreatedAtRoute(created, GetBookingByIdRoute, new { id = created.Id });
    }

    private static async Task<Ok<BookingDto>> UpdateBooking(
        int id, UpdateBookingRequest request, IBookingService bookings, CancellationToken cancellationToken) =>
        TypedResults.Ok(await bookings.UpdateAsync(id, request, cancellationToken));

    private static async Task<NoContent> DeleteBooking(
        int id, IBookingService bookings, CancellationToken cancellationToken)
    {
        await bookings.DeleteAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}
