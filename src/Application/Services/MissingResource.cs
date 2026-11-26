namespace DispatchGuard.Application.Services;


public static class MissingResource
{
    public static KeyNotFoundException Trip(int id) => new ResourceNotFoundException(Texts.MissingTrip(id));

    public static KeyNotFoundException Driver(int id) => new ResourceNotFoundException(Texts.MissingDriver(id));

    public static KeyNotFoundException Vehicle(int id) => new ResourceNotFoundException(Texts.MissingVehicle(id));

    public static KeyNotFoundException Booking(int id) => new ResourceNotFoundException(Texts.MissingBooking(id));

    public static KeyNotFoundException Base(int id) => new ResourceNotFoundException(Texts.MissingBase(id));

    public static KeyNotFoundException Location(int id) => new ResourceNotFoundException(Texts.MissingLocation(id));

    public static KeyNotFoundException Client(int id) => new ResourceNotFoundException(Texts.MissingClient(id));

    public static KeyNotFoundException AllocationAttempt(int id) => new ResourceNotFoundException(Texts.MissingAttempt(id));

    public static KeyNotFoundException DriverUnavailability(int unavailabilityId, int driverId) =>
        new ResourceNotFoundException(Texts.MissingDriverUnavailability(unavailabilityId, driverId));

    public static KeyNotFoundException VehicleUnavailability(int unavailabilityId, int vehicleId) =>
        new ResourceNotFoundException(Texts.MissingVehicleUnavailability(unavailabilityId, vehicleId));
}
