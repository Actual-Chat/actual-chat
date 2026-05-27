using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace ActualChat.Mcp.Auth;

public sealed class McpAuthMiddleware(RequestDelegate next, ISessionsBackend sessionsBackend)
{
    private const string BearerPrefix = "Bearer ";
    private const string Realm = "Voxt";

    public async Task Invoke(HttpContext httpContext)
    {
        var session = TryGetSession(httpContext);
        if (session is null) {
            await Reject(httpContext, "invalid_request", "Missing or malformed Authorization: Bearer header.").ConfigureAwait(false);
            return;
        }
        if (session.Kind != SessionKind.ApiKey) {
            await Reject(httpContext, "invalid_token", "Token is not an API key.").ConfigureAwait(false);
            return;
        }

        var info = await sessionsBackend.Get(session, httpContext.RequestAborted).ConfigureAwait(false);
        if (info is null || !info.IsActive || info.UserId is not { IsGuest: false }) {
            await Reject(httpContext, "invalid_token", "API key is inactive, expired, or not linked to a user.").ConfigureAwait(false);
            return;
        }

        httpContext.Items[McpSessionAccessor.HttpContextItemKey] = session;
        await next(httpContext).ConfigureAwait(false);
    }

    // Private methods

    private static Session? TryGetSession(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue(HeaderNames.Authorization, out var values))
            return null;
        var header = values.ToString();
        if (header.IsNullOrEmpty() || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var token = header[BearerPrefix.Length..].Trim();
        return SessionExt.NewValidOrNull(token);
    }

    private static Task Reject(HttpContext httpContext, string error, string description)
    {
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        httpContext.Response.Headers[HeaderNames.WWWAuthenticate] =
            $"Bearer realm=\"{Realm}\", error=\"{error}\", error_description=\"{description}\"";
        return httpContext.Response.WriteAsync(description);
    }
}
