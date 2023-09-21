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

    public static async Task<(Session Session, bool IsNew)> Authenticate(
        this HttpContext httpContext,
        ServerAuthHelper serverAuthHelper,
        CancellationToken cancellationToken = default)
    {
        var originalSession = httpContext.TryGetSessionFromCookie();
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
                    .UpdateAuthState(session, httpContext, cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(1), cancellationToken)
                    .ConfigureAwait(false);
                var isNew = originalSession != session;
                if (isNew)
                    httpContext.AddSessionCookie(session);
                return (session, isNew);
            }
            catch (TimeoutException) {
                if (tryIndex >= 2)
                    throw;
            }
            session = Session.New();
        }
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
        var cookieBuilder = Cookie;
        var cookie = cookieBuilder.Build(httpContext);
        httpContext.Response.Cookies.Append(Constants.Session.CookieName, session.Id.Value, cookie);
        return session;
    }
}
