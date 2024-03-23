using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat.Events;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Media.Db;
using ActualChat.Redis.Module;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Media.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MediaServiceModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IMediaBackend>().IsClient();

        // ASP.NET Core controllers
        if (rpcHost.IsApiHost)
            services.AddMvcCore().AddApplicationPart(GetType().Assembly);

        // Link previews
        rpcHost.AddApi<IMediaLinkPreviews, MediaLinkPreviews>();
        rpcHost.AddBackend<ILinkPreviewsBackend, LinkPreviewsBackend>();
        rpcHost.AddBackend<IMediaBackend, MediaBackend>();

        // Commander handlers
        rpcHost.Commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<MediaDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<MediaDbContext>))
                return true;

            // 2. Check if we're running on the client backend
            if (isBackendClient)
                return false;

            // 3. Make sure the handler is intact only for local commands
            var commandNamespace = commandType.Namespace;
            return commandNamespace.OrdinalStartsWith(typeof(IMediaLinkPreviews).Namespace!)
                || commandType == typeof(TextEntryChangedEvent); // Event
        });
        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddHttpClient(nameof(LinkPreviewsBackend))
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.Add(new ("ActualChat-Bot", "0.1")));
        services.AddHttpClient(nameof(LinkPreviewsBackend) + ".fallback")
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.Add(new ("googlebot", null)));
        services.AddSingleton<Crawler>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<MediaDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, MediaDbInitializer>();
        dbModule.AddDbContextServices<MediaDbContext>(services, db => {
            db.AddEntityResolver<string, DbMedia>();
        });
    }
}
