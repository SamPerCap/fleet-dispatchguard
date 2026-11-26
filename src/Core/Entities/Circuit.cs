using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Entities;

/// <summary>Connected trips that reuse resources, e.g. a multi-day tour or a round trip.</summary>
public class Circuit : Entity
{
    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public TimeWindow Duration { get; private set; } = null!;

    private readonly List<Trip> _trips = [];
    public IReadOnlyCollection<Trip> Trips => _trips.AsReadOnly();

    /// <summary>A preference for the optimizer, not a constraint.</summary>
    public bool PreferSameDriver { get; private set; }

    /// <summary>A preference for the optimizer, not a constraint.</summary>
    public bool PreferSameVehicle { get; private set; }

    private Circuit() { }

    public Circuit(
        string name,
        TimeWindow duration,
        string? description = null,
        bool preferSameDriver = true,
        bool preferSameVehicle = true)
    {
        ValidateName(name);
        ValidateDuration(duration);

        Name = name.Trim();
        Duration = duration;
        Description = description;
        PreferSameDriver = preferSameDriver;
        PreferSameVehicle = preferSameVehicle;
    }

    public void AddTrip(Trip trip)
    {
        ValidateTrip(trip);

        if (trip.ServiceType != ServiceType.Circuit)
        {
            throw new InvalidOperationException(
                "Trip added to a circuit must have ServiceType.Circuit");
        }

        if (!trip.ScheduledWindow.OverlapsWith(Duration))
        {
            throw new InvalidOperationException("Trip must fall within circuit duration");
        }

        _trips.Add(trip);
        MarkAsUpdated();
    }

    public void RemoveTrip(Trip trip)
    {
        ValidateTrip(trip);
        _trips.Remove(trip);
        MarkAsUpdated();
    }

    public IEnumerable<Trip> GetTripsInSequence()
    {
        return _trips.OrderBy(t => t.ScheduledWindow.Start);
    }

    /// <summary>CE 561/2006 Art. 8.6: a weekly rest is required within 6 consecutive 24h periods.</summary>
    public bool AllowsWeeklyRest()
    {
        return Duration.Duration <= DrivingLimits.MaxIntervalBetweenWeeklyRests;
    }

    public TimeSpan GetTotalDrivingTime()
    {
        return TimeSpan.FromMinutes(
            _trips
                .Where(t => t.CalculatedMetrics != null)
                .Sum(t => t.CalculatedMetrics!.EstimatedDuration.TotalMinutes)
        );
    }

    public bool HasConsistentDriverAssignment()
    {
        if (_trips.Count == 0)
        {
            return false;
        }

        var firstDriverId = _trips.First().AssignedDriverId;
        return firstDriverId.HasValue && _trips.All(t => t.AssignedDriverId == firstDriverId);
    }

    public bool HasConsistentVehicleAssignment()
    {
        if (_trips.Count == 0)
        {
            return false;
        }

        var firstVehicleId = _trips.First().AssignedVehicleId;
        return firstVehicleId.HasValue && _trips.All(t => t.AssignedVehicleId == firstVehicleId);
    }

    public void UpdatePreferences(bool preferSameDriver, bool preferSameVehicle)
    {
        PreferSameDriver = preferSameDriver;
        PreferSameVehicle = preferSameVehicle;
        MarkAsUpdated();
    }

    private static void ValidateName(string? name)
    {
        EntityValidator.ValidateRequiredString(name, nameof(name));
    }

    private static void ValidateDuration(TimeWindow? duration)
    {
        EntityValidator.ValidateNotNull(duration, nameof(duration));
    }

    private static void ValidateTrip(Trip? trip)
    {
        EntityValidator.ValidateNotNull(trip, nameof(trip));
    }
}