using ActualChat.Mcp.Module;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Mcp.Controllers;

[ApiController]
public sealed class McpResourceMetadataController(IServiceProvider services) : ControllerBase
{
    public const string Route = "/.well-known/oauth-protected-resource";

    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private McpSettings Settings { get; } = services.GetRequiredService<McpSettings>();

    [HttpGet(Route), HttpGet(Route + "/{**resourcePath}")]
    public ActionResult Get(string? resourcePath)
    {
        // RFC 9728 §3.3: "resource" must match the URL the client connected to, so each MCP route
        // gets its own document; the bare well-known URL describes the canonical route
        var route = resourcePath.IsNullOrEmpty()
            ? Settings.Routes.FirstOrDefault()
            : Settings.Routes.FirstOrDefault(r => string.Equals(
                r.Trim('/'), resourcePath.Trim('/'), StringComparison.OrdinalIgnoreCase));
        if (route is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(new {
            resource = UrlMapper.ToAbsolute(route),
            // Must be byte-identical to the issuer OpenIddict derives from the request base URL,
            // which keeps the trailing slash (RFC 8414 §3.3 makes clients compare the two)
            authorization_servers = new[] { UrlMapper.BaseUrl },
            scopes_supported = new[] { "mcp", "offline_access" },
            bearer_methods_supported = new[] { "header" },
        });
    }
}
