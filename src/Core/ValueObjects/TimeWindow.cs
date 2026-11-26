namespace DispatchGuard.Core.ValueObjects;

public sealed class TimeWindow : IEquatable<TimeWindow>
{
    public DateTime Start { get; }
    public DateTime End { get; }

    public TimeSpan Duration => End - Start;

    // Only for EF Core.
    public TimeWindow()
    {

    }

    public TimeWindow(DateTime start, DateTime end)
    {
        if (end <= start)
        {
            throw new InvalidRequestException(Texts.EndAfterStart());
        }

        Start = start;
        End = end;
    }

    public static TimeWindow FromDateAndTime(DateTime startDate, TimeSpan startTime, DateTime endDate, TimeSpan endTime)
    {
        var start = startDate.Date.Add(startTime);
        var end = endDate.Date.Add(endTime);
        return new TimeWindow(start, end);
    }

    public bool OverlapsWith(TimeWindow other)
    {
        return Start < other.End && End > other.Start;
    }

    public bool Contains(DateTime dateTime)
    {
        return dateTime >= Start && dateTime <= End;
    }

    public bool Equals(TimeWindow? other)
    {
        if (other is null)
        {
            return false;
        }

        return Start == other.Start && End == other.End;
    }

    public override bool Equals(object? obj) => Equals(obj as TimeWindow);

    public override int GetHashCode() => HashCode.Combine(Start, End);

    public override string ToString() => $"{Start:dd-MM-yyyy HH:mm} → {End:dd-MM-yyyy HH:mm}";
}