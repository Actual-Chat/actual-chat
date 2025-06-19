namespace ActualChat.Internal;

public class StringIdentifierJsonConverter<TId> : JsonConverter<TId>
    where TId : StringIdentifier, IStringIdentifier<TId>
{
    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);

    public override TId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return s.IsNullOrEmpty() ? null : TId.Parse(s);
    }

    public override TId ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // This method handles reading the property name (dictionary key).
        if (reader.TokenType != JsonTokenType.PropertyName)
            throw new JsonException($"Expected property name token, but got {reader.TokenType}.");

        var s = reader.GetString();
        return TId.Parse(s!);
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.Value);
}
