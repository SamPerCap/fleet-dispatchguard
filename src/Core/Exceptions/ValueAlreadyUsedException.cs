namespace DispatchGuard.Core.Exceptions;

/// <summary>Another resource of the same kind has that value, also when both were saved at once (SPECs 0079 R3 and 0094 R4).</summary>
public sealed class ValueAlreadyUsedException : Exception, IHasLocalizedText
{
    private ValueAlreadyUsedException(string field, LocalizedText text, Exception? inner) : base(text.ToString(), inner) => (Field, Text) = (field, text);

    public LocalizedText Text { get; }

    /// <summary>As the API names it: the client shows the error next to it.</summary>
    public string Field { get; }

    public static ValueAlreadyUsedException IdentificationNumber(string number, Exception? inner = null) =>
        new(nameof(Entities.Driver.IdentificationNumber), Texts.RepeatedIdentification(number), inner);

    public static ValueAlreadyUsedException LicensePlate(string plate, Exception? inner = null) =>
        new(nameof(Entities.Vehicle.LicensePlate), Texts.RepeatedPlate(plate), inner);

    public static ValueAlreadyUsedException TaxId(string? taxId, Exception? inner = null) =>
        new(nameof(Entities.Client.TaxId), Texts.RepeatedTaxId(taxId), inner);

    public static ValueAlreadyUsedException BaseName(string name, Exception? inner = null) =>
        new(nameof(Entities.OperatingBase.Name), Texts.RepeatedBaseName(name), inner);
}
