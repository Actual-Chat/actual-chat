using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Maui.Services;
using ActualChat.UI.App.Services;
using ActualChat.UI.Caching;
using ActualLab.Fusion.Client.Caching;
using ActualLab.IO;
using ActualLab.Kvasar;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui.Module;

public class MauiModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IAppModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        var appCacheDir = new FilePath(FileSystem.CacheDirectory);
        var appDataDir = new FilePath(FileSystem.AppDataDirectory);
        KvasarStoreSupport.DeleteLegacyDbFiles(ModuleServices,
            appCacheDir & "CCC.db3",
            appDataDir & "LocalSettings.db3");

        // RemoteComputedCache
        services.AddSingleton(_ => new KvasarRemoteComputedCache.Options() {
            BasePath = appCacheDir & "CCC",
            EncryptionKey = MauiPreferences.DbEncryptionKey,
        });
        services.AddSingleton<IRemoteComputedCache>(c => {
            var options = c.GetRequiredService<KvasarRemoteComputedCache.Options>();
            var cache = new KvasarRemoteComputedCache(options, c);
            cache.Kvas.WithSuspendHandler(c);
            return cache;
        });

        // LocalSettings backend override
        services.AddSingleton(_ => new LocalSettings.Options() {
            StoreFactory = c => new KvasarKvas(new KvasarKvas.Options() {
                BasePath = appDataDir & "LocalSettings",
                EncryptionKey = MauiPreferences.DbEncryptionKey,
                Version = "1.0",
                PageSize = 4 * 1024,
                PageCacheBytes = 1024 * 1024,
                // Unlike the computed cache, settings aren't regenerable - don't lose them on power loss.
                Durability = KvasarDurability.Flushed,
            }, c).WithSuspendHandler(c),
        });
        // Make LocalSettings singleton
        services.Replace(ServiceDescriptor.Singleton(c
            => new LocalSettings(c.GetRequiredService<LocalSettings.Options>(), c)));

        // Sharing
#if IOS
        var fusion = services.AddFusion();
        fusion.AddService<IconUI>(ServiceLifetime.Scoped);
        fusion.AddService<IncomingShareSuggestions, AppleIncomingShareSuggestions>(ServiceLifetime.Scoped);

        // Video transcoding
        services.AddScoped<VideoTranscoder>(c => new AppleVideoTranscoder(c));
#elif ANDROID
        var fusion = services.AddFusion();
        fusion.AddService<IconUI>(ServiceLifetime.Scoped);
        fusion.AddService<IncomingShareSuggestions, AndroidIncomingShareSuggestions>(ServiceLifetime.Scoped);
#endif
    }
}
