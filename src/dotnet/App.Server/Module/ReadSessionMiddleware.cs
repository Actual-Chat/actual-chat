using ActualChat.Hosting;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.App.Server.Module;

public sealed class ReadSessionMiddleware : IMiddleware, IHasServices
{
    public record Options
    {
        public static Options Default { get; set; } = new();

        public Func<HttpContext, bool> RequestFilter { get; init; } = _ => true;
        public Func<HttpContext, Symbol> TenantIdExtractor { get; init; } = TenantIdExtractors.None;
    }

    public Options Settings { get; }
    public SessionCookieOptions CookieSettings { get; }
    public IServiceProvider Services { get; }
    public ILogger Log { get; }

    public IAuth? Auth { get; }
    public ISessionResolver SessionResolver { get; }
    public ClientInfoProvider ClientInfoProvider { get; }

    public ReadSessionMiddleware(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Log = services.LogFor(GetType());

        Auth = services.GetService<IAuth>();
        SessionResolver = services.GetRequiredService<ISessionResolver>();
        CookieSettings = services.GetRequiredService<SessionCookieOptions>();
        ClientInfoProvider = services.GetRequiredService<ClientInfoProvider>();
    }

    public async Task InvokeAsync(HttpContext httpContext, RequestDelegate next)
    {
        if (Settings.RequestFilter.Invoke(httpContext)) {
            var session = GetSession(httpContext);
            if (session != null)
                SessionResolver.Session = session;
        }
        await next(httpContext).ConfigureAwait(false);
    }

    public Session? GetSession(HttpContext httpContext)
    {
        var cookies = httpContext.Request.Cookies;
        var cookieName = CookieSettings.Cookie.Name ?? "";
        cookies.TryGetValue(cookieName, out var sessionId);
        var originalSession = sessionId.IsNullOrEmpty()
            ? null
            : new Session(sessionId);
        var tenantId = Settings.TenantIdExtractor.Invoke(httpContext);
        var ipAddress = httpContext.GetRemoteIPAddress()?.ToString() ?? "";
        var userAgent = httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgentValues)
            ? userAgentValues.FirstOrDefault() ?? ""
            : "";
        ClientInfoProvider.SetClientInfo(new ClientInfo(tenantId, userAgent, ipAddress));
        return originalSession?.WithTenantId(tenantId);
    }
}
