using ActualChat.Mcp.Controllers;
using ActualChat.Mcp.Module;
using ActualChat.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace ActualChat.Mcp.Auth;

public sealed class McpAuthMiddleware(RequestDelegate next, IServiceProvider services)
{
    private const string BearerPrefix = "Bearer ";
    private const string Realm = "Voxt";

    private ISessionsBackend SessionsBackend { get; } = services.GetRequiredService<ISessionsBackend>();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private McpSettings Settings { get; } = services.GetRequiredService<McpSettings>();
    private OAuthBearerAuthenticator? BearerAuthenticator { get; } = services.GetService<OAuthBearerAuthenticator>();

    public async Task Invoke(HttpContext httpContext)
    {
        var token = TryGetBearer(httpContext);
        if (token is null) {
            await Reject(httpContext, null, "Missing or malformed Authorization: Bearer header.").ConfigureAwait(false);
            return;
        }

        Session? session;
        if (token.StartsWith(CoreConstants.Session.ApiKeyPrefix)) {
            session = SessionExt.NewValidOrNull(token);
            if (session is not null) {
                var info = await SessionsBackend.Get(session, httpContext.RequestAborted).ConfigureAwait(false);
                if (info is null || !info.IsActive || info.UserId is not { IsGuest: false })
                    session = null;
            }
        }
        else
            session = BearerAuthenticator is null
                ? null
                : await BearerAuthenticator.TryGetSession(httpContext, Settings.Route, httpContext.RequestAborted)
                    .ConfigureAwait(false);

        if (session is null) {
            await Reject(httpContext, "invalid_token", "The token is invalid, expired, or revoked.")
                .ConfigureAwait(false);
            return;
        }

        httpContext.Items[McpSessionAccessor.HttpContextItemKey] = session;
        await next(httpContext).ConfigureAwait(false);
    }

    // Private methods

    private static string? TryGetBearer(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.TryGetValue(HeaderNames.Authorization, out var values))
            return null;
        var header = values.ToString();
        if (header.IsNullOrEmpty() || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;
        return header[BearerPrefix.Length..].Trim();
    }

    private Task Reject(HttpContext httpContext, string? error, string description)
    {
        var metadataUrl = UrlMapper.ToAbsolute(McpResourceMetadataController.Route + Settings.Route);
        var header = $"Bearer realm=\"{Realm}\", resource_metadata=\"{metadataUrl}\", scope=\"mcp\"";
        if (error is not null)
            header += $", error=\"{error}\", error_description=\"{description}\"";
        httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
        httpContext.Response.Headers[HeaderNames.WWWAuthenticate] = header;
        return httpContext.Response.WriteAsync(description);
    }
}
