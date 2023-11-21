using System.Diagnostics.CodeAnalysis;
using ActualChat.Blobs.Internal;
using ActualChat.Hosting;
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
        var appKind = HostInfo.AppKind;
        if (appKind.IsClient())
            throw StandardError.Internal("This module can be used on server side only.");

        services.AddSingleton<IContentSaver>(
            c => new ContentSaver(c.GetRequiredService<IBlobStorageProvider>()));

        var storageBucket = Settings.GoogleStorageBucket;
        if (storageBucket.IsNullOrEmpty()) {
            services.AddSingleton<IContentTypeProvider>(ContentTypeProvider.Instance);
            services.AddSingleton<LocalFolderBlobStorage.Options>();
            services.AddSingleton<IBlobStorageProvider>(c => new LocalFolderBlobStorageProvider(c));
        }
        else
            services.AddSingleton<IBlobStorageProvider>(new GoogleCloudBlobStorageProvider(storageBucket));
    }
}
