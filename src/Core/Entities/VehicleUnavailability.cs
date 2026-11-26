using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.Enums;
using DispatchGuard.Core.ValueObjects;
using Core.Interfaces.ValueObjects;

namespace DispatchGuard.Core.Entities;

public class VehicleUnavailability : IUnavailability
{
    public int Id { get; private set; }

    public int VehicleId { get; private set; }
    public Vehicle Vehicle { get; private set; } = null!;

    public TimeWindow Period { get; private set; } = null!;

    public VehicleUnavailabilityType Type { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Workshop or company that does the work.</summary>
    public string? ServiceProvider { get; private set; }

    public AuditInfo Audit { get; private set; } = new();

    private VehicleUnavailability() { }

    public VehicleUnavailability(
        int vehicleId,
        TimeWindow period,
        VehicleUnavailabilityType type,
        string? notes = null,
        string? serviceProvider = null)
    {
        ValidateVehicleId(vehicleId);
        ValidatePeriod(period);

        VehicleId = vehicleId;
        Period = period;
        Type = type;
        Notes = AgendaEntryRules.Notes(notes);
        ServiceProvider = AgendaEntryRules.ServiceProvider(serviceProvider);
    }

    public void Change(TimeWindow period, VehicleUnavailabilityType type, string? notes, string? serviceProvider)
    {
        ValidatePeriod(period);

        Notes = AgendaEntryRules.Notes(notes);
        ServiceProvider = AgendaEntryRules.ServiceProvider(serviceProvider);
        Period = period;
        Type = type;
        Audit.MarkAsUpdated();
    }

    public bool OverlapsWith(TimeWindow window) => Period.OverlapsWith(window);

    public bool IsCritical() =>
        Type == VehicleUnavailabilityType.Repair || Type == VehicleUnavailabilityType.TechnicalInspection;

    public void UpdateNotes(string? notes)
    {
        Notes = AgendaEntryRules.Notes(notes);
        Audit.MarkAsUpdated();
    }

    private static void ValidateVehicleId(int vehicleId)
    {
        EntityValidator.ValidatePositive(vehicleId, nameof(vehicleId));
    }

    private static void ValidatePeriod(TimeWindow? period)
    {
        EntityValidator.ValidateNotNull(period, nameof(period));
    }
}