using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.ResponseCaching;
using Microsoft.Net.Http.Headers;

namespace ActualChat.AspNetCore;

public sealed class CacheControlImmutableAttribute : ResultFilterAttribute
{
    public int Duration { get; set; } = 3600;

    public bool IsPrivate { get; set; }

    public string[]? VaryByQueryKeys { get; set; }

    public string? Vary { get; set; }

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;

        // Clear all headers
        headers.Remove(HeaderNames.CacheControl);
        headers.Remove(HeaderNames.Pragma);
        headers.Remove(HeaderNames.Vary);

        if (VaryByQueryKeys is { Length: > 0 }) {
            var feature = context.HttpContext.Features.Get<IResponseCachingFeature>();
            if (feature != null)
                feature.VaryByQueryKeys = VaryByQueryKeys;
        }

        if (!Vary.IsNullOrEmpty())
            headers[HeaderNames.Vary] = Vary;

        var location = IsPrivate
            ? "private"
            : "public";
        var cacheControlHeader = $"{location}, max-age={Duration}, immutable, stale-while-revalidate={Duration}";
        headers.TryAdd(HeaderNames.CacheControl, cacheControlHeader);
        base.OnResultExecuting(context);
    }
}
