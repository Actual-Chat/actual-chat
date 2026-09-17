using ActualChat.Db.Module;
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
        rpcHost.AddBackend<IImageSuggestionsBackend, ImageSuggestionsBackend>();

        // Link previews
        rpcHost.AddApi<IMediaLinkPreviews, MediaLinkPreviews>();
        rpcHost.AddBackend<ILinkPreviewsBackend, LinkPreviewsBackend>();
        rpcHost.AddBackend<IGrabStatusesBackend, GrabStatusesBackend>();

        // Uploads
        rpcHost.AddApi<IUploads, Uploads>();
        rpcHost.AddBackend<IUploadsBackend, UploadsBackend>();
        services.AddSingleton<IMediaSaver, MediaSaver>();
        services.AddSingleton<IImageGenerations, ImageGenerations>();

        // GIFs
        rpcHost.AddApi<IGifs, Gifs>();
        services.AddSingleton<EgressGuard>();
        services.AddEgressHttpClient(Gifs.HttpClientName);

        if (isBackendClient)
            return;

        // The services below are used only when this module operates in non-client mode

        // Internal services
        services.AddEgressHttpClient(Crawler.HttpClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(Crawler.DefaultUserAgent));
        services.AddEgressHttpClient(RobotsFiles.HttpClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.UserAgent.ParseAdd(Crawler.DefaultUserAgent));
        services.AddEgressHttpClient(ImageGrabber.HttpClientName)
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
            db.AddEntityResolver<string, DbImageSuggestion>();
            db.AddEntityResolver<string, DbLinkPreview>();
        });

        // Flows
        services.AddFlows()
            .Add<LinkPreviewFlow>()
            .Add<PreviewThumbnailUpdateFlow>()
            .Add<UploadProcessingFlow>()
            .Add<ImageSuggestionSweepFlow>();

        // Uploads
        services.AddSingleton<UploadsStorage>();
    }
}

public static class MediaServiceCollectionExt
{
    public static IHttpClientBuilder AddEgressHttpClient(
        this IServiceCollection services, string name, long? maxResponseContentLength = null)
        => services.AddHttpClient(name)
            .ConfigurePrimaryHttpMessageHandler(c => {
                var guard = c.GetRequiredService<EgressGuard>();
                var options = new EgressHttpHandler.Options(guard.IsAllowedUri, guard.IsAllowedAddress);
                if (maxResponseContentLength is { } max)
                    options = options with { MaxResponseContentLength = max };
                return new EgressHttpHandler(options);
            });
}
