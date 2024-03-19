using System.Diagnostics.CodeAnalysis;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using ActualChat.AspNetCore;
using ActualChat.Queues.Internal;
using ActualChat.Queues.Nats;
using Microsoft.AspNetCore.StaticFiles;
using NATS.Client.Core;
using NATS.Client.Hosting;

namespace ActualChat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class CoreServerModule(IServiceProvider moduleServices)
    : HostModule<CoreServerSettings>(moduleServices), IServerModule
{
    protected override CoreServerSettings ReadSettings()
        => Cfg.GetSettings<CoreServerSettings>(nameof(CoreSettings));

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        var hostKind = HostInfo.HostKind;
        if (!hostKind.IsServer())
            throw StandardError.Internal("This module can be used on server side only.");

        // RPC
        services.AddRpcHost(HostInfo, Log);

        // Command queues
        services.AddSingleton(c => new EventHandlerRegistry(c));
        var useInMemoryQueues = HostInfo.IsProductionInstance && HostInfo.HasRole(HostRole.OneServer);
        if (useInMemoryQueues)
            services.AddInMemoryQueues();
        else {
            var natsSettings = Cfg.GetSettings<NatsSettings>();
            var natsTimeout = TimeSpan.FromSeconds(IsDevelopmentInstance ? 300 : 10);
            services.AddNats(
                poolSize: 1,
                options => options with {
                    Url = natsSettings.Url,
                    TlsOpts = new NatsTlsOpts {
                        Mode = TlsMode.Auto,
                    },
                    AuthOpts = natsSettings.Seed.IsNullOrEmpty() || natsSettings.NKey.IsNullOrEmpty()
                        ? NatsAuthOpts.Default
                        : new NatsAuthOpts {
                            Seed = natsSettings.Seed,
                            NKey = natsSettings.NKey,
                        },
                    CommandTimeout = natsTimeout,
                    ConnectTimeout = natsTimeout,
                    RequestTimeout = natsTimeout,
                });
            services.AddNatsQueues();
        }

        // Controllers, etc.
        services.AddRouting();
        var mvc = services.AddMvc(options => {
            options.ModelBinderProviders.Add(new MvcModelBinderProvider());
            options.ModelMetadataDetailsProviders.Add(new MvcValidationMetadataProvider());
        });
        mvc.AddApplicationPart(GetType().Assembly);
        services.AddResponseCaching();

        // Other services
        services.AddSingleton<IContentSaver>(c => new ContentSaver(c.BlobStorages()));
        var storageBucket = Settings.GoogleStorageBucket;
        if (storageBucket.IsNullOrEmpty()) {
            services.AddSingleton<IContentTypeProvider>(_ => ContentTypeProvider.Instance);
            services.AddSingleton(_ => new LocalFolderBlobStorage.Options());
            services.AddSingleton<IBlobStorages>(c => new LocalFolderBlobStorages(c));
        }
        else
            services.AddSingleton<IBlobStorages>(_ => new GoogleCloudBlobStorages(storageBucket));
    }
}
