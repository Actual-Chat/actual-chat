using Microsoft.AspNetCore.Http;
using Stl.Fusion.Server.Authentication;
using Stl.Generators;

namespace ActualChat.Web;

public static class SessionCookies
{
    public static readonly CookieBuilder Cookie = new() {
        Name = "FusionAuth.SessionId",
        IsEssential = true,
        HttpOnly = true,
        SecurePolicy = CookieSecurePolicy.Always,
        SameSite = SameSiteMode.Lax,
        Expiration = TimeSpan.FromDays(28),
    };

    public static async Task<Session> Authenticate(
        HttpContext httpContext,
        ServerAuthHelper serverAuthHelper)
    {
        var originalSession = Read(httpContext);
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
                    Write(httpContext, session);
                return session;
            }
            catch (TimeoutException) {
                if (tryIndex >= 2)
                    throw;
            }
            session = Session.New();
        }
    }

    public static Session? Read(HttpContext httpContext, string? queryParameterName = null)
    {
        var cookies = httpContext.Request.Cookies;
        var cookieName = Cookie.Name ?? "";
        cookies.TryGetValue(cookieName, out var sessionId);
        var session = SessionExt.NewValidOrNull(sessionId);

        if (session == null && !queryParameterName.IsNullOrEmpty()) {
            var query = httpContext.Request.Query;
            session = SessionExt.NewValidOrNull(query[queryParameterName].SingleOrDefault() ?? "");
        }
        return session;
    }

    public static Session Write(HttpContext httpContext, Session session)
    {
        session.RequireValid();
        var cookieBuilder = Cookie;
        var sessionCookie = cookieBuilder.Build(httpContext);
        httpContext.Response.Cookies.Append(cookieBuilder.Name!, session.Id.Value, sessionCookie);
        return session;
    }
}
