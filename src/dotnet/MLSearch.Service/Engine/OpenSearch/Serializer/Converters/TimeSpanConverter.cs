namespace ActualChat.MLSearch.Engine.OpenSearch.Serializer.Converters;

internal sealed class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value.Ticks);

    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch {
            JsonTokenType.String => reader.GetString() is var token && TimeSpan.TryParse(token, out var timestamp)
                ? timestamp
                : throw new JsonException($"Unable to parse timestamp from its string representation: '{token}'."),
            JsonTokenType.Number => new TimeSpan(reader.GetInt64()),
            _ => throw new JsonException("Unexpected timestamp json token."),
        };
}

internal sealed class NullableTimeSpanConverter : JsonConverter<TimeSpan?>
{
    private readonly TimeSpanConverter _innerConverter = new();
    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value is null) {
            writer.WriteNullValue();
            return;
        }
        _innerConverter.Write(writer, value.Value, options);
    }

    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch {
            JsonTokenType.Null => default,
            _ => _innerConverter.Read(ref reader, typeToConvert, options)
        };
}
