using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Entities;

public class Trip : Entity
{
    public const int MaxDescriptionLength = 500;

    public int OriginLocationId { get; private set; }
    public Location OriginLocation { get; private set; } = null!;

    public int DestinationLocationId { get; private set; }
    public Location DestinationLocation { get; private set; } = null!;

    private readonly List<TripStop> _stops = [];
    public IReadOnlyCollection<TripStop> IntermediateStops => _stops.AsReadOnly();

    public TimeWindow ScheduledWindow { get; private set; } = null!;

    public int RequiredSeats { get; private set; }

    public string? Description { get; private set; }

    public int? AssignedDriverId { get; private set; }
    public Driver? AssignedDriver { get; private set; }

    public int? AssignedVehicleId { get; private set; }
    public Vehicle? AssignedVehicle { get; private set; }

    public TripStatus Status { get; private set; }

    public int BookingId { get; private set; }
    public Booking Booking { get; private set; } = null!;

    public int? CircuitId { get; private set; }
    public Circuit? Circuit { get; private set; }

    public ServiceType ServiceType { get; private set; }

    public RouteMetrics? CalculatedMetrics { get; private set; }

    public LegalValidation? LegalValidation { get; private set; }

    public DateTime? LastOptimizationDate { get; private set; }

    /// <summary>Kept out of automatic changes, e.g. assigned by hand or an external booking.</summary>
    public bool IsLocked { get; private set; }

    public string? ForcedReason { get; private set; }

    public string? ForcedBy { get; private set; }

    private readonly List<ForcedByTrip> _forcedByTrips = [];

    /// <summary>Earlier services forced knowing that they would make this one illegal (SPEC 0063 R8), by service.</summary>
    public IReadOnlyList<ForcedByTrip> ForcedByTrips => _forcedByTrips.AsReadOnly();

    /// <summary>The only definition of departed (SPEC 0070). <paramref name="now"/> is a UTC instant, as the window.</summary>
    public bool HasDeparted(DateTime now) => ScheduledWindow.Start <= now;

    /// <summary>
    /// Null when it can be recommended at <paramref name="now"/>. Shared by proposing, checking a proposal and applying it, so
    /// that the three agree (SPEC 0062 R1).
    /// </summary>
    public LocalizedText? WhyNotRecommendable(DateTime now) =>
        !IsActive || Status == TripStatus.Cancelled ? Texts.ServiceCancelled(Id)
        : AssignedDriverId is not null || AssignedVehicleId is not null ? Texts.ServiceAlreadyAssigned(Id)
        : IsLocked ? Texts.ServiceLocked(Id)
        : Status is not (TripStatus.Pending or TripStatus.ValidationFailed) ? Texts.ServiceNotPending(Id)
        : HasDeparted(now) ? Texts.ServiceDeparted(Id)
        : null;

    /// <summary>Forced itself (SPEC 0056 R4), or illegal because of a forced one (SPEC 0063 R8).</summary>
    public bool IsForced => ForcedReason is not null || _forcedByTrips.Count > 0;

    /// <summary>Shown to the dispatcher; null when it is not forced.</summary>
    public LocalizedText? ForcedDescription => !IsForced ? null : Texts.Dotted(
        (ForcedReason is null ? [] : new[] { Texts.ForcedBy(ForcedBy!, ForcedReason) })
        .Concat(_forcedByTrips.Select(f => Texts.ForcedByService(f.SourceTripId, f.By, f.Reason))).ToList());

    private Trip() { }

    public int BaseId { get; private set; } = OperatingBase.DefaultId;

    public void MoveToBase(int baseId)
    {
        EntityValidator.ValidatePositive(baseId, nameof(baseId));
        BaseId = baseId;
        MarkAsUpdated();
    }

    public Trip(
        Location origin,
        Location destination,
        TimeWindow scheduledWindow,
        int requiredSeats,
        ServiceType serviceType,
        string? description = null,
        Booking? booking = null)
    {
        ValidateOrigin(origin);
        ValidateDestination(destination);
        ValidateScheduledWindow(scheduledWindow);
        ValidateRequiredSeats(requiredSeats);
        EntityValidator.ValidateMaxLength(description, MaxDescriptionLength, Texts.ServiceDescriptionCannot());

        OriginLocation = origin;
        OriginLocationId = origin.Id;

        DestinationLocation = destination;
        DestinationLocationId = destination.Id;

        ScheduledWindow = scheduledWindow;
        RequiredSeats = requiredSeats;
        ServiceType = serviceType;
        Description = description;
        Status = TripStatus.Pending;
        IsLocked = false;

        // A trip entered on its own gets its own single-trip booking (SPEC 0045 R1).
        Booking = booking ?? Booking.ForSingleTrip(description);
        BookingId = Booking.Id;
        Booking.Attach(this);
    }

    public void AddIntermediateStop(Location location, TimeWindow stopWindow)
    {
        ValidateLocation(location);
        ValidateStopWindow(stopWindow);

        var sequence = _stops.Count + 1;
        var stop = new TripStop(Id, location, stopWindow, sequence);
        _stops.Add(stop);
        MarkAsUpdated();
    }

