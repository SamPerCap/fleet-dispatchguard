using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DispatchGuard.Core.Localization;

/// <summary>
/// A text stored as its code: <c>{"k":"NotEnoughSeats","a":[20,50]}</c>. Renaming a method of <see cref="Texts"/>, or changing its
/// parameters, makes the stored texts that used it unreadable: whoever stores them keeps the wording as well.
/// </summary>
public sealed class CodedTextJsonConverter : JsonConverter<LocalizedText>
{
    private const string KeyName = "k";
    private const string ArgumentsName = "a";

    public override LocalizedText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (JsonNode.Parse(ref reader) is not JsonObject stored || stored[KeyName] is not JsonValue key)
        {
            throw new JsonException("A stored text has a key");
        }

        var method = TextCatalogue.Find(key.GetValue<string>()) ?? throw new JsonException($"Unknown text {key}");
        var parameters = method.GetParameters();
        var arguments = stored[ArgumentsName] as JsonArray ?? [];
        if (arguments.Count != parameters.Length)
        {
            throw new JsonException($"The text {key} has {parameters.Length} parameters");
        }

        var values = parameters.Select((parameter, i) => arguments[i].Deserialize(parameter.ParameterType, options)).ToArray();
        return (LocalizedText)method.Invoke(null, values)!;
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString(KeyName, value.Key);
        if (value.Arguments.Count > 0)
        {
            writer.WriteStartArray(ArgumentsName);
            foreach (var argument in value.Arguments)
            {
                JsonSerializer.Serialize(writer, argument, argument?.GetType() ?? typeof(object), options);
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }
}

/// <summary>The methods of <see cref="Texts"/> by name.</summary>
public static class TextCatalogue
{
    private static readonly Dictionary<string, MethodInfo> Methods = typeof(Texts)
        .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(m => m.ReturnType == typeof(LocalizedText))
        .ToDictionary(m => m.Name, StringComparer.Ordinal);

    public static IReadOnlyCollection<MethodInfo> All => Methods.Values;

    public static MethodInfo? Find(string key) => Methods.GetValueOrDefault(key);
}
