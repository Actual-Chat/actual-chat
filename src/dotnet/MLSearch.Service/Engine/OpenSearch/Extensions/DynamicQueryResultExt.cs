using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Extensions;

internal static class DynamicQueryResultExt
{
    public static IDictionary<string, object> FirstHit(this DynamicResponse result)
    {
        var hit = result.AssertSuccess()
            .Get<List<object>>("hits.hits").FirstOrDefault() as IDictionary<string, object>;
        return hit ?? throw new InvalidOperationException("Query result is empty.");
    }
}
