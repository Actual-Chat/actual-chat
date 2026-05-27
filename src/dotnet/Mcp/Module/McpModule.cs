using System.Text.Json.Serialization.Metadata;
using ActualChat.Hosting;
using ActualChat.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;

namespace ActualChat.Mcp.Module;

public sealed class McpModule(IServiceProvider moduleServices)
    : HostModule<McpSettings>(moduleServices), IServerModule
{
    private const string ServerName = "Voxt";

    protected override void InjectServices(IServiceCollection services)
    {
        if (Settings.Route.IsNullOrEmpty() || !HostInfo.HasRole(HostRole.Api))
            return;

        services.TryAddSingleton<Auth.McpSessionAccessor>();
        services.AddHttpContextAccessor();

        var serializerOptions = new JsonSerializerOptions(SystemJsonSerializer.Default.Options);
        serializerOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        services
            .AddMcpServer(o => o.ServerInfo = new Implementation {
                Name = ServerName,
                Version = ApiConstants.VersionString,
            })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<McpMessageTools>(serializerOptions)
            .WithTools<McpChatTools>(serializerOptions);
    }
}
