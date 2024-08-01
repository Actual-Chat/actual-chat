using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Serializer;

internal static class JsonReaderExtensions
{
    public static MemoryStream ToStream(this JsonDocument jsonDocument, IMemoryStreamFactory memoryStreamFactory)
    {
        var ms = memoryStreamFactory.Create();
        using var writer = new Utf8JsonWriter(ms);
        jsonDocument.WriteTo(writer);
        writer.Flush();
        ms.Position = 0;
        return ms;
    }
}
