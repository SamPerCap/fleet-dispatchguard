using DispatchGuard.Core.Entities;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Entities;

public class BookingTests
{
    private static readonly DateTime Day = new(2026, 12, 1, 8, 0, 0);

    private static Location NewLocation(string name) =>
        new(name, "Calle Falsa 123", new GeoCoordinate(40.4168m, -3.7038m), "Madrid", "Madrid");

    private static Trip NewTrip(Booking? booking = null, int hour = 0, string? description = null) =>
        new(NewLocation("A"), NewLocation("B"), new TimeWindow(Day.AddHours(hour), Day.AddHours(hour + 2)), 20,
            ServiceType.Discretionary, description, booking);

    private static void Assign(Trip trip) =>
        trip.AssignResources(new Driver("ID1", "Ana", "López"), new Vehicle("1234ABC", "Irizar", "i6", 55));

    private static Booking Excursion() => new("Colegio San José", "Excursión a Segovia", "Recoger en la puerta principal");

    [Fact]
    public void Constructor_ValidData_SetsFieldsWithoutTrips()
    {
        var booking = Excursion();

        Assert.Equal("Colegio San José", booking.CustomerName);
        Assert.Equal("Excursión a Segovia", booking.Description);
        Assert.Equal("Recoger en la puerta principal", booking.Notes);
        Assert.Empty(booking.Trips);
        Assert.True(booking.IsActive);
    }

    [Theory]
    [InlineData("", "desc")]
    [InlineData("  ", "desc")]
    [InlineData("Cliente", "")]
    public void Constructor_MissingCustomerOrDescription_Throws(string customer, string description) =>
        Assert.ThrowsAny<ArgumentException>(() => new Booking(customer, description));

    [Fact]
    public void Constructor_TooLongFields_Throw()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Booking(new string('a', Booking.CustomerNameMaxLength + 1), "d"));
        Assert.ThrowsAny<ArgumentException>(() => new Booking("c", new string('a', Booking.DescriptionMaxLength + 1)));
        Assert.ThrowsAny<ArgumentException>(() => new Booking("c", "d", new string('a', Booking.NotesMaxLength + 1)));
        _ = new Booking(new string('a', Booking.CustomerNameMaxLength), new string('a', Booking.DescriptionMaxLength), new string('a', Booking.NotesMaxLength));
    }

    [Fact]
    public void TripConstructor_WithBooking_JoinsThatBooking()
    {
        var booking = Excursion();

        var outbound = NewTrip(booking, 0);
        var inbound = NewTrip(booking, 8);

        Assert.Same(booking, outbound.Booking);
        Assert.Equal([outbound, inbound], booking.Trips);
    }

    [Fact]
    public void TripConstructor_WithoutBooking_GetsItsOwnSingleTripBooking()
    {
        var trip = NewTrip(description: "Traslado aeropuerto");

        Assert.NotNull(trip.Booking);
        Assert.Equal(Booking.UnnamedCustomer, trip.Booking.CustomerName);
        Assert.Equal("Traslado aeropuerto", trip.Booking.Description);
        Assert.Equal([trip], trip.Booking.Trips);
    }

    [Fact]
    public void TripConstructor_WithoutBookingNorDescription_UsesDefaultDescription() =>
        Assert.Equal(Booking.DefaultSingleTripDescription, NewTrip().Booking.Description);

    [Fact]
    public void AssignmentStatus_NoTripAssigned_IsUnassigned()
    {
        var booking = Excursion();
        NewTrip(booking, 0);
        NewTrip(booking, 8);

        Assert.Equal(BookingAssignmentStatus.Unassigned, booking.AssignmentStatus);
    }

    [Fact]
    public void AssignmentStatus_SomeAssigned_IsPartiallyAssigned()
    {
        var booking = Excursion();
        Assign(NewTrip(booking, 0));
        NewTrip(booking, 8);

        Assert.Equal(BookingAssignmentStatus.PartiallyAssigned, booking.AssignmentStatus);
    }

    [Fact]
    public void AssignmentStatus_AllAssigned_IsFullyAssigned()
    {
        var booking = Excursion();
        Assign(NewTrip(booking, 0));
        Assign(NewTrip(booking, 8));

        Assert.Equal(BookingAssignmentStatus.FullyAssigned, booking.AssignmentStatus);
    }

    [Fact]
    public void AssignmentStatus_CancelledPendingTripIsIgnored_IsFullyAssigned()
    {
        var booking = Excursion();
        Assign(NewTrip(booking, 0));
        NewTrip(booking, 8).Cancel();

        Assert.Equal(BookingAssignmentStatus.FullyAssigned, booking.AssignmentStatus);
    }

    [Fact]
    public void AssignmentStatus_CancelledAssignedTripIsIgnored_IsUnassigned()
    {
        var booking = Excursion();
        var cancelled = NewTrip(booking, 0);
        Assign(cancelled);
        cancelled.Cancel();
        NewTrip(booking, 8);

        Assert.Equal(BookingAssignmentStatus.Unassigned, booking.AssignmentStatus);
    }

    [Fact]
    public void AssignmentStatus_AllTripsCancelledOrNone_IsUnassigned()
    {
        var empty = Excursion();
        var allCancelled = Excursion();
        NewTrip(allCancelled, 0).Cancel();

        Assert.Equal(BookingAssignmentStatus.Unassigned, empty.AssignmentStatus);
        Assert.Equal(BookingAssignmentStatus.Unassigned, allCancelled.AssignmentStatus);
    }

    [Fact]
    public void UpdateDetails_ChangesFieldsAndValidates()
    {
        var booking = Excursion();

        booking.UpdateDetails("Colegio Nuevo", "Excursión a Toledo", null);

        Assert.Equal("Colegio Nuevo", booking.CustomerName);
        Assert.Equal("Excursión a Toledo", booking.Description);
        Assert.Null(booking.Notes);
        Assert.ThrowsAny<ArgumentException>(() => booking.UpdateDetails("", "x", null));
    }

    [Fact]
    public void Cancel_CancelsEveryTripAndDeactivates()
    {
        var booking = Excursion();
        var outbound = NewTrip(booking, 0);
        var inbound = NewTrip(booking, 8);

        booking.Cancel();

        Assert.False(booking.IsActive);
        Assert.All([outbound, inbound], t => Assert.Equal(TripStatus.Cancelled, t.Status));
    }
}
