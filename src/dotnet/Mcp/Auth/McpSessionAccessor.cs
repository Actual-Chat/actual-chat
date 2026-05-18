using Microsoft.AspNetCore.Http;

namespace ActualChat.Mcp.Auth;

public sealed class McpSessionAccessor(IHttpContextAccessor httpContextAccessor)
{
    public const string HttpContextItemKey = "ActualChat.Mcp.Session";

    public Session Session {
        get {
            var httpContext = httpContextAccessor.HttpContext
                ?? throw StandardError.Internal("No HttpContext is available for the current MCP request.");
            if (httpContext.Items[HttpContextItemKey] is Session session)
                return session;
            throw StandardError.Unauthorized("MCP request is not authenticated.");
        }
    }
}
