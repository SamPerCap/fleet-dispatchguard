using DispatchGuard.Application.DTOs.MasterData;
using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;

namespace DispatchGuard.Application.DTOs.Bookings;

/// <summary>Driver and vehicle are null while unassigned.</summary>
public sealed record BookingTripDto(
    int Id,
    int OriginLocationId,
    int DestinationLocationId,
    DateTime Start,
    DateTime End,
    int RequiredSeats,
    string ServiceType,
    string Status,
    string? Description,
    int? AssignedDriverId,
    string? AssignedDriverName,
    int? AssignedVehicleId,
    string? AssignedVehiclePlate,
    bool IsLocked,
    string? ForcedReason = null,
    string? ForcedBy = null,
    LocalizedText? ForcedDescription = null);

/// <summary>Trips are ordered by start; the assignment state is derived from them (SPEC 0045 R2).</summary>
public sealed record BookingDto(
    int Id,
    string CustomerName,
    string Description,
    string? Notes,
    string AssignmentStatus,
    DateTime? Start,
    DateTime? End,
    bool IsActive,
    IReadOnlyList<BookingTripDto> Trips);

/// <summary>With 1..<see cref="Booking.MaxTrips"/> trips.</summary>
public sealed record CreateBookingRequest(
    string CustomerName,
    string Description,
    IReadOnlyList<CreateTripRequest> Trips,
    string? Notes = null);

/// <summary>Only the booking's own data: its trips have their own endpoints.</summary>
public sealed record UpdateBookingRequest(string CustomerName, string Description, string? Notes = null);

/// <summary>New resources are projected by hand, without AutoMapper (SPEC 0036).</summary>
public static class BookingMappings
{
    public static BookingDto ToDto(this Booking booking)
    {
        var trips = booking.Trips.OrderBy(t => t.ScheduledWindow.Start).ThenBy(t => t.Id).ToList();
        var live = trips.Where(t => t.Status != TripStatus.Cancelled).ToList();
        return new BookingDto(
            booking.Id, booking.CustomerName, booking.Description, booking.Notes, booking.AssignmentStatus.ToString(),
            live.Count > 0 ? live.Min(t => t.ScheduledWindow.Start) : null,
            live.Count > 0 ? live.Max(t => t.ScheduledWindow.End) : null,
            booking.IsActive,
            trips.Select(ToBookingTripDto).ToList());
    }

    private static BookingTripDto ToBookingTripDto(Trip trip) => new(
        trip.Id, trip.OriginLocationId, trip.DestinationLocationId, trip.ScheduledWindow.Start, trip.ScheduledWindow.End,
        trip.RequiredSeats, trip.ServiceType.ToString(), trip.Status.ToString(), trip.Description,
        trip.AssignedDriverId, trip.AssignedDriver?.FullName, trip.AssignedVehicleId, trip.AssignedVehicle?.LicensePlate, trip.IsLocked,
        trip.ForcedReason, trip.ForcedBy, trip.ForcedDescription);
}
