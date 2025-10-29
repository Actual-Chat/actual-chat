namespace ActualChat.App.Server.Module;

public static class ApplicationBuilderExt
{
    public static IApplicationBuilder UseCoopHeaders(this IApplicationBuilder app)
        => app.Use((context, next) => {
            var path = context.Request.Path.Value ?? string.Empty;
            var localUrl = new LocalUrl(path);
            if (!localUrl.IsChat() && !localUrl.IsUser() && !localUrl.IsSettings() && !localUrl.IsHome())
                return next();

            context.Response.OnStarting(() => {
                var headers = context.Response.Headers;
                if (!headers.ContainsKey("Cross-Origin-Opener-Policy"))
                    headers.Append("Cross-Origin-Opener-Policy", "same-origin");
                if (!headers.ContainsKey("Cross-Origin-Embedder-Policy"))
                    headers.Append("Cross-Origin-Embedder-Policy", "require-corp");
                return Task.CompletedTask;
            });

            return next();
        });

    public static IApplicationBuilder UseBaseUrl(this IApplicationBuilder app, string baseUrl)
    {
        var baseUri = baseUrl.ToUri();
        var scheme = baseUri.Scheme;
        var host = baseUri.Host;
        var port = baseUri.Port;
        port = scheme switch {
            "https" when port == 443 => -1,
            "http" when port == 80 => -1,
            _ => port,
        };
        return app.Use((context, next) => {
            var requestPath = context.Request.Path.Value ?? "";
            if (requestPath.OrdinalStartsWith(EndpointsExt.BackendPathPrefix))
                return next();
            if (requestPath.OrdinalIgnoreCaseStartsWith(EndpointsExt.HealthPathPrefix))
                return next();
            if (requestPath.OrdinalIgnoreCaseStartsWith(EndpointsExt.PrometheusPathPrefix))
                return next();

            var hostInfo = context.RequestServices.HostInfo();
            if (hostInfo.GetHosts().Contains(context.Request.Host.Host))
                return next();

            context.Request.Scheme = scheme;
            context.Request.Host = port > 0
                ? new HostString(host, port)
                : new HostString(host);

            return next();
        });
    }

    public static IApplicationBuilder UseStaticDistCacheHeaders(this IApplicationBuilder builder)
    {
        builder.UseMiddleware<CacheControlMiddleware>();
        return builder;
    }

    private class CacheControlMiddleware(RequestDelegate next)
    {
        public Task InvokeAsync(HttpContext context)
        {
            var requestPath = context.Request.Path;
            var isDist = requestPath.StartsWithSegments("/dist", StringComparison.OrdinalIgnoreCase);
            var isContent = requestPath.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase);
            var isFramework = requestPath.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase);
            if (!isDist && !isContent && !isFramework)
                return next(context);

            var mustDisable = false;
#if DEBUG
            mustDisable = true;
#endif
            if (!mustDisable && Constants.DebugMode.DisableStaticFileCaching) {
                var hostInfo = context.RequestServices.HostInfo();
                mustDisable = hostInfo.IsDevelopmentInstance;
            }
            if (mustDisable)
                return next(context);

            context.Response.OnStarting(() => {
                var requestPathValue = requestPath.Value ?? "";
                var currentCacheControl = (string?)context.Response.Headers.CacheControl;
                var hasNoCache = string.Equals(currentCacheControl, "no-cache", StringComparison.OrdinalIgnoreCase);
                if (hasNoCache && isFramework)
                    return Task.CompletedTask;

                var hasImmutable = currentCacheControl?.EndsWith("immutable", StringComparison.OrdinalIgnoreCase) == true;
                var fileExtension = Path.GetExtension(requestPathValue);
                var isVideo = OrdinalIgnoreCaseEquals(fileExtension, ".mp4") || OrdinalIgnoreCaseEquals(fileExtension, ".webm");
                var isMedia = isVideo
                    || OrdinalIgnoreCaseEquals(fileExtension, ".png")
                    || OrdinalIgnoreCaseEquals(fileExtension, ".svg")
                    || OrdinalIgnoreCaseEquals(fileExtension, ".jpg");

                var cacheControlHeader = hasImmutable
                    ? "public, max-age=5184000, immutable, stale-while-revalidate=86400, s-maxage=2592000" // immutable, 60 days + up to 1 for revalidation
                    : isMedia ? "public, max-age=518400, stale-while-revalidate=86400, s-maxage=2592000" // 6 days + up to 1 for revalidation
                        : "public, max-age=3600, max-stale=86400, stale-while-revalidate=86400, s-maxage=86400, must-revalidate"; // 1d + up to 1 day for revalidation
                context.Response.Headers.Remove("Cache-Control");
                context.Response.Headers.Append("Cache-Control", cacheControlHeader);

                if (isVideo && context.Response.StatusCode == StatusCodes.Status416RangeNotSatisfiable)
                    context.Response.StatusCode = StatusCodes.Status206PartialContent; // Temp fix for range request until it is fixed in ASP.NET Core 9+
                return Task.CompletedTask;
            });
            return next(context);
        }
    }
}
