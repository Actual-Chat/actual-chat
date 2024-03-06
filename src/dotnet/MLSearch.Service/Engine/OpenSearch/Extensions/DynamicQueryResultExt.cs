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

    private static DynamicResponse AssertSuccess(this DynamicResponse result)
        => result.Success ? result : throw new InvalidOperationException("Request has failed.",result.OriginalException);
}
