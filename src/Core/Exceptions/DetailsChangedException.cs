namespace DispatchGuard.Core.Exceptions;

/// <summary>Somebody else saved the data of the driver or vehicle after the caller read them (SPEC 0090 R2).</summary>
public sealed class DetailsChangedException() : Exception(Texts.DetailsChanged().ToString()), IHasLocalizedText
{
    public LocalizedText Text => Texts.DetailsChanged();
}
