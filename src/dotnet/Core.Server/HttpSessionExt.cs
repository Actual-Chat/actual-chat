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
    public static readonly CookieBuilder TokenCookie = new() {
        Name = Constants.Session.TokenCookieName,
        IsEssential = true,
        HttpOnly = true,
        SecurePolicy = CookieSecurePolicy.Always,
        SameSite = SameSiteMode.Lax,
        Expiration = TimeSpan.FromHours(1),
    };

    public static void AddSessionCookie(this HttpContext httpContext, Session session)
    {
        session.RequireValid();
        var cookie = Cookie.Build(httpContext);
        httpContext.Response.Cookies.Append(Constants.Session.CookieName, session.Id, cookie);
    }

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
        return secureTokensBackend.TryDecryptSessionToken(header);
    }

    public static Session GetSessionFromCookie(this HttpContext httpContext)
        => httpContext.TryGetSessionFromCookie().RequireValid();
    public static Session? TryGetSessionFromCookie(this HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(Constants.Session.CookieName, out var sessionId) || sessionId.IsNullOrEmpty())
            return null;

        // Reject API keys from cookie-based auth
        if (sessionId.Length > 0 && sessionId[0] == CoreConstants.Session.ApiKeyPrefix)
            return null;

        return SessionExt.NewValidOrNull(sessionId);
    }

    // Token cookie

    public static void AddSessionTokenCookie(this HttpContext httpContext, string sessionToken)
    {
        var cookie = TokenCookie.Build(httpContext);
        httpContext.Response.Cookies.Append(Constants.Session.TokenCookieName, sessionToken, cookie);
    }

    public static void RemoveSessionTokenCookie(this HttpContext httpContext)
        => httpContext.Response.Cookies.Delete(Constants.Session.TokenCookieName);

    public static Session? TryPullSessionFromTokenCookie(this HttpContext httpContext)
    {
        var session = httpContext.TryGetSessionFromTokenCookie();
        if (session != null)
            httpContext.RemoveSessionTokenCookie();
        return session;
    }

    public static Session? TryGetSessionFromTokenCookie(this HttpContext httpContext)
    {
        if (!httpContext.Request.Cookies.TryGetValue(Constants.Session.TokenCookieName, out var token) || token.IsNullOrEmpty())
            return null;

        var secureTokensBackend = httpContext.RequestServices.GetRequiredService<ISecureTokensBackend>();
        return secureTokensBackend.TryDecryptSessionToken(token);
    }
}
