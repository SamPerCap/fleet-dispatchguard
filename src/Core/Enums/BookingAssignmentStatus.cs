namespace DispatchGuard.Core.Enums;

/// <summary>Derived from the non-cancelled trips of a <see cref="Entities.Booking"/>, never stored (SPEC 0045 R2).</summary>
public enum BookingAssignmentStatus
{
    /// <summary>No trip has both driver and vehicle; also when there are no trips or all are cancelled.</summary>
    Unassigned = 0,

    PartiallyAssigned = 1,

    /// <summary>Every trip has both driver and vehicle.</summary>
    FullyAssigned = 2
}
