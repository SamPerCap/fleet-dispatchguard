using DispatchGuard.Core.ValueObjects;
using Core.Interfaces.ValueObjects;

namespace DispatchGuard.Core.Entities.Base;

/// <summary>
/// What the agenda entries of drivers and vehicles have in common (SPEC 0078).
/// </summary>
public static class AgendaEntryRules
{
    public const int MaxNotesLength = 500;
    public const int MaxServiceProviderLength = 200;

    public static TimeWindow Period(DateTime start, DateTime end)
    {
        if (start == default)
        {
            throw new InvalidRequestException(Texts.StartMissing());
        }

        if (end <= start)
        {
            throw new InvalidRequestException(Texts.EndAfterStart());
        }

        return new TimeWindow(start, end);
    }

    public static string? Notes(string? notes) => Text(notes, MaxNotesLength, Texts.NotesCannot());

    public static string? ServiceProvider(string? provider) => Text(provider, MaxServiceProviderLength, Texts.WorkshopCannot());

    /// <summary>What is over is history: rewriting it would change who was off that day and why a service was rejected.</summary>
    public static bool IsFinished(IUnavailability entry, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Period.End <= now;
    }

    private static string? Text(string? text, int maxLength, LocalizedText subject)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Length <= maxLength ? text : throw new InvalidRequestException(Texts.TooLong(subject, maxLength));
    }
}
