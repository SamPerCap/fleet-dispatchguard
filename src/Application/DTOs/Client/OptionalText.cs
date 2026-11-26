namespace DispatchGuard.Application.DTOs.Client;

/// <summary>
/// Forms send an empty input as "", but the validation attributes only accept null as "not provided", and a blank tax ID
/// stored as text would collide with any other one in its unique index.
/// </summary>
internal static class OptionalText
{
    public static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
