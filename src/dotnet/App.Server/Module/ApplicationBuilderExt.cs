using System.Text.RegularExpressions;

namespace ActualChat.App.Server.Module;

/// <summary>
/// Extension methods for configuring middleware including COOP headers, base URL, and static file caching.
/// </summary>
public static partial class ApplicationBuilderExt
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

    public static IApplicationBuilder UseResponseHeaders(this IApplicationBuilder app)
        => app.Use((context, next) => {
            context.Response.OnStarting(static state => {
                var httpContext = (HttpContext)state;
                var response = httpContext.Response;
                var headers = response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                // The full policy is nonce-bearing & sizeable, so it's added to documents only
                if (response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true) {
                    var policy = httpContext.RequestServices.GetRequiredService<ContentSecurityPolicy>();
                    headers.ContentSecurityPolicy = policy.Get(ContentSecurityPolicy.GetNonce(httpContext));
                }
                return Task.CompletedTask;
            }, context);

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
            if (requestPath.StartsWith(EndpointsExt.BackendPathPrefix))
                return next();
            if (requestPath.StartsWith(EndpointsExt.HealthPathPrefix, StringComparison.OrdinalIgnoreCase))
                return next();
            if (requestPath.StartsWith(EndpointsExt.PrometheusPathPrefix, StringComparison.OrdinalIgnoreCase))
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

    private partial class CacheControlMiddleware(RequestDelegate next)
    {
        // Regex to detect fingerprinted files (10-char hash before extension)
        // Matches patterns like: file.abc1234xyz.js, dotnet.native.kx5e2qo6u9.wasm
        [GeneratedRegex(@"\.[a-z0-9]{10}\.(js|mjs|wasm|css|dll|webcil|onnx|ort)$", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture)]
        private static partial Regex FingerprintRegex();

        public Task InvokeAsync(HttpContext context)
        {
            var requestPath = context.Request.Path;
            var isDist = requestPath.StartsWithSegments("/dist", StringComparison.OrdinalIgnoreCase);
            var isContent = requestPath.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase);
            var isFramework = requestPath.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase);
            if (!isDist && !isContent && !isFramework)
                return next(context);

            var services = context.RequestServices;
            var requestPathValue = requestPath.Value ?? "";

            // Check if file is fingerprinted (has hash in filename)
            var isFingerprinted = FingerprintRegex().IsMatch(requestPathValue);

            // In DEBUG: only disable caching for non-fingerprinted files
            // Fingerprinted files are immutable by definition - safe to cache
            var mustDisableCaching =
#if DEBUG
                !isFingerprinted; // Allow caching fingerprinted files even in DEBUG
#else
                false;
#endif
            mustDisableCaching |= Constants.DebugMode.DisableStaticFileCaching
                && services.HostInfo().IsDevelopmentInstance
                && !isFingerprinted;

            if (mustDisableCaching) {
                context.Response.OnStarting(() => {
                    context.Response.Headers.Remove("Cache-Control");
                    context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
                    return Task.CompletedTask;
                });
                return next(context);
            }

            context.Response.OnStarting(() => {
                var currentCacheControl = (string?)context.Response.Headers.CacheControl;

                // For fingerprinted files, always use immutable caching (1 year)
                if (isFingerprinted) {
                    context.Response.Headers.Remove("Cache-Control");
                    context.Response.Headers.Append("Cache-Control", "public, max-age=31536000, immutable");
                    return Task.CompletedTask;
                }

                // For framework files without fingerprint (blazor.boot.json, blazor.web.js)
                // Allow no-cache from MapStaticAssets
                var hasNoCache = string.Equals(currentCacheControl, "no-cache", StringComparison.OrdinalIgnoreCase);
                if (hasNoCache && isFramework)
                    return Task.CompletedTask;

                var hasImmutable = currentCacheControl?.EndsWith("immutable", StringComparison.OrdinalIgnoreCase) == true;
                var fileExtension = Path.GetExtension(requestPathValue);
                var isVideo = string.Equals(fileExtension, ".mp4", StringComparison.OrdinalIgnoreCase) || string.Equals(fileExtension, ".webm", StringComparison.OrdinalIgnoreCase);
                var isMedia = isVideo
                    || string.Equals(fileExtension, ".png", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileExtension, ".svg", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileExtension, ".jpg", StringComparison.OrdinalIgnoreCase);

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
