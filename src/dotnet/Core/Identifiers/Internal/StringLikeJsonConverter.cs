namespace ActualChat.Internal;

// Generic System.Text.Json converter for any IStringLike<T>: writes the .Value string,
// parses via T.Parse. Replaces the StringIdentifier/SymbolIdentifier pair of converters.
public class StringLikeJsonConverter<T> : JsonConverter<T>
    where T : IStringLike<T>
{
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value?.Value);

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return s.IsNullOrEmpty() ? default : T.Parse(s);
    }

    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.PropertyName)
            throw new JsonException($"Expected property name token, but got {reader.TokenType}.");
        return T.Parse(reader.GetString());
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value);
}
