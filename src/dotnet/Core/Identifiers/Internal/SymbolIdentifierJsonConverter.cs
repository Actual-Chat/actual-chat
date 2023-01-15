namespace ActualChat.Internal;

public class SymbolIdentifierJsonConverter<T> : JsonConverter<T>
    where T : struct, ISymbolIdentifier<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => T.Parse(reader.GetString());

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}
