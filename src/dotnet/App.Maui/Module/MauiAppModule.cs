using System.Diagnostics.CodeAnalysis;
using ActualChat.App.Maui.Services;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Permissions;
using ActualChat.UI;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ActualLab.Fusion.Client.Caching;
using ActualLab.IO;

namespace ActualChat.App.Maui.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MauiAppModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    protected override void InjectServices(IServiceCollection services)
    {
        // System
        services.AddScoped<ScopedServicesDisposeTracker>(c => new ScopedServicesDisposeTracker(c));

        // Session & authentication
        services.AddSingleton(c => new MauiSession(c));
        services.AddScoped<IClientAuth>(c => new MauiClientAuth(c.UIHub()));

        // UI
        services.Replace(ServiceDescriptor.Singleton<ReloadUI>(c => new MauiReloadUI(c))); // Replaces scoped ReloadUI
        services.AddScoped<BrowserInfo>(c => new MauiBrowserInfo(c.UIHub()));
        services.AddScoped<KeepAwakeUI>(c => new MauiKeepAwakeUI(c));
        services.AddScoped<IMauiShare>(c => new MauiShare(c));
        services.AddScoped<SystemSettingsUI>(_ => new MauiSystemSettingsUI());

        // Permissions
        services.AddScoped<MicrophonePermissionHandler>(c => new MauiMicrophonePermissionHandler(c.UIHub()));

        // ClientComputedCache
        var appCacheDir = new FilePath(FileSystem.CacheDirectory);
        services.AddSingleton(_ => new SQLiteClientComputedCache.Options() {
            DbPath = appCacheDir & "CCC.db3",
        });
        services.AddSingleton<IClientComputedCache>(c => {
            var options = c.GetRequiredService<SQLiteClientComputedCache.Options>();
            return new SQLiteClientComputedCache(options, c);
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

        // Contacts
        services.AddScoped<DeviceContacts>(c => new MauiContacts(c));
        services.AddScoped<ContactsPermissionHandler>(c => new MauiContactsPermissionHandler(c.UIHub()));

        // Test Page
#if ANDROID
        services.RemoveAll<IWebViewCrasher>();
        services.AddSingleton<IWebViewCrasher, AndroidWebViewCrasher>();
#endif
    }
}
