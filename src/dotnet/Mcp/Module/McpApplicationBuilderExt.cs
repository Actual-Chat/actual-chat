using ActualChat.Mcp.Auth;
using ActualChat.Mcp.Module;
using Microsoft.AspNetCore.Builder;

// ReSharper disable once CheckNamespace
namespace ActualChat.Mcp;

public static class McpApplicationBuilderExt
{
    public static void MapMcp(this WebApplication app)
    {
        var hostInfo = app.Services.HostInfo();
        if (!hostInfo.HasRole(HostRole.Api))
            return;

        var settings = app.Services.GetRequiredService<McpSettings>();
        foreach (var route in settings.Routes) {
            app.UseWhen(
                ctx => ctx.Request.Path.StartsWithSegments(route, StringComparison.OrdinalIgnoreCase),
                branch => branch.UseMiddleware<McpAuthMiddleware>(route));
            Microsoft.AspNetCore.Builder.McpEndpointRouteBuilderExtensions.MapMcp(app, route);
        }
    }
}
