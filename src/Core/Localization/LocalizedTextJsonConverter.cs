using System.Text.Json;
using System.Text.Json.Serialization;

namespace DispatchGuard.Core.Localization;

/// <summary>In an HTTP answer a text is a string in the language of the request.</summary>
public sealed class LocalizedTextJsonConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { } text ? Texts.AsWritten(text) : null;

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.In(CurrentLanguage.Value));
}

/// <summary>Always in the same language, for what is stored already worded.</summary>
public sealed class WordedTextJsonConverter(Language language) : JsonConverter<LocalizedText>
{
    public override LocalizedText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() is { } text ? Texts.AsWritten(text) : null;

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.In(language));
}