    public void AssignResources(Driver driver, Vehicle vehicle)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Cannot assign resources to inactive trip");
        }

        if (IsLocked)
        {
            throw new InvalidOperationException("Cannot reassign locked trip");
        }

        ValidateDriver(driver);
        ValidateVehicle(vehicle);

        AssignedDriver = driver;
        AssignedDriverId = driver.Id;

        AssignedVehicle = vehicle;
        AssignedVehicleId = vehicle.Id;

        // A normal (re)assignment is never a forced one (SPEC 0056 R4), nor depends on what was forced before (SPEC 0063 R8).
        ForcedReason = null;
        ForcedBy = null;
        _forcedByTrips.Clear();

        Status = TripStatus.Assigned;
        MarkAsUpdated();
    }

    /// <summary>
    /// Assigns past a CE 561/2006 rejection by explicit human decision (SPEC 0056). Keeps the non-compliant validation for
    /// audit. Only the orchestrator calls it, after vehicle eligibility and schedule conflicts have passed.
    /// </summary>
    public void ForceAssign(Driver driver, Vehicle vehicle, LegalValidation validation, AllocationOverride forcedBy)
    {
        ArgumentNullException.ThrowIfNull(forcedBy);
        AssignResources(driver, vehicle);
        SetLegalValidation(validation);
        ForcedReason = forcedBy.Reason;
        ForcedBy = forcedBy.By;
        Status = TripStatus.Assigned;
        MarkAsUpdated();
    }

    /// <summary>
    /// The mark and the validation stay: it is the same driver, and the later services it was forced for are still affected
    /// (SPEC 0063 R8).
    /// </summary>
    public void ReassignKeepingForced(Driver driver, Vehicle vehicle)
    {
        var (reason, by, causes) = (ForcedReason, ForcedBy, _forcedByTrips.ToList());
        AssignResources(driver, vehicle);
        ForcedReason = reason;
        ForcedBy = by;
        _forcedByTrips.AddRange(causes);
    }

    /// <summary>
    /// <paramref name="sourceTripId"/> was forced knowing that this later trip stops meeting CE 561/2006 (SPEC 0063 R8).
    /// Keeps the assignment, the status and any mark of its own or of other services. Repeating a source updates its reason.
    /// </summary>
    public void MarkForcedBy(int sourceTripId, AllocationOverride forcedBy, LegalValidation validation)
    {
        ArgumentNullException.ThrowIfNull(forcedBy);
        ValidateLegalValidation(validation);
        _forcedByTrips.RemoveAll(f => f.SourceTripId == sourceTripId);
        _forcedByTrips.Add(new ForcedByTrip(sourceTripId, forcedBy.Reason, forcedBy.By));
        LegalValidation = validation;
        MarkAsUpdated();
    }

    /// <summary>True if <paramref name="sourceTripId"/> had left a mark.</summary>
    public bool ReleaseForcedBy(int sourceTripId)
    {
        if (_forcedByTrips.RemoveAll(f => f.SourceTripId == sourceTripId) == 0)
        {
            return false;
        }

        MarkAsUpdated();
        return true;
    }

    /// <summary>Unlike <see cref="SetLegalValidation"/>, never changes the status of the trip (SPEC 0063 R8).</summary>
    public void RefreshLegalValidation(LegalValidation validation)
    {
        ValidateLegalValidation(validation);
        LegalValidation = validation;
        MarkAsUpdated();
    }

    public void MarkAsManuallyAssigned(Driver driver, Vehicle vehicle)
    {
        AssignResources(driver, vehicle);
        IsLocked = true;
        Status = TripStatus.ManuallyAssigned;
        MarkAsUpdated();
    }

    public void SetRouteMetrics(RouteMetrics metrics)
    {
        ValidateMetrics(metrics);
        CalculatedMetrics = metrics;
        MarkAsUpdated();
    }

    public void SetLegalValidation(LegalValidation validation)
    {
        ValidateLegalValidation(validation);
        LegalValidation = validation;

        if (!validation.IsCompliant)
        {
            Status = TripStatus.ValidationFailed;
        }

        MarkAsUpdated();
    }

    public void MarkAsOptimized()
    {
        LastOptimizationDate = DateTime.UtcNow;
        MarkAsUpdated();
    }

    public void Lock()
    {
        IsLocked = true;
        MarkAsUpdated();
    }

    public void Unlock()
    {
        IsLocked = false;
        MarkAsUpdated();
    }

    public void Cancel()
    {
        Status = TripStatus.Cancelled;
        Deactivate();
    }

    public void Complete()
    {
        Status = TripStatus.Completed;
        MarkAsUpdated();
    }

    public bool RequiresOptimization()
    {
        return IsActive
            && !IsLocked
            && Status == TripStatus.Pending
            && AssignedDriverId == null
            && AssignedVehicleId == null;
    }

    private static void ValidateOrigin(Location? origin)
    {
        EntityValidator.ValidateNotNull(origin, nameof(origin));
    }

    private static void ValidateDestination(Location? destination)
    {
        EntityValidator.ValidateNotNull(destination, nameof(destination));
    }

    private static void ValidateScheduledWindow(TimeWindow? scheduledWindow)
    {
        EntityValidator.ValidateNotNull(scheduledWindow, nameof(scheduledWindow));
    }

    private static void ValidateRequiredSeats(int requiredSeats)
    {
        EntityValidator.ValidatePositive(requiredSeats, nameof(requiredSeats));
    }

    private static void ValidateLocation(Location? location)
    {
        EntityValidator.ValidateNotNull(location, nameof(location));
    }

    private static void ValidateStopWindow(TimeWindow? stopWindow)
    {
        EntityValidator.ValidateNotNull(stopWindow, nameof(stopWindow));
    }

    private static void ValidateDriver(Driver? driver)
    {
        EntityValidator.ValidateNotNull(driver, nameof(driver));
    }

    private static void ValidateVehicle(Vehicle? vehicle)
    {
        EntityValidator.ValidateNotNull(vehicle, nameof(vehicle));
    }

    private static void ValidateMetrics(RouteMetrics? metrics)
    {
        EntityValidator.ValidateNotNull(metrics, nameof(metrics));
    }

    private static void ValidateLegalValidation(LegalValidation? validation)
    {
        EntityValidator.ValidateNotNull(validation, nameof(validation));
    }
}