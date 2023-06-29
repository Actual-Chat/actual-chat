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

    public ReadSessionMiddleware(Options settings, IServiceProvider services)
    {
        Settings = settings;
        Services = services;
        Log = services.LogFor(GetType());

        Auth = services.GetService<IAuth>();
        SessionResolver = services.GetRequiredService<ISessionResolver>();
        CookieSettings = services.GetRequiredService<SessionCookieOptions>();
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
        return originalSession?.WithTenantId(tenantId);
    }
    //
    // public async Task<Session> GetOrCreateSession(HttpContext httpContext)
    // {
    //     var cancellationToken = httpContext.RequestAborted;
    //     var originalSession = GetSession(httpContext);
    //     var tenantId = Settings.TenantIdExtractor.Invoke(httpContext);
    //     var session = originalSession?.WithTenantId(tenantId);
    //
    //     if (session != null && Auth != null) {
    //         var isSignOutForced = await Auth.IsSignOutForced(session, cancellationToken).ConfigureAwait(false);
    //         if (isSignOutForced) {
    //             await Settings.ForcedSignOutHandler(this, httpContext).ConfigureAwait(false);
    //             session = null;
    //         }
    //     }
    //     session ??= SessionFactory.CreateSession().WithTenantId(tenantId);
    //
    //     if (session != originalSession) {
    //         var cookieName = Settings.Cookie.Name ?? "";
    //         var responseCookies = httpContext.Response.Cookies;
    //         responseCookies.Append(cookieName, session.Id, Settings.Cookie.Build(httpContext));
    //     }
    //     return session;
    // }
}
