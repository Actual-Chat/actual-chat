using ActualChat.Mcp.Module;
using Microsoft.AspNetCore.Mvc;

namespace ActualChat.Mcp.Controllers;

[ApiController]
public sealed class McpResourceMetadataController(IServiceProvider services) : ControllerBase
{
    public const string Route = "/.well-known/oauth-protected-resource";

    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private McpSettings Settings { get; } = services.GetRequiredService<McpSettings>();

    [HttpGet(Route), HttpGet(Route + "/api/mcp")]
    public ActionResult Get()
    {
        if (Settings.Route.IsNullOrEmpty())
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=3600";
        return Ok(new {
            resource = UrlMapper.ToAbsolute(Settings.Route),
            authorization_servers = new[] { UrlMapper.BaseUrl.TrimEnd('/') },
            scopes_supported = new[] { "mcp", "offline_access" },
            bearer_methods_supported = new[] { "header" },
        });
    }
}
