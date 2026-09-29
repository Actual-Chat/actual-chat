using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace ActualChat.AspNetCore;

public static class HttpContextExt
{
    private const string OriginalHostKey = "OriginalHost";

    public static void RememberOriginalHost(this HttpContext context)
        // UseBaseUrl normalizes Request.Host to the base host, so the host the client actually
        // dialed - an edge relay's, say - survives only if the middleware remembers it first.
        => context.Items[OriginalHostKey] = context.Request.Host.Host;

    public static string GetOriginalHost(this HttpContext context)
        => context.Items.TryGetValue(OriginalHostKey, out var host) && host is string originalHost
            ? originalHost
            : context.Request.Host.Host;

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
