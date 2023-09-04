using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.Permissions;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Stl.Fusion.Client.Caching;
using Stl.IO;

namespace ActualChat.App.Maui.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MauiAppModule : HostModule, IBlazorUIModule
{
    public MauiAppModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        // Session & authentication
        services.AddSingleton(c => new MauiSession(c));
        services.AddScoped<IClientAuth>(c => new MauiClientAuth(c));

        // UI
        services.AddSingleton<ReloadUI>(c => new MauiReloadUI(c)); // Note that it replaces scoped ReloadUI
        services.AddScoped<BrowserInfo>(c => new MauiBrowserInfo(c));
        services.AddScoped<KeepAwakeUI>(c => new MauiKeepAwakeUI(c));
        services.AddScoped<IMauiShare>(_ => new MauiShare());

        // Misc.
        services.AddScoped<DisposeTracer>(c => new DisposeTracer(c));
        services.AddScoped<MicrophonePermissionHandler>(c => new MauiMicrophonePermissionHandler(c));

        // ClientComputedCache
        var appCacheDir = new FilePath(FileSystem.CacheDirectory);
        services.AddSingleton(_ => new SQLiteClientComputedCache.Options() {
            DbPath = appCacheDir & "CCC.db3",
        });
        services.AddSingleton(c => new SQLiteClientComputedCache(
            c.GetRequiredService<SQLiteClientComputedCache.Options>(), c));
        services.AddAlias<IClientComputedCache, SQLiteClientComputedCache>();

        // LocalSettings backend override
        var appDataDir = new FilePath(FileSystem.AppDataDirectory);
        services.AddSingleton(c => {
            var dbPath = appDataDir & "LocalSettings.db3";
            return new LocalSettings.Options() {
                BackendOverride = new SQLiteBatchingKvasBackend(dbPath, "1.0", c),
                ReadBatchConcurrencyLevel = HardwareInfo.ProcessorCount.Clamp(1, 16),
            };
        });

        services.AddScoped<DeviceContacts>(c => new MauiDeviceContacts(c));
    }
}
