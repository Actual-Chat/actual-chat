using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.Search;

public static class OpenSearchExt
{
    public static SearchDescriptor<T> Log<T>(this SearchDescriptor<T> descriptor, IOpenSearchClient client, ILogger? log, string description = "") where T : class
    {
        if (log == null)
            return descriptor;

        var s = client.RequestResponseSerializer.SerializeToString(descriptor);
        log.LogDebug("{Message}: {Request}", description, s);
        return descriptor;
    }
}
