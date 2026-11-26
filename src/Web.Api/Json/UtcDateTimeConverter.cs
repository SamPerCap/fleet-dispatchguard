using System.Text.Json;
using System.Text.Json.Serialization;

namespace DispatchGuard.API.Json;

/// <summary>
/// The only place where a date of a request body becomes the UTC instant the domain works with (SPEC 0067): one with an
/// explicit offset is converted; one with Z or with no zone is left as it comes (no zone already meant UTC). Without this,
/// System.Text.Json turns an offset into the server's local time. Writing does not change.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetDateTime();
        // From the text, not from the local value: the local hour that the autumn change repeats is ambiguous.
        return value.Kind == DateTimeKind.Local ? reader.GetDateTimeOffset().UtcDateTime : value;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
