using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Maui.Services;
using ActualChat.UI.App.Services;
using ActualLab.Fusion.Client.Caching;
using ActualLab.IO;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui.Module;

public class MauiModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IAppModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // RemoteComputedCache
        var appCacheDir = new FilePath(FileSystem.CacheDirectory);
        services.AddSingleton(_ => new SQLiteRemoteComputedCache.Options() {
            DbPath = appCacheDir & "CCC.db3",
            Key = MauiPreferences.DbEncryptionKey,
        });
        services.AddSingleton<IRemoteComputedCache>(c => {
            var options = c.GetRequiredService<SQLiteRemoteComputedCache.Options>();
            var cache = new SQLiteRemoteComputedCache(options, c);
            return cache;
        });

        // LocalSettings backend override
        var appDataDir = new FilePath(FileSystem.AppDataDirectory);
        services.AddSingleton(c => {
            var dbPath = appDataDir & "LocalSettings.db3";
            var backend = new SQLiteBatchingKvasBackend(dbPath, "1.0", c, MauiPreferences.DbEncryptionKey);
            return new LocalSettings.Options() {
                BackendFactory = _ => backend,
                ReaderWorkerPolicy = new BatchProcessorWorkerPolicy() {
                    MinWorkerCount = 2,
                    MaxWorkerCount = HardwareInfo.ProcessorCount.Clamp(2, 16),
                },
            };
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
