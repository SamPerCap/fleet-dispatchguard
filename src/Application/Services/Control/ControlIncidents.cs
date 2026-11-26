namespace DispatchGuard.Application.Services.Control;

/// <summary>One raw alert, with the group it belongs to and the trips it affects.</summary>
public sealed record AlertFact(ControlAlertDto Alert, string GroupKey, IReadOnlyList<AffectedTripDto> Trips)
{
    public static string BookingKey(int bookingId) => $"Booking:{bookingId}";
    public static string DriverKey(int driverId) => $"Driver:{driverId}";
    public static string VehicleKey(int vehicleId) => $"Vehicle:{vehicleId}";
}

/// <summary>
/// Related alerts become one incident: uncovered trips with their booking, everything about a vehicle, everything about a
/// driver, forced assignments included (SPEC 0057 R2, R3).
/// </summary>
public static class ControlIncidents
{
    public static IReadOnlyList<ControlIncidentDto> Group(IEnumerable<AlertFact> facts) =>
        facts.GroupBy(f => f.GroupKey, StringComparer.Ordinal)
            .Select(Merge)
            .OrderBy(i => i.Severity) // Urgent before warning.
            .ThenBy(i => i.Deadline ?? DateTime.MaxValue) // Without deadline, last.
            .ThenByDescending(i => i.AffectedTripCount)
            .ThenByDescending(i => i.AffectedSeats)
            .ThenBy(i => i.Key, StringComparer.Ordinal)
            .ToList();

    private static ControlIncidentDto Merge(IGrouping<string, AlertFact> group)
    {
        // The most severe and, among equals, the one with the nearest deadline; then a stable order.
        var main = group
            .OrderBy(f => f.Alert.Severity)
            .ThenBy(f => f.Alert.Deadline ?? DateTime.MaxValue)
            .ThenBy(f => f.Alert.Type)
            .ThenBy(f => f.Alert.EntityId)
            .First().Alert;

        var trips = group.SelectMany(f => f.Trips)
            .GroupBy(t => t.TripId).Select(g => g.First())
            .OrderBy(t => t.Start).ThenBy(t => t.TripId)
            .ToList();

        return new ControlIncidentDto(
            Key: group.Key,
            Severity: group.Min(f => f.Alert.Severity),
            Type: main.Type,
            EntityType: main.EntityType,
            EntityId: main.EntityId,
            EntityLabel: main.EntityLabel,
            Deadline: group.Min(f => f.Alert.Deadline),
            RecommendedAction: main.SuggestedAction,
            Detail: main.Detail,
            AffectedTripCount: trips.Count,
            AffectedSeats: trips.Sum(t => t.Seats),
            AffectedTrips: trips,
            Types: group.Select(f => f.Alert.Type).Distinct().OrderBy(t => t).ToList());
    }
}
