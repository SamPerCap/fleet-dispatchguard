using DispatchGuard.Core.Entities.Base;

namespace DispatchGuard.Core.Entities;

public class Client : Entity
{
    private const int MinTaxIdLength = 5;
    public const int MaxNameLength = 200;

    public string Name { get; private set; } = string.Empty;
    public string? TaxId { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public bool IsExternal { get; private set; }

    private readonly List<Trip> _trips = [];
    public IReadOnlyCollection<Trip> Trips => _trips.AsReadOnly();

    private Client() { }

    public Client(
        string name,
        bool isExternal = false,
        string? taxId = null,
        string? contactEmail = null,
        string? contactPhone = null)
    {
        ValidateName(name);
        ValidateTaxId(taxId);
        EntityValidator.ValidateEmail(contactEmail);
        EntityValidator.ValidatePhoneNumber(contactPhone);

        Name = name.Trim();
        IsExternal = isExternal;
        TaxId = taxId?.Trim();
        ContactEmail = contactEmail?.Trim();
        ContactPhone = contactPhone?.Trim();
    }

    public void Rename(string name)
    {
        ValidateName(name);

        Name = name.Trim();
        MarkAsUpdated();
    }

    public void UpdateContactInfo(string? email, string? phone)
    {
        EntityValidator.ValidateEmail(email);
        EntityValidator.ValidatePhoneNumber(phone);

        ContactEmail = email?.Trim();
        ContactPhone = phone?.Trim();
        MarkAsUpdated();
    }

    public void UpdateTaxId(string taxId)
    {
        ValidateTaxId(taxId);
        TaxId = taxId.Trim();
        MarkAsUpdated();
    }

    public IEnumerable<Trip> GetActiveTrips(DateTime startDate, DateTime endDate)
    {
        return _trips.Where(t =>
            t.IsActive
            && t.ScheduledWindow.Start >= startDate
            && t.ScheduledWindow.End <= endDate);
    }

    private static void ValidateName(string? name)
    {
        EntityValidator.ValidateRequiredString(name, nameof(name));

        if (name!.Trim().Length > MaxNameLength)
        {
            throw new InvalidRequestException(Texts.TooLong(Texts.NameCannot(), MaxNameLength));
        }
    }

    private static void ValidateTaxId(string? taxId)
    {
        if (string.IsNullOrWhiteSpace(taxId))
        {
            return;
        }

        if (taxId.Length < MinTaxIdLength)
        {
            throw new InvalidRequestException(Texts.TaxIdTooShort(MinTaxIdLength));
        }
    }
}