using ActualChat.Mcp.Module;
using ActualChat.OAuth;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Mcp.Controllers;

[ApiController]
public sealed class McpResourceMetadataController(IServiceProvider services) : ControllerBase
{
    public const string Route = "/.well-known/oauth-protected-resource";

    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private McpSettings Settings { get; } = services.GetRequiredService<McpSettings>();

    [HttpGet(Route), HttpGet(Route + OAuthConstants.McpResourcePath)]
    public ActionResult Get()
    {
        if (Settings.Route.IsNullOrEmpty())
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(new {
            resource = UrlMapper.ToAbsolute(Settings.Route),
            // Must be byte-identical to the issuer OpenIddict derives from the request base URL,
            // which keeps the trailing slash (RFC 8414 §3.3 makes clients compare the two)
            authorization_servers = new[] { UrlMapper.BaseUrl },
            scopes_supported = new[] { "mcp", "offline_access" },
            bearer_methods_supported = new[] { "header" },
        });
    }
}
