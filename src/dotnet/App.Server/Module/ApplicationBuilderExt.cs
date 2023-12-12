using ActualChat.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace ActualChat.App.Server.Module;

public static class ApplicationBuilderExt
{
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
            if (context.Request.Path.Value?.StartsWith(EndpointsExt.HealthPathPrefix, StringComparison.OrdinalIgnoreCase) == true)
                return next();
            if (context.Request.Path.Value?.StartsWith(EndpointsExt.PrometheusPathPrefix, StringComparison.OrdinalIgnoreCase) == true)
                return next();

            context.Request.Scheme = scheme;
            context.Request.Host = port > 0
                ? new HostString(host, port)
                : new HostString(host);

            return next();
        });
    }

    public static IApplicationBuilder UseDistFiles(this IApplicationBuilder builder)
    {
        var webHostEnvironment = builder.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
        var options = CreateStaticFilesOptions(webHostEnvironment.WebRootFileProvider);

        builder.MapWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/dist", StringComparison.OrdinalIgnoreCase)
                || ctx.Request.Path.StartsWithSegments("/_content", StringComparison.OrdinalIgnoreCase),
            subBuilder => {
                subBuilder.UseMiddleware<ContentEncodingNegotiator>();
                subBuilder.UseStaticFiles(options);
            });

        return builder;
    }

    private static StaticFileOptions CreateStaticFilesOptions(IFileProvider webRootFileProvider)
    {
        var contentTypeProvider = ContentTypeProvider.Instance;
        return  new StaticFileOptions {
            FileProvider = webRootFileProvider,
            ContentTypeProvider = contentTypeProvider,
            // Static files middleware will try to use application/x-gzip as the content
            // type when serving a file with a gz extension. We need to correct that before
            // sending the file.
            OnPrepareResponse = ctx => {
                var mustDisable = false;
                if (Constants.DebugMode.DisableStaticFileCaching) {
                    var hostInfo = ctx.Context.RequestServices.HostInfo();
                    mustDisable = hostInfo.IsDevelopmentInstance;
                }

                var request = ctx.Context.Request;
                var hasVersion = request.Query.TryGetValue("v", out var version) && version.Count > 0;
                var requestPath = request.Path.Value ?? "";
                var fileExtension = Path.GetExtension(requestPath);

                var isJavaScriptOrWasm =
                    OrdinalIgnoreCaseEquals(fileExtension, ".js")
                    || OrdinalIgnoreCaseEquals(fileExtension, ".wasm");
                var mustNotCache = mustDisable || (isJavaScriptOrWasm && !hasVersion);
                if (mustNotCache) {
                    ctx.Context.Response.Headers.Append(HeaderNames.CacheControl, "no-cache");
                    return;
                }

                var cacheControlHeader = hasVersion
                    ? "public, max-age=518400, stale-while-revalidate=86400" // 6 days + up to 1 for revalidation
                    : "public, max-age=3600, stale-while-revalidate=86400"; // 1h + up to 1 day for revalidation
                ctx.Context.Response.Headers.Append("Cache-Control", cacheControlHeader);

                var isCompressed =
                    OrdinalIgnoreCaseEquals(fileExtension, ".gz")
                    || OrdinalIgnoreCaseEquals(fileExtension, ".br");
                if (!isCompressed)
                    return;

                // Here we calculate the uncompressed content type by removing the extension and determining
                // it based on the remainder of the file name.
                // When we revisit this, we should consider calculating the original content type and storing it
                // in the request along with the original target path so that we don't have to calculate it here.
                var preCompressionPath = Path.GetFileNameWithoutExtension(requestPath);
                if (contentTypeProvider.TryGetContentType(preCompressionPath, out var originalContentType))
                    ctx.Context.Response.ContentType = originalContentType;
            },
        };
    }

    internal sealed class ContentEncodingNegotiator
    {
        // List of encodings by preference order with their associated extension so that we can easily handle "*".
        private static readonly StringSegment[] _preferredEncodings = { "br", "gzip" };

        private static readonly Dictionary<StringSegment, string> _encodingExtensionMap = new Dictionary<StringSegment, string>(StringSegmentComparer.OrdinalIgnoreCase)
        {
            ["br"] = ".br",
            ["gzip"] = ".gz",
        };

        private readonly RequestDelegate _next;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ContentEncodingNegotiator(RequestDelegate next, IWebHostEnvironment webHostEnvironment)
        {
            _next = next;
            _webHostEnvironment = webHostEnvironment;
        }

        public Task InvokeAsync(HttpContext context)
        {
            NegotiateEncoding(context);
            return _next(context);
        }

        private void NegotiateEncoding(HttpContext context)
        {
            var accept = context.Request.Headers.AcceptEncoding;

            if (StringValues.IsNullOrEmpty(accept))
                return;

            if (!StringWithQualityHeaderValue.TryParseList(accept, out var encodings) || encodings.Count == 0)
                return;

            var selectedEncoding = StringSegment.Empty;
            var selectedEncodingQuality = .0;

            foreach (var encoding in encodings)
            {
                var encodingName = encoding.Value;
                var quality = encoding.Quality.GetValueOrDefault(1);

                if (quality >= double.Epsilon && quality >= selectedEncodingQuality)
                {
                    if (Math.Abs(quality - selectedEncodingQuality) < 0.001)
                        selectedEncoding = PickPreferredEncoding(context, selectedEncoding, encoding);
                    else if (_encodingExtensionMap.TryGetValue(encodingName, out var encodingExtension) && ResourceExists(context, encodingExtension))
                    {
                        selectedEncoding = encodingName;
                        selectedEncodingQuality = quality;
                    }

                    if (StringSegment.Equals("*", encodingName, StringComparison.Ordinal))
                    {
                        // If we *, pick the first preferred encoding for which a resource exists.
                        selectedEncoding = PickPreferredEncoding(context, default, encoding);
                        selectedEncodingQuality = quality;
                    }

                    if (!StringSegment.Equals("identity", encodingName, StringComparison.OrdinalIgnoreCase)) continue;

                    selectedEncoding = StringSegment.Empty;
                    selectedEncodingQuality = quality;
                }
            }

            if (!_encodingExtensionMap.TryGetValue(selectedEncoding, out var extension))
                return;

            context.Request.Path += extension;
            context.Response.Headers.ContentEncoding = selectedEncoding.Value;
            context.Response.Headers.Append(HeaderNames.Vary, HeaderNames.ContentEncoding);

            return;

            StringSegment PickPreferredEncoding(
                HttpContext context1,
                StringSegment selectedEncoding1,
                StringWithQualityHeaderValue encoding)
            {
                foreach (var preferredEncoding in _preferredEncodings) {
                    if (preferredEncoding == selectedEncoding1)
                        return selectedEncoding1;

                    if ((preferredEncoding == encoding.Value || encoding.Value == "*")
                        && ResourceExists(context1, _encodingExtensionMap[preferredEncoding]))
                        return preferredEncoding;
                }

                return StringSegment.Empty;
            }
        }

        private bool ResourceExists(HttpContext context, string extension) =>
            _webHostEnvironment.WebRootFileProvider.GetFileInfo(context.Request.Path + extension).Exists;
    }

}
