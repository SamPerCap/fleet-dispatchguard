using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Entities;

public class TripStop
{
    public int Id { get; private set; }

    public int TripId { get; private set; }
    public Trip Trip { get; private set; } = null!;

    public int LocationId { get; private set; }
    public Location Location { get; private set; } = null!;

    public TimeWindow StopWindow { get; private set; } = null!;

    /// <summary>1-based.</summary>
    public int SequenceOrder { get; private set; }

    public AuditInfo Audit { get; private set; } = new();

    private TripStop() { }

    public TripStop(int tripId, Location location, TimeWindow stopWindow, int sequenceOrder)
    {
        ValidateTripId(tripId);
        ValidateLocation(location);
        ValidateStopWindow(stopWindow);
        ValidateSequenceOrder(sequenceOrder);

        TripId = tripId;
        Location = location;
        LocationId = location.Id;
        StopWindow = stopWindow;
        SequenceOrder = sequenceOrder;
    }

    private static void ValidateTripId(int tripId)
    {
        EntityValidator.ValidatePositive(tripId, nameof(tripId));
    }

    private static void ValidateLocation(Location? location)
    {
        EntityValidator.ValidateNotNull(location, nameof(location));
    }

    private static void ValidateStopWindow(TimeWindow? stopWindow)
    {
        EntityValidator.ValidateNotNull(stopWindow, nameof(stopWindow));
    }

    private static void ValidateSequenceOrder(int sequenceOrder)
    {
        EntityValidator.ValidatePositive(sequenceOrder, nameof(sequenceOrder));
    }
}