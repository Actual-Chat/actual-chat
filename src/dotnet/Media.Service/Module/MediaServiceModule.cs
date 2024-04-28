using System.Diagnostics.CodeAnalysis;
using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Media.Db;
using ActualChat.Redis.Module;

namespace ActualChat.Media.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MediaServiceModule(IServiceProvider moduleServices)
    : HostModule<MediaSettings>(moduleServices), IServerModule
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

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddHttpClient(Crawler.HttpClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(Crawler.DefaultUserAgent));
        services.AddSingleton<Crawler>();
        services.AddSingleton<ICrawlingHandler, WebSiteHandler>();
        services.AddSingleton<ICrawlingHandler, ImageLinkHandler>();
        services.AddSingleton<ImageGrabber>();

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
