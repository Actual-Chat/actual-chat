using ActualChat.Db.Module;
using ActualChat.Hosting;
using ActualChat.Media.Db;
using ActualChat.Media.Flows;
using ActualChat.Redis.Module;
using ActualChat.Uploads;

namespace ActualChat.Media.Module;

public sealed class MediaServiceModule(IServiceProvider moduleServices)
    : HostModule<MediaSettings>(moduleServices), IServerModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        var rpcHost = services.AddRpcHost(HostInfo);
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IMediaBackend>().IsClient();

        // Link previews
        rpcHost.AddApi<IMediaLinkPreviews, MediaLinkPreviews>();
        rpcHost.AddBackend<ILinkPreviewsBackend, LinkPreviewsBackend>();
        rpcHost.AddBackend<IMediaBackend, MediaBackend>();
        rpcHost.AddBackend<IGrabStatusesBackend, GrabStatusesBackend>();

        // Uploads
        rpcHost.AddApi<IUploads, Uploads>();

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddHttpClient(Crawler.HttpClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(Crawler.DefaultUserAgent));
        services.AddSingleton<Crawler>();
        services.AddSingleton<RobotsFiles>();
        services.AddSingleton<ICrawlingHandler, WebSiteHandler>();
        services.AddSingleton<ICrawlingHandler, ImageLinkHandler>();
        services.AddSingleton<ImageGrabber>();
        services.AddSingleton<EgressGuard>();

        // Redis
        var redisModule = Host.GetModule<RedisModule>();
        redisModule.AddRedisDb<MediaDbContext>(services);

        // DB
        var dbModule = Host.GetModule<DbModule>();
        services.AddSingleton<IDbInitializer, MediaDbInitializer>();
        dbModule.AddDbContextServices<MediaDbContext>(services, db => {
            db.AddEntityResolver<string, DbMedia>();
            db.AddEntityResolver<string, DbGrabStatus>();
            db.AddEntityResolver<string, DbLinkPreview>();
        });

        // Flows
        services.AddFlows().Add<LinkPreviewFlow>().Add<PreviewThumbnailUpdateFlow>();

        // Uploads
        rpcHost.AddBackend<IUploadsBackend, UploadsBackend>();
        services.AddSingleton<UploadsStorage>();
        services.AddSingleton<IMediaSaver>(c => new MediaSaver(c.Commander(), c.GetRequiredService<IContentSaver>()));
        services.AddSingleton<IMediaProcessor, MediaProcessor>();
    }
}
