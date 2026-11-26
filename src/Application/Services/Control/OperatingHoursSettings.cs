namespace DispatchGuard.Application.Services.Control;

/// <summary>
/// Fixed operating day of each base, in Europe/Madrid local time (SPEC 0058 R3, user decision, 2026-11-19). Configured in
/// <c>Control:OperatingHours</c> (<c>Start</c>, <c>End</c> and optional <c>Bases:{id}</c> overrides); default 06:00–22:00.
/// Overnight days (end before start) are not supported.
/// </summary>
public sealed class OperatingHoursSettings
{
    public const string Section = "Control:OperatingHours";

    public TimeOnly Start { get; set; } = new(6, 0);
    public TimeOnly End { get; set; } = new(22, 0);
    public Dictionary<int, BaseOperatingHours> Bases { get; set; } = [];

    public (TimeOnly Start, TimeOnly End) For(int baseId) =>
        Bases.TryGetValue(baseId, out var hours) ? (hours.Start, hours.End) : (Start, End);

    /// <summary>[from, to) in UTC, on the Madrid day that starts at <paramref name="dayFrom"/>.</summary>
    public (DateTime From, DateTime To) On(DateTime dayFrom, int baseId)
    {
        var (start, end) = For(baseId);
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(dayFrom, DateTimeKind.Utc), OperationalDay.TimeZone).Date;
        return (ToUtc(localDate.Add(start.ToTimeSpan())), ToUtc(localDate.Add(end.ToTimeSpan())));
    }

    /// <summary>At startup: throws when a configured day is empty or overnight.</summary>
    public OperatingHoursSettings Validate()
    {
        foreach (var (id, (start, end)) in Bases.Select(kv => (kv.Key, (kv.Value.Start, kv.Value.End))).Prepend((0, (Start, End))))
            if (end <= start) throw new InvalidOperationException($"{Section}: end must be after start{(id == 0 ? "" : $" (base {id})")}");
        return this;
    }

    private static DateTime ToUtc(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // A local time skipped by the spring change (02:00–03:00) moves to the next valid instant.
        while (OperationalDay.TimeZone.IsInvalidTime(unspecified)) unspecified = unspecified.AddMinutes(30);
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(unspecified, OperationalDay.TimeZone), DateTimeKind.Utc);
    }
}

public sealed class BaseOperatingHours
{
    public TimeOnly Start { get; set; } = new(6, 0);
    public TimeOnly End { get; set; } = new(22, 0);
}
