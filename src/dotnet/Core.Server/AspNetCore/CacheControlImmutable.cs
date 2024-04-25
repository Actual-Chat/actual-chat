using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;

namespace ActualChat.AspNetCore;

public class CacheControlImmutableAttribute : ResultFilterAttribute
{
    public int Duration { get; set; } = 3600;

    public bool IsPrivate { get; set; }

    public override void OnResultExecuting(ResultExecutingContext context)
    {
        var headers = context.HttpContext.Response.Headers;

        // Clear all headers
        headers.Remove(HeaderNames.Vary);
        headers.Remove(HeaderNames.CacheControl);
        headers.Remove(HeaderNames.Pragma);

        var location = IsPrivate
            ? "private"
            : "public";
        headers.Add(HeaderNames.CacheControl, $"{location}, max-age={Duration}, immutable, stale-while-revalidate={Duration}");
        base.OnResultExecuting(context);
    }
}
