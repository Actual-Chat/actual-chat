using Newtonsoft.Json;

namespace ActualChat.Internal;

public class StringIdentifierNewtonsoftJsonConverter<TId> : Newtonsoft.Json.JsonConverter<TId>
    where TId : StringIdentifier, IStringIdentifier<TId>
{
    public override void WriteJson(
        JsonWriter writer, TId? value,
        Newtonsoft.Json.JsonSerializer serializer)
        => writer.WriteValue(value?.Value);

    public override TId? ReadJson(
        JsonReader reader, Type objectType, TId? existingValue, bool hasExistingValue,
        Newtonsoft.Json.JsonSerializer serializer)
    {
        var s = (string?)reader.Value;
        return s.IsNullOrEmpty() ? null : TId.Parse(s);
    }
}
