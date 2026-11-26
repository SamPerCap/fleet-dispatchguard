using DispatchGuard.Core.Localization;

namespace DispatchGuard.Core.Exceptions;

/// <summary>An exception whose text reaches the dispatcher, in either language; its message is for the log.</summary>
public interface IHasLocalizedText
{
    LocalizedText Text { get; }
}

/// <summary>Data of the caller that breaks an invariant.</summary>
public sealed class InvalidRequestException(LocalizedText text) : ArgumentException(text.ToString()), IHasLocalizedText
{
    public LocalizedText Text { get; } = text;
}

/// <summary>An id that does not exist.</summary>
public sealed class ResourceNotFoundException(LocalizedText text) : KeyNotFoundException(text.ToString()), IHasLocalizedText
{
    public LocalizedText Text { get; } = text;
}
