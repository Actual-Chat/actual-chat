namespace ActualChat.App.Server.Module;

public static class ApplicationBuilderExt
{
    public static IApplicationBuilder UseBaseUri(this IApplicationBuilder app, Uri baseUri)
    {
        var scheme = baseUri.Scheme;
        var host = baseUri.Host;
        var port = baseUri.Port;
        port = scheme switch {
            "https" when port == 443 => -1,
            "http" when port == 80 => -1,
            _ => port,
        };
        return app.Use((context, next) => {
            context.Request.Scheme = scheme;
            context.Request.Host = port > 0 ? new HostString(host, port) : new HostString(host);
            return next();
        });
    }
}
