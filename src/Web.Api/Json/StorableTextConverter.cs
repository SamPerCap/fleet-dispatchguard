using System.Text.Json;
using System.Text.Json.Serialization;

namespace DispatchGuard.API.Json;

/// <summary>
/// PostgreSQL does not store the null character: a text of a request body with one is a wrong request, not a failure when saving it
/// (SPEC 0092).
/// </summary>
public sealed class StorableTextConverter : JsonConverter<string>
{
    private const char NullCharacter = '\0';

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return text is not null && text.Contains(NullCharacter)
            ? throw new JsonException("A text cannot have the null character")
            : text;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
