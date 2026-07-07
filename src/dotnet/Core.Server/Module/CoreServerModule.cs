using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using ActualChat.AspNetCore;
using ActualChat.Diagnostics;
using ActualChat.Queues.Internal;
using ActualChat.Uploads;
using Google.Cloud.Storage.V1;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.Module;

public sealed class CoreServerModule(IServiceProvider moduleServices)
    : HostModule<CoreServerSettings>(moduleServices), IServerModule
{
    protected override CoreServerSettings LoadSettings()
    {
        var settings = Cfg.Settings<CoreServerSettings>(nameof(CoreSettings));
        if (!settings.GoogleProjectId.IsNullOrEmpty())
            return settings;

        var environmentProjectId = Cfg["GOOGLE_CLOUD_PROJECT"];
        if (!environmentProjectId.IsNullOrEmpty())
            settings.GoogleProjectId = environmentProjectId;
        return settings;
    }

    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        services.AddRpcHost(HostInfo, Log);

        // ShardOwners
        ShardKeyResolvers.MustThrowOnNotFound |= HostInfo.BaseUrlKind != BaseUrlKind.Production;
        services.AddSingleton(c => new ShardOwners(c));

        // Queues
        services.AddSingleton(c => new EventHandlerRegistry(c));
        if (Settings.UseNatsQueues) {
            services.AddNats(HostInfo);
            services.AddNatsQueues();
        }
        else
            services.AddInMemoryQueues();

        // Upload processors
        services.AddSingleton<RasterImageNormalizer>();
        services.AddSingleton<SvgRasterizer>();
        services.AddSingleton<IUploadProcessor, IconUploadProcessor>();
        services.AddSingleton<IUploadProcessor, ImageUploadProcessor>();

        // Blob storages
        var storageBucket = Settings.GoogleStorageBucket;
        if (storageBucket.IsNullOrEmpty()) {
            services.AddSingleton<IContentTypeProvider>(_ => ContentTypeProvider.Instance);
            services.AddSingleton(_ => new LocalFolderBlobStorage.Options());
            services.AddSingleton<IBlobStorages>(c => new LocalFolderBlobStorages(c));
        }
        else
            services.AddSingleton<IBlobStorages>(_ => new GoogleCloudBlobStorages(storageBucket));

        // Video upload processor
        if (Settings.UseGoogleTranscoder && !storageBucket.IsNullOrEmpty())
            services.AddSingleton<IUploadProcessor>(c => new GoogleCloudVideoUploadProcessor(
                StorageClient.Create(),
                storageBucket,
                Settings.GoogleProjectId,
                Settings.GoogleRegionId,
                c.LogFor<GoogleCloudVideoUploadProcessor>()));
        else
            services.AddSingleton<IUploadProcessor, LocalVideoUploadProcessor>();
        services.AddSingleton<IMediaProcessor, MediaProcessor>();

        services.AddSingleton<AudioSourceDownloader>();

        // Controllers, etc.
        services.AddRouting();
        services.AddMvcCore(options => {
            options.ModelBinderProviders.Add(new MvcModelBinderProvider());
            options.ModelMetadataDetailsProviders.Add(new MvcValidationMetadataProvider());
        });
        services.AddResponseCaching();
        // Health-related services
        services.AddSingleton(c => new HealthEventListener(c));
        services.AddAlias<IHealthState, HealthEventListener>();
    }
}
