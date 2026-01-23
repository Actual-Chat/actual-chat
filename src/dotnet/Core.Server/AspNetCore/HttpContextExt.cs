using System.Net;
using Microsoft.AspNetCore.Http;

namespace ActualChat.AspNetCore;

public static class HttpContextExt
{
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
}
