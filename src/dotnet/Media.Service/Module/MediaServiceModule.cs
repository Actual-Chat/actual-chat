using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat.Events;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Media.Db;
using ActualChat.Redis.Module;
using ActualLab.Fusion.EntityFramework.Operations;

namespace ActualChat.Media.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MediaServiceModule(IServiceProvider moduleServices) : HostModule<MediaSettings>(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.HostKind.IsServer())
            return; // Server-side only module

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<MediaDbContext>(services, Settings.Redis);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, MediaDbInitializer>();
        dbModule.AddDbContextServices<MediaDbContext>(services, Settings.Db, db => {
            db.AddEntityResolver<string, DbMedia>();
        });

        // Commander & Fusion
        var commander = services.AddCommander();
        commander.AddHandlerFilter((handler, commandType) => {
            // 1. Check if this is DbOperationScopeProvider<MediaDbContext> handler
            if (handler is not InterfaceCommandHandler<ICommand> ich)
                return true;
            if (ich.ServiceType != typeof(DbOperationScopeProvider<MediaDbContext>))
                return true;

            // 2. Make sure it's intact only for local commands
            var commandAssembly = commandType.Assembly;
            return commandAssembly == typeof(IMediaBackend).Assembly // Media.Contracts assembly
                || commandType == typeof(TextEntryChangedEvent);
        });

        var backend = services.AddBackend(HostInfo);
        var isClientBackend = backend.GetServiceMode<IMediaBackend>() == ServiceMode.Client;

        // Media
        backend.AddService<IMediaBackend, MediaBackend>();

        // Links
        backend.AddService<IMediaLinkPreviews, MediaLinkPreviews>();
        backend.AddService<ILinkPreviewsBackend, LinkPreviewsBackend>();
        if (isClientBackend)
            return;

        services.AddHttpClient(nameof(LinkPreviewsBackend))
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.Add(new ("ActualChat-Bot", "0.1")));
        services.AddSingleton<Crawler>();
    }
}
