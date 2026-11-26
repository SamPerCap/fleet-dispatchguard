using DispatchGuard.Core.Entities.Base;
using DispatchGuard.Core.ValueObjects;

namespace DispatchGuard.Core.Entities;

public class Vehicle : EntityWithUnavailabilities<VehicleUnavailability>
{
    public const int MaxLicensePlateLength = 20;
    public const int MaxBrandOrModelLength = 100;

    public string LicensePlate { get; private set; } = string.Empty;
    public string Brand { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string FullDescription => $"{Brand} {Model}";
    public int SeatingCapacity { get; private set; }
    public int OdometerKm { get; private set; }
    public DateTime? RegistrationDate { get; private set; }

    public bool IsExternal { get; private set; }
    public bool AllowsAutoAssignment { get; private set; }
    public DateTime? DecommissionedDate { get; private set; }

    private Vehicle() { }

    public int BaseId { get; private set; } = OperatingBase.DefaultId;

    public void MoveToBase(int baseId)
    {
        EntityValidator.ValidatePositive(baseId, nameof(baseId));
        BaseId = baseId;
        MarkAsUpdated();
    }

    public Vehicle(
        string licensePlate,
        string brand,
        string model,
        int seatingCapacity,
        bool isExternal = false,
        bool allowsAutoAssignment = true)
    {
        SetDetails(licensePlate, brand, model, seatingCapacity, isExternal, allowsAutoAssignment);
    }

    /// <summary>All or nothing. The last two only decide whether the vehicle is proposed, never its eligibility.</summary>
    public void ChangeDetails(
        string licensePlate, string brand, string model, int seatingCapacity, bool isExternal, bool allowsAutoAssignment)
    {
        SetDetails(licensePlate, brand, model, seatingCapacity, isExternal, allowsAutoAssignment);
        MarkAsUpdated();
    }

    private void SetDetails(
        string licensePlate, string brand, string model, int seatingCapacity, bool isExternal, bool allowsAutoAssignment)
    {
        var validPlate = ResourceDetailRules.Text(licensePlate, MaxLicensePlateLength, Texts.PlateMissing(), Texts.PlateCannot());
        var validBrand = ResourceDetailRules.Text(brand, MaxBrandOrModelLength, Texts.BrandMissing(), Texts.BrandCannot());
        var validModel = ResourceDetailRules.Text(model, MaxBrandOrModelLength, Texts.ModelMissing(), Texts.ModelCannot());
        var validSeats = ResourceDetailRules.Seats(seatingCapacity);

        LicensePlate = validPlate.ToUpperInvariant();
        Brand = validBrand;
        Model = validModel;
        SeatingCapacity = validSeats;
        IsExternal = isExternal;
        AllowsAutoAssignment = allowsAutoAssignment;
    }

    public void SetRegistrationDate(DateTime date)
    {
        ValidateRegistrationDate(date);
        RegistrationDate = date;
        MarkAsUpdated();
    }

    public void UpdateOdometer(int newOdometerKm)
    {
        if (newOdometerKm < OdometerKm)
        {
            throw new ArgumentException("New odometer reading cannot be less than current", nameof(newOdometerKm));
        }

        OdometerKm = newOdometerKm;
        MarkAsUpdated();
    }

    public void Decommission(DateTime decommissionedDate)
    {
        ValidateDecommissionDate(decommissionedDate);
        DecommissionedDate = decommissionedDate;
        Deactivate();
    }

    public override void Reactivate()
    {
        DecommissionedDate = null;
        base.Reactivate();
    }

    public void EnableAutoAssignment()
    {
        AllowsAutoAssignment = true;
        MarkAsUpdated();
    }

    public void DisableAutoAssignment()
    {
        AllowsAutoAssignment = false;
        MarkAsUpdated();
    }

    public bool HasCapacityFor(int requiredSeats) => SeatingCapacity >= requiredSeats;

    public bool CanPerformTrip(TimeWindow tripWindow, int requiredSeats)
    {
        return IsActive
            && HasCapacityFor(requiredSeats)
            && IsAvailableDuring(tripWindow);
    }

    public bool IsEligibleForAutoAssignment() => IsActive && AllowsAutoAssignment && !IsExternal;

    public bool NeedsMaintenance(int maintenanceIntervalKm = 20000)
    {
        return OdometerKm % maintenanceIntervalKm > (maintenanceIntervalKm * 0.9);
    }

    private static void ValidateRegistrationDate(DateTime date)
    {
        EntityValidator.ValidateNotFuture(date, nameof(date));
    }

    private static void ValidateDecommissionDate(DateTime decommissionedDate)
    {
        EntityValidator.ValidateNotFuture(decommissionedDate, nameof(decommissionedDate));
    }
}