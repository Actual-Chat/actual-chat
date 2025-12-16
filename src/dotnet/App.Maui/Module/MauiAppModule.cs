using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services;
using ActualChat.App.Maui.Services.Playback;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.MediaPlayback;
using ActualChat.UI;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using ActualLab.Fusion.Client.Caching;
using ActualLab.IO;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.App.Maui.Module;

#pragma warning disable IL2026 // Fine for modules

public sealed class MauiAppModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IAppModule, IBlazorUIModule
{
    public static string ImportName => "mauiApp";

    protected override void InjectServices(IServiceCollection services)
    {
        // System
        services.AddScoped<MauiWebViewPageContextTracker>(c => new MauiWebViewPageContextTracker(c));

        // Session & authentication
        services.AddSingleton(c => new MauiSession(c));
        services.AddScoped<IClientAuth>(c => new MauiAuth(c.UIHub()));

        // UI
        services.Replace(ServiceDescriptor.Singleton<ReloadUI>(c => new MauiReloadUI(c))); // Replaces scoped ReloadUI
        services.AddScoped<BrowserInfo>(c => new MauiBrowserInfo(c.UIHub()));
        services.AddScoped<KeepAwakeUI>(c => new MauiKeepAwakeUI(c.UIHub()));
        services.AddScoped<KeepWebViewAliveUI>(c => new (c.UIHub()));
        services.AddScoped<IMauiShare>(c => new MauiShare(c));
        services.AddScoped<IMauiHostSwitcher>(c => new MauiHostSwitcher(c.UIHub().UrlMapper, c.GetRequiredService<ReloadUI>()));
        services.AddScoped<IDeveloperTools>(_ => new MauiDeveloperTools());
        services.AddScoped<SystemSettingsUI>(_ => new MauiSystemSettingsUI());
        services.AddScoped<IMediaMetadataUI>(c => new MediaMetadataUI(c.AppUIHub()));
        services.AddSingleton<MauiTestPage.IMauiTestPageBackend>(_ => new MauiTestPageBackend());

        // Permissions
        services.AddScoped<MicrophonePermissionHandler>(c => new MauiMicrophonePermissionHandler(c.UIHub()));
        services.AddScoped<IDataCollectionSettingsUI>(_ => new MauiDataCollectionSettingsUI());

        // Audio
        services.AddScoped<IAudioRecorderEngine>(c => new MauiRecorderEngine(c.AppUIHub()));
#if WINDOWS
        services.AddScoped<IAudioCodec, OpusAudioCodec>();
        services.AddScoped<TuneUI>(c => new MauiTunes(c.UIHub()));
        // services.AddSingleton<VoiceActivityDetector>(c => new NoopVoiceActivityDetector(c));
        services.AddSingleton<VoiceActivityDetector>(c => {
            return new OnnxVoiceActivityDetector(c, ModelLoader);

            async Task<byte[]> ModelLoader()
            {
                var modelStream = await FileSystem
                    .OpenAppPackageFileAsync(@"wwwroot\dist\assets\ort\vad_batched.ort")
                    .ConfigureAwait(true);
                await using var _ = modelStream.ConfigureAwait(true);
                using var ms = new MemoryStream();
                await modelStream.CopyToAsync(ms, CancellationToken.None).ConfigureAwait(true);
                ms.Position = 0;
                return ms.ToArray();
            }
        });
#elif ANDROID
        services.AddScoped<IAudioCodec, OpusAudioCodec>();
        services.AddScoped<TuneUI>(c => new MauiTunes(c.UIHub()));
        services.AddSingleton<VoiceActivityDetector>(c => new TfLiteVoiceActivityDetector(c));
#elif IOS || MACCATALYST
        services.AddScoped<VoiceActivityDetector>(c => new CoreMLVoiceActivityDetector(c));
#endif
        services.AddScoped<IAudioInitializer>(c => new MauiAudioInitializer());
        services.AddScoped<IAudioPlaybackEngineFactory>(c => new MauiAudioPlaybackEngineFactory(c.AppUIHub()));

        // Notifications
        services.AddSingleton<MauiNotifications>(c => new MauiNotifications(c));

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

        // Contacts
        services.AddScoped<DeviceContacts>(c => new MauiContacts(c));
        services.AddScoped<ContactsPermissionHandler>(c => new MauiContactsPermissionHandler(c.UIHub()));

        // Audio Focus
#if ANDROID
        services.AddScoped<AudioFocusService>(c => new AndroidAudioFocusService(c.AppUIHub()));
#endif
        // File attachments
#if ANDROID || WINDOWS
        services.AddScoped<IAttachmentFilePicker>(c => new MauiAttachmentFilePicker(c));
#endif
        services.AddScoped<IMauiFileProviderImplFactory>(c => new MauiFileProviderImplFactory(c));

        // Test Page
#if ANDROID
        services.RemoveAll<IWebViewCrasher>();
        services.AddSingleton<IWebViewCrasher, AndroidWebViewCrasher>();
#endif
    }
}
