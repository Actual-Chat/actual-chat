using ActualChat.App.Maui.Services;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Maui.Services;
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
        });
        services.AddSingleton<IRemoteComputedCache>(c => {
            var options = c.GetRequiredService<SQLiteRemoteComputedCache.Options>();
            return new SQLiteRemoteComputedCache(options, c);
        });

        // LocalSettings backend override
        var appDataDir = new FilePath(FileSystem.AppDataDirectory);
        services.AddSingleton(c => {
            var dbPath = appDataDir & "LocalSettings.db3";
            var backend = new SQLiteBatchingKvasBackend(dbPath, "1.0", c);
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
    }
}
