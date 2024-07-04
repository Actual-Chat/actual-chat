using OpenSearch.Net;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Serializer.Converters;

internal sealed class JoinFieldConverter(IOpenSearchSerializer builtInSerializer, IMemoryStreamFactory memoryStreamFactory)
    : OscTypeConverter<JoinField>(builtInSerializer, memoryStreamFactory)
{
    public override JoinField? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        return jsonDoc.RootElement.ValueKind == JsonValueKind.String
            ? JoinField.Root(jsonDoc.Deserialize<string>())
            : ReadWithBuiltInSerializer(typeToConvert, jsonDoc);
    }
}
