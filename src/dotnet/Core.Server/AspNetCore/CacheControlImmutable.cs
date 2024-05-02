using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace ActualChat.AspNetCore;

public sealed class CacheControlImmutableAttribute : ResultFilterAttribute
{
    public int Duration { get; set; } = 3600;

    public bool IsPrivate { get; set; }

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;

        // Clear all headers
        headers.Remove(HeaderNames.CacheControl);
        headers.Remove(HeaderNames.Pragma);
        headers.Remove(HeaderNames.Vary);

        var location = IsPrivate
            ? "private"
            : "public";
        var cacheControlHeader = $"{location}, max-age={Duration}, immutable, stale-while-revalidate={Duration}";
        headers.TryAdd(HeaderNames.CacheControl, cacheControlHeader);
        base.OnResultExecuting(context);
    }
}
