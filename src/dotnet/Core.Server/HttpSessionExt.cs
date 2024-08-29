using ActualChat.Security;
using Microsoft.AspNetCore.Http;

namespace ActualChat;

public static class HttpSessionExt
{
    public static readonly CookieBuilder Cookie = new() {
        Name = Constants.Session.CookieName,
        IsEssential = true,
        HttpOnly = true,
        SecurePolicy = CookieSecurePolicy.Always,
        SameSite = SameSiteMode.Lax,
        Expiration = TimeSpan.FromDays(28),
    };

    public static Session GetSessionFromHeader(this HttpContext httpContext, SessionFormat format = SessionFormat.Id)
        => httpContext.TryGetSessionFromHeader(format).RequireValid();
    public static Session? TryGetSessionFromHeader(this HttpContext httpContext, SessionFormat format = SessionFormat.Id)
    {
        if (!httpContext.Request.Headers.TryGetValue(Constants.Session.HeaderName, out var headers))
            return null;

        var header = headers.SingleOrDefault();
        if (header.IsNullOrEmpty())
            return null;

        if (!SecureToken.HasValidPrefix(header))
            return format is SessionFormat.Token ? null : SessionExt.NewValidOrNull(header);

        var secureTokensBackend = httpContext.RequestServices.GetRequiredService<ISecureTokensBackend>();
        return secureTokensBackend.TryParseSessionToken(header);
    }

    public static Session GetSessionFromCookie(this HttpContext httpContext)
        => httpContext.TryGetSessionFromCookie().RequireValid();
    public static Session? TryGetSessionFromCookie(this HttpContext httpContext)
        => httpContext.Request.Cookies.TryGetValue(Constants.Session.CookieName, out var sessionId)
            ? SessionExt.NewValidOrNull(sessionId)
            : null;

    public static Session AddSessionCookie(this HttpContext httpContext, Session session)
    {
        session.RequireValid();
        var cookie = Cookie.Build(httpContext);
        httpContext.Response.Cookies.Append(Constants.Session.CookieName, session.Id.Value, cookie);
        return session;
    }
}
