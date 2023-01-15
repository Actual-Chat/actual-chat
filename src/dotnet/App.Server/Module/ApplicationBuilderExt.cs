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
}
