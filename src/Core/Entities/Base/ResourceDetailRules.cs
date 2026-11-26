namespace DispatchGuard.Core.Entities.Base;

/// <summary>
/// Limits shared by the data of drivers and vehicles (SPEC 0079).
/// </summary>
public static class ResourceDetailRules
{
    /// <summary>The trimmed text. <paramref name="subject"/> begins the message, as in "The name cannot".</summary>
    public static string Text(string? text, int maxLength, LocalizedText missing, LocalizedText subject)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new InvalidRequestException(missing);
        }

        return trimmed.Length <= maxLength ? trimmed : throw new InvalidRequestException(Texts.TooLong(subject, maxLength));
    }

    public static int Seats(int seats) => seats > 0 ? seats : throw new InvalidRequestException(Texts.SeatsAtLeastOne());
}
