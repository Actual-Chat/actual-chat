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
}
