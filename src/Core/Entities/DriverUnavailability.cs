using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Core.Interfaces.ValueObjects;

namespace DispatchGuard.Core.Entities;

public class DriverUnavailability : IUnavailability
{
    public int Id { get; private set; }
    public int DriverId { get; private set; }
    public Driver Driver { get; private set; } = null!;
    public TimeWindow Period { get; private set; } = null!;
    public UnavailabilityType Type { get; private set; }
    public string? Notes { get; private set; }
    public AuditInfo Audit { get; private set; } = new();

    private DriverUnavailability() { }

    public DriverUnavailability(
        int driverId,
        TimeWindow period,
        UnavailabilityType type,
        string? notes = null)
    {
        ValidateDriverId(driverId);
        ValidatePeriod(period);

        DriverId = driverId;
        Period = period;
        Type = type;
        Notes = AgendaEntryRules.Notes(notes);
    }

    public void Change(TimeWindow period, UnavailabilityType type, string? notes)
    {
        ValidatePeriod(period);

        Notes = AgendaEntryRules.Notes(notes);
        Period = period;
        Type = type;
        Audit.MarkAsUpdated();
    }

    public bool OverlapsWith(TimeWindow window) => Period.OverlapsWith(window);

    public bool IsWeeklyRest() => Type == UnavailabilityType.WeeklyRest;

    private static void ValidateDriverId(int driverId)
    {
        EntityValidator.ValidatePositive(driverId, nameof(driverId));
    }

    private static void ValidatePeriod(TimeWindow? period)
    {
        EntityValidator.ValidateNotNull(period, nameof(period));
    }
}