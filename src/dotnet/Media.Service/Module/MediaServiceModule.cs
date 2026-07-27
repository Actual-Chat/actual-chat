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
        var isBackendClient = HostInfo.Roles.GetBackendServiceMode<IMediaBackend>() is ServiceMode.Client;

        // Media
        rpcHost.AddApi<IMedia, MediaService>(name: "IMedias");
        rpcHost.AddBackend<IMediaBackend, MediaBackend>();
        rpcHost.AddBackend<IMediaProgressBackend, MediaProgressBackend>();

        // Link previews
        rpcHost.AddApi<IMediaLinkPreviews, MediaLinkPreviews>();
        rpcHost.AddBackend<ILinkPreviewsBackend, LinkPreviewsBackend>();
        rpcHost.AddBackend<IGrabStatusesBackend, GrabStatusesBackend>();

        // Uploads
        rpcHost.AddApi<IUploads, Uploads>();
        rpcHost.AddBackend<IUploadsBackend, UploadsBackend>();
        services.AddSingleton<IMediaSaver, MediaSaver>();

        // GIFs
        rpcHost.AddApi<IGifs, Gifs>();
        services.AddSingleton<EgressGuard>();
        AddEgressHttpClient(services, Gifs.HttpClientName);

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        AddEgressHttpClient(services, Crawler.HttpClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(Crawler.DefaultUserAgent));
        AddEgressHttpClient(services, RobotsFiles.HttpClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(Crawler.DefaultUserAgent));
        AddEgressHttpClient(services, ImageGrabber.HttpClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(Crawler.DefaultUserAgent));
        services.AddSingleton<Crawler>();
        services.AddSingleton<RobotsFiles>();
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
            db.AddEntityResolver<string, DbMediaProgress>();
            db.AddEntityResolver<string, DbGrabStatus>();
            db.AddEntityResolver<string, DbLinkPreview>();
        });

        // Flows
        services.AddFlows()
            .Add<LinkPreviewFlow>()
            .Add<PreviewThumbnailUpdateFlow>()
            .Add<UploadProcessingFlow>();

        // Uploads
        services.AddSingleton<UploadsStorage>();
    }

    // Private methods

    private static IHttpClientBuilder AddEgressHttpClient(IServiceCollection services, string name)
        => services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(c => {
                var guard = c.GetRequiredService<EgressGuard>();
                var options = new EgressHttpHandler.Options(guard.IsAllowedUri, guard.IsAllowedAddress);
                return new EgressHttpHandler(options);
            });
}
