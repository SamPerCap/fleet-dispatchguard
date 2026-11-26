using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;

namespace DispatchGuard.Core.Entities;

/// <summary>
/// A customer's order. Every trip belongs to exactly one booking and the assignment state is derived from the trips, never
/// stored (SPEC 0045). CE 561/2006 is still validated trip by trip.
/// </summary>
public class Booking : Entity
{
    public const int CustomerNameMaxLength = 200;
    public const int DescriptionMaxLength = 500;
    public const int NotesMaxLength = 1000;

    /// <summary>In one creation request.</summary>
    public const int MaxTrips = 50;

    /// <summary>Customer of the booking created for a trip entered on its own. Stored as it is: the web never creates one.</summary>
    public const string UnnamedCustomer = "Sin cliente";

    /// <summary>Description of that booking when the trip has none.</summary>
    public const string DefaultSingleTripDescription = "Servicio";

    public string CustomerName { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public string? Notes { get; private set; }

    private readonly List<Trip> _trips = [];
    public IReadOnlyCollection<Trip> Trips => _trips.AsReadOnly();

    private Booking() { }

    public Booking(string customerName, string description, string? notes = null)
    {
        SetDetails(customerName, description, notes);
    }

    public static Booking ForSingleTrip(string? tripDescription) =>
        new(UnnamedCustomer,
            string.IsNullOrWhiteSpace(tripDescription) ? DefaultSingleTripDescription : Truncate(tripDescription.Trim(), DescriptionMaxLength));

    /// <summary>Over non-cancelled trips; a trip counts as assigned only with both driver and vehicle (SPEC 0045 R2).</summary>
    public BookingAssignmentStatus AssignmentStatus
    {
        get
        {
            var live = _trips.Where(t => t.Status != TripStatus.Cancelled).ToList();
            var assigned = live.Count(t => t.AssignedDriverId.HasValue && t.AssignedVehicleId.HasValue);
            if (assigned == 0) return BookingAssignmentStatus.Unassigned;
            return assigned == live.Count ? BookingAssignmentStatus.FullyAssigned : BookingAssignmentStatus.PartiallyAssigned;
        }
    }

    public void UpdateDetails(string customerName, string description, string? notes)
    {
        SetDetails(customerName, description, notes);
        MarkAsUpdated();
    }

    /// <summary>Cancels every trip, so that they stop counting as driving history or conflicts, and deactivates.</summary>
    public void Cancel()
    {
        foreach (var trip in _trips.Where(t => t.Status != TripStatus.Cancelled))
        {
            trip.Cancel();
        }

        Deactivate();
    }

    /// <summary>Called by the <see cref="Trip"/> constructor, to keep both sides of the relation in sync.</summary>
    internal void Attach(Trip trip)
    {
        if (!_trips.Contains(trip))
        {
            _trips.Add(trip);
        }
    }

    private void SetDetails(string customerName, string description, string? notes)
    {
        EntityValidator.ValidateRequiredString(customerName, nameof(customerName));
        EntityValidator.ValidateRequiredString(description, nameof(description));
        EntityValidator.ValidateMaxLength(customerName.Trim(), CustomerNameMaxLength, Texts.CustomerCannot());
        EntityValidator.ValidateMaxLength(description.Trim(), DescriptionMaxLength, Texts.DescriptionCannot());
        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        EntityValidator.ValidateMaxLength(trimmedNotes, NotesMaxLength, Texts.NotesCannot());

        CustomerName = customerName.Trim();
        Description = description.Trim();
        Notes = trimmedNotes;
    }


    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
