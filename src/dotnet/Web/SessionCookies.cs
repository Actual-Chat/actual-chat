using Microsoft.AspNetCore.Http;

namespace ActualChat.Web;

public sealed class SessionCookies
{
    public static readonly CookieBuilder Cookie = new() {
        Name = "FusionAuth.SessionId",
        IsEssential = true,
        HttpOnly = true,
        SecurePolicy = CookieSecurePolicy.Always,
        SameSite = SameSiteMode.Lax,
        Expiration = TimeSpan.FromDays(28),
    };

    private ISessionFactory SessionFactory { get; }

    public SessionCookies(IServiceProvider services)
        => SessionFactory = services.SessionFactory();

    public Session? Read(HttpContext httpContext)
    {
        var cookies = httpContext.Request.Cookies;
        var cookieName = Cookie.Name ?? "";
        cookies.TryGetValue(cookieName, out var sessionId);
        if (sessionId.IsNullOrEmpty() || OrdinalEquals(sessionId, Session.Default.Id))
            return null;

        return new Session(sessionId);
    }

    public Session ReadOrWriteNew(HttpContext httpContext)
        => Read(httpContext) ?? WriteNew(httpContext);

    public Session Write(HttpContext httpContext, Session session)
    {
        session.RequireValid();
        var cookieBuilder = Cookie;
        var sessionCookie = cookieBuilder.Build(httpContext);
        httpContext.Response.Cookies.Append(cookieBuilder.Name!, session.Id.Value, sessionCookie);
        return session;
    }

    public Session WriteNew(HttpContext httpContext)
        => Write(httpContext, SessionFactory.CreateSession());
}
