namespace DispatchGuard.Core.ValueObjects;

/// <summary>Raw input of the CE 561/2006 evaluation: one recorded driving interval.</summary>
public sealed class DrivingPeriod : IEquatable<DrivingPeriod>
{
    public int DriverId { get; }
    public DateTime Start { get; }
    public DateTime End { get; }

    /// <summary>To name the later services (SPEC 0063); null when unknown. Not part of equality.</summary>
    public int? TripId { get; }

    public TimeSpan Duration => End - Start;

    public DrivingPeriod(int driverId, DateTime start, DateTime end, int? tripId = null)
    {
        if (end <= start)
        {
            throw new ArgumentException("End must be after start", nameof(end));
        }

        DriverId = driverId;
        Start = start;
        End = end;
        TripId = tripId;
    }

    public TimeSpan GapUntil(DrivingPeriod next) => next.Start - End;

    public bool Equals(DrivingPeriod? other)
    {
        if (other is null)
        {
            return false;
        }

        return DriverId == other.DriverId && Start == other.Start && End == other.End;
    }

    public override bool Equals(object? obj) => Equals(obj as DrivingPeriod);

    public override int GetHashCode() => HashCode.Combine(DriverId, Start, End);

    public override string ToString() => $"Driver {DriverId}: {Start:dd-MM-yyyy HH:mm} → {End:dd-MM-yyyy HH:mm} ({Duration.TotalHours:F2}h)";
}
