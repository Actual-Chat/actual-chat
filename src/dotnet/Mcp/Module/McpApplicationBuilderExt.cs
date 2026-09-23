using ActualChat.Mcp.Auth;
using ActualChat.Mcp.Module;
using Microsoft.AspNetCore.Builder;

// ReSharper disable once CheckNamespace
namespace ActualChat.Mcp;

public static class McpApplicationBuilderExt
{
    public static IEndpointConventionBuilder? MapMcp(this WebApplication app)
    {
        var hostInfo = app.Services.HostInfo();
        if (!hostInfo.HasRole(HostRole.Api))
            return null;

        var settings = app.Services.GetRequiredService<McpSettings>();
        var route = settings.Route;
        if (route.IsNullOrEmpty())
            return null;

        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments(route, StringComparison.OrdinalIgnoreCase),
            branch => branch.UseMiddleware<McpAuthMiddleware>());
        return Microsoft.AspNetCore.Builder.McpEndpointRouteBuilderExtensions.MapMcp(app, route);
    }
}
