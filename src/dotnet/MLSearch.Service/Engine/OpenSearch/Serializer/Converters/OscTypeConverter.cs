using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Serializer.Converters;

internal class OscTypeConverter<T>(IOpenSearchSerializer builtInSerializer, IMemoryStreamFactory memoryStreamFactory)
    : JsonConverter<T>
{
    private static readonly Type targetType = typeof(T);

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var formatting = options.WriteIndented
            ? SerializationFormatting.Indented
            : SerializationFormatting.None;

        using var ms = memoryStreamFactory.Create();
        builtInSerializer.Serialize(value, ms, formatting);
        ms.Position = 0;

        var docOptions = new JsonDocumentOptions {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Allow,
        };
        using var jsonDoc = JsonDocument.Parse(ms, docOptions);
        jsonDoc.WriteTo(writer);
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        return ReadWithBuiltInSerializer(typeToConvert, jsonDoc);
    }

    protected T? ReadWithBuiltInSerializer(Type typeToConvert, JsonDocument jsonDoc)
    {
        using var ms = jsonDoc.ToStream(memoryStreamFactory);
        return (T?)builtInSerializer.Deserialize(typeToConvert, ms);
    }

    public override bool CanConvert(Type objectType) => targetType == objectType
        || (targetType.IsInterface && targetType.IsAssignableFrom(objectType));
}
