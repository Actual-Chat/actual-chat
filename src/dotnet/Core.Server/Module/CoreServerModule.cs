using System.Diagnostics.CodeAnalysis;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
using ActualLab.Rpc;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class CoreServerModule(IServiceProvider moduleServices) : HostModule<CoreServerSettings>(moduleServices)
{
    protected override CoreServerSettings ReadSettings()
        => Cfg.GetSettings<CoreServerSettings>(nameof(CoreSettings));

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        var hostKind = HostInfo.HostKind;
        if (hostKind.IsApp())
            throw StandardError.Internal("This module can be used on server side only.");

        // Core server-side services
        services.AddSingleton(c => new ServerSideServiceDefs(c));

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
