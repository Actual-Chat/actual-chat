using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace ActualChat.AspNetCore;

public static class HttpContextExt
{
    public static void DisableResponseCaching(this HttpContext context)
        => context.Response.OnStarting(() => {
            var headers = context.Response.Headers;
            headers[HeaderNames.CacheControl] = "no-store, no-cache, must-revalidate";
            headers[HeaderNames.Pragma] = "no-cache";
            headers[HeaderNames.Expires] = "0";
            return Task.CompletedTask;
        });

    public static IPAddress? GetRemoteIPAddress(this HttpContext context, bool useForwardedForHeaders = true)
    {
        if (useForwardedForHeaders) {
            var headers = context.Request.Headers;
            // If you are allowing CloudFlare headers, you must ensure you are restricting
            // your front-end servers to their IPs: https://www.cloudflare.com/ips/ ,
            // otherwise it can be spoofed.
            var forwardedForHeader = headers["CF-Connecting-IP"].FirstOrDefault()
                ?? headers["X-Forwarded-For"].FirstOrDefault();
            if (IPAddress.TryParse(forwardedForHeader, out var ipAddress))
                return ipAddress;
        }
        return context.Connection.RemoteIpAddress;
    }

    // Rounded up, so the client never retries earlier than the server meant
    public static void SetRetryAfter(this HttpResponse response, TimeSpan retryDelay)
        => response.Headers[HeaderNames.RetryAfter] = ((int)Math.Ceiling(retryDelay.TotalSeconds)).Format();
}
