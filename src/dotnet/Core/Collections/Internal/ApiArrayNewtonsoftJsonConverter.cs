using Newtonsoft.Json;
using JsonConverter = Newtonsoft.Json.JsonConverter;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace ActualChat.Collections.Internal;

#pragma warning disable IL2026

public class ApiArrayNewtonsoftJsonConverter : JsonConverter
{
    private static readonly ConcurrentDictionary<Type, JsonConverter?> ConverterCache = new();

    public override bool CanConvert(Type typeToConvert)
        => GetConverter(typeToConvert) != null;

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        => GetConverter(objectType)!.ReadJson(reader, objectType, existingValue, serializer);

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        => GetConverter(value!.GetType())!.WriteJson(writer, value, serializer);

    private JsonConverter? GetConverter(Type type)
        => ConverterCache.GetOrAdd(type, static t => {
            var canConvert = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ApiArray<>);
            if (!canConvert)
                return null;

            var tArg = t.GetGenericArguments()[0];
            var tConverter = typeof(Converter<>).MakeGenericType(tArg);
            return (JsonConverter)tConverter.CreateInstance();
        });

    // Nested type

    public sealed class Converter<T> : Newtonsoft.Json.JsonConverter<ApiArray<T>>
    {
        public override ApiArray<T> ReadJson(
            JsonReader reader,
            Type objectType,
            ApiArray<T> existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            var items = serializer.Deserialize<T[]>(reader);
            return new(items!);
        }

        public override void WriteJson(JsonWriter writer, ApiArray<T> value, JsonSerializer serializer)
            => serializer.Serialize(writer, value.Items);
    }
}
