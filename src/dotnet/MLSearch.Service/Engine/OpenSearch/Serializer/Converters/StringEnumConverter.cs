
using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Serializer.Converters;

internal sealed class StringEnumConverter : JsonConverterFactory
{
    private readonly JsonStringEnumConverter _innerConverter = new JsonStringEnumConverter();

    public override bool CanConvert(Type typeToConvert)
        => _innerConverter.CanConvert(typeToConvert) && typeToConvert.GetCustomAttribute<StringEnumAttribute>() != null;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        => _innerConverter.CreateConverter(typeToConvert, options);
}
