using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Extensions;

internal static class DynamicQueryResultExt
{
    public static IDictionary<string, object> FirstHit(this DynamicResponse result)
        => result.FirstHitOrDefault() ?? throw new InvalidOperationException("Query result is malformed or empty.");

    public static IDictionary<string, object>? FirstHitOrDefault(this DynamicResponse result)
        => result.Get<List<object>>("hits.hits")?.FirstOrDefault() as IDictionary<string, object>;
}
