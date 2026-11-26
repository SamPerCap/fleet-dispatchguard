namespace DispatchGuard.Core.Exceptions;

/// <summary>
/// Another operation holds the same driver, vehicle or service and did not finish in time. Not a rejection: nothing was saved
/// and no rule was evaluated, so the caller may simply try again (SPEC 0065 R5).
/// </summary>
public sealed class ResourceBusyException(string resource, Exception? inner = null)
    : Exception(Texts.Busy().ToString(), inner), IHasLocalizedText
{
    public LocalizedText Text => Texts.Busy();

    /// <summary>For the log.</summary>
    public string Resource { get; } = resource;
}
