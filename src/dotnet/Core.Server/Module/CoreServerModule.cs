using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using ActualChat.Mesh;
using ActualChat.AspNetCore;
using ActualChat.Commands;
using ActualChat.Rpc;
using ActualLab.Fusion.Server;
using ActualLab.Fusion.Server.Middlewares;
using ActualLab.Fusion.Server.Rpc;
using ActualLab.Rpc;
using ActualLab.Rpc.Clients;
using ActualLab.Rpc.Diagnostics;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.AddRpcHost(HostInfo, Log);

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
        services.AddSingleton<EventHandlerResolver>(c => new EventHandlerResolver(c));
    }
}
