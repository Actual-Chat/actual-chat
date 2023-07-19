using ActualChat.Security;
using Microsoft.AspNetCore.Http;
using Stl.Fusion.Server.Authentication;

namespace ActualChat.Web;

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

    public static async Task<Session> Authenticate(
        this HttpContext httpContext,
        ServerAuthHelper serverAuthHelper)
    {
        var originalSession = httpContext.TryGetSession();
        var session = originalSession ?? Session.New();
        for (var tryIndex = 0;; tryIndex++) {
            try {
#if false
                // You can enable this code to verify this logic works
                if (Random.Shared.Next(3) == 0) {
                    await Task.Delay(1000).ConfigureAwait(false);
                    throw new TimeoutException();
                }
#endif
                await serverAuthHelper
                    .UpdateAuthState(session, httpContext)
                    .WaitAsync(TimeSpan.FromSeconds(1))
                    .ConfigureAwait(false);
                if (originalSession != session)
                    httpContext.AddSessionCookie(session);
                return session;
            }
            catch (TimeoutException) {
                if (tryIndex >= 2)
                    throw;
            }
            session = Session.New();
        }
    }

    // NOTE(AY): Not sure if it's going to be useful at all, but...
    public static Session ResolveSession(this HttpContext httpContext, string? queryParameterName = null)
    {
        var session = httpContext.GetSession();
        var sessionResolver = httpContext.RequestServices.GetRequiredService<ISessionResolver>();
        sessionResolver.Session = session;
        return session;
    }

    public static Session GetSession(this HttpContext httpContext, string? queryParameterName = null)
        => httpContext.TryGetSession(queryParameterName).RequireValid();

    public static Session? TryGetSession(this HttpContext httpContext, string? queryParameterName = null)
    {
        if (!queryParameterName.IsNullOrEmpty()) {
            var session = httpContext.TryGetSessionFromQuery(queryParameterName);
            if (session != null)
                return session;
        }

        var request = httpContext.Request;
        if (request.Headers.TryGetValue(Constants.Session.HeaderName, out var sessionIds))
            return SessionExt.NewValidOrNull(sessionIds.SingleOrDefault());
        if (request.Cookies.TryGetValue(Constants.Session.CookieName, out var sessionId))
            return SessionExt.NewValidOrNull(sessionId);
        return null;
    }

    public static Session GetSessionFromQuery(this HttpContext httpContext, string parameterName)
        => httpContext.TryGetSessionFromQuery(parameterName).RequireValid();

    public static Session? TryGetSessionFromQuery(this HttpContext httpContext, string parameterName)
    {
        var sessionId = httpContext.Request.Query[parameterName].SingleOrDefault() ?? "";
        if (sessionId.IsNullOrEmpty())
            return null;

        if (SecureToken.HasValidPrefix(sessionId)) {
            var secureTokensBackend = httpContext.RequestServices.GetRequiredService<ISecureTokensBackend>();
            sessionId = secureTokensBackend.TryParse(sessionId)?.Value;
        }
        return SessionExt.NewValidOrNull(sessionId);
    }

    public static Session AddSessionCookie(this HttpContext httpContext, Session session)
    {
        session.RequireValid();
        var cookieBuilder = Cookie;
        var cookie = cookieBuilder.Build(httpContext);
        httpContext.Response.Cookies.Append(Constants.Session.CookieName, session.Id.Value, cookie);
        return session;
    }
}
