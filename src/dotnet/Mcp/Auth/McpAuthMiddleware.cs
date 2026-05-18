using Microsoft.AspNetCore.Http;

namespace ActualChat.Mcp.Auth;

public sealed class McpAuthMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext httpContext)
    {
        var session = httpContext.TryGetSessionFromHeader();
        if (session is null || session.Kind != SessionKind.ApiKey) {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            httpContext.Response.Headers.WWWAuthenticate = "Session realm=\"ActualChat\"";
            await httpContext.Response.WriteAsync(
                "MCP requires an API-key Session in the 'Session' HTTP header.").ConfigureAwait(false);
            return;
        }

        httpContext.Items[McpSessionAccessor.HttpContextItemKey] = session;
        await next(httpContext).ConfigureAwait(false);
    }
}
