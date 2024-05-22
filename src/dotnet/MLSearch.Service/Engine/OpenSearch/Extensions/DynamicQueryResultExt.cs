using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Extensions;

internal static class DynamicQueryResultExt
{
    public static IDictionary<string, object> FirstHit(this DynamicResponse result)
    {
        var hits = result.Get<List<object>>("hits.hits");
        var hit = hits?.FirstOrDefault() as IDictionary<string, object>;
        return hit ?? throw new InvalidOperationException("Query result is malformed or empty.");
    }
}
