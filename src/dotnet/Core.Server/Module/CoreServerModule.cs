using System.Diagnostics.CodeAnalysis;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using ActualChat.AspNetCore;
using ActualChat.Diagnostics;
using ActualChat.Flows;
using ActualChat.Queues.Internal;
using ActualChat.Uploads;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class CoreServerModule(IServiceProvider moduleServices)
    : HostModule<CoreServerSettings>(moduleServices), IServerModule
{
    static CoreServerModule()
    {
        ShardKeyResolvers.Register<FlowId>(static x => ShardKeyResolvers.ForString(x.Value));
        ShardKeyResolvers.Register<IFlowEvent>(static x => ShardKeyResolvers.ForString(x.FlowId.Value));
        MeshRefResolvers.Register<Flows_Store>(static _ => NodeRef.OwnNodeAlias);
    }

    protected override CoreServerSettings GetSettings()
        => Cfg.Settings<CoreServerSettings>(nameof(CoreSettings));

    protected override void InjectServices(IServiceCollection services)
    {
        // RPC host
        services.AddRpcHost(HostInfo, Log);

        // Queues
        services.AddSingleton(c => new EventHandlerRegistry(c));
        if (Settings.UseNatsQueues) {
            services.AddNats(HostInfo);
            services.AddNatsQueues();
        }
        else
            services.AddInMemoryQueues();

        // Upload processors
        services.AddSingleton<IUploadProcessor, ImageUploadProcessor>();
        services.AddSingleton<IUploadProcessor, VideoUploadProcessor>();

        // Blob storages & IContentSaver
        var storageBucket = Settings.GoogleStorageBucket;
        if (storageBucket.IsNullOrEmpty()) {
            services.AddSingleton<IContentTypeProvider>(_ => ContentTypeProvider.Instance);
            services.AddSingleton(_ => new LocalFolderBlobStorage.Options());
            services.AddSingleton<IBlobStorages>(c => new LocalFolderBlobStorages(c));
        }
        else
            services.AddSingleton<IBlobStorages>(_ => new GoogleCloudBlobStorages(storageBucket));
        services.AddSingleton<IContentSaver>(c => new ContentSaver(c.BlobStorages()));

        // Controllers, etc.
        services.AddRouting();
        var mvc = services.AddMvc(options => {
            options.ModelBinderProviders.Add(new MvcModelBinderProvider());
            options.ModelMetadataDetailsProviders.Add(new MvcValidationMetadataProvider());
        });
        mvc.AddApplicationPart(GetType().Assembly);
        services.AddResponseCaching();

        // Health-related services
        services.AddSingleton(c => new HealthEventListener(c));
        services.AddAlias<IHealthState, HealthEventListener>();
    }
}
