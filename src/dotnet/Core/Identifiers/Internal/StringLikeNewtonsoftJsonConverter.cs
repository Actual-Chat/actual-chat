using Newtonsoft.Json;

namespace ActualChat.Internal;

public class StringLikeNewtonsoftJsonConverter<T> : Newtonsoft.Json.JsonConverter<T>
    where T : IStringLike<T>
{
    public override void WriteJson(JsonWriter writer, T? value, Newtonsoft.Json.JsonSerializer serializer)
        => writer.WriteValue(value?.Value);

    public override T? ReadJson(JsonReader reader, Type objectType, T? existingValue, bool hasExistingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        var s = (string?)reader.Value;
        return s.IsNullOrEmpty() ? default : T.Parse(s);
    }
}
