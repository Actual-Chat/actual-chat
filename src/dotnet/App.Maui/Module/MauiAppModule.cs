using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Location;
using ActualChat.App.Maui.Services;
using ActualChat.App.Maui.Services.Playback;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using ActualChat.UI;
using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.App.Maui.Module;

public sealed class MauiAppModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IAppModule, IBlazorUIModule
{
    public static string ImportName => "mauiApp";

    protected override void InjectServices(IServiceCollection services)
    {
        var fusion = services.AddFusion();

        // System
        services.AddScoped<MauiWebViewPageContextTracker>(c => new MauiWebViewPageContextTracker(c));
        services.AddScoped<MauiSentryInitializer>();

        // Session & authentication
        services.AddSingleton(c => new MauiSession(c));
        fusion.AddService<AccountUI, MauiAccountUI>(ServiceLifetime.Scoped);

        // UI
        services.AddSingleton<ScopedServicesAccessor>(_ => static () => TryGetScopedServices(out var c) ? c : null); // Scoped in WASM/SSB, singleton in MAUI
        services.AddScoped<BrowserInfo>(c => new MauiBrowserInfo(c.UIHub()));
        services.AddScoped<KeepAwakeUI>(c => new MauiKeepAwakeUI(c.UIHub()));
        services.AddScoped<KeepWebViewAliveUI>(c => new (c.UIHub()));
        services.AddScoped<IMauiShare>(c => new MauiShare(c));
        services.AddScoped<AppServerInstanceSelector>(c => new MauiAppServerInstanceSelector(c.UIHub()));
        services.AddScoped<SystemSettingsUI>(_ => new MauiSystemSettingsUI());
        services.AddScoped<IMediaMetadataUI>(c => new MediaMetadataUI(c.AppUIHub()));
        services.AddSingleton<ReloadUI>(c => new MauiReloadUI(c)); // Replaces scoped ReloadUI
        services.AddSingleton<BackgroundStateTracker>(_ => new MauiBackgroundStateTracker()); // Replaces scoped WebBackgroundStateTracker
        services.AddSingleton<MauiTestPage.IMauiTestPageBackend>(_ => new MauiTestPageBackend());

        // Permissions
        services.AddScoped<MicrophonePermissionHandler>(c => new MauiMicrophonePermissionHandler(c.UIHub()));
        services.AddScoped<CameraPermissionHandler>(c => new MauiCameraPermissionHandler(c.UIHub()));
        services.AddScoped<LocationPermissionHandler>(c => new MauiLocationPermissionHandler(c.UIHub()));
        services.AddScoped<IDataCollectionSettingsUI>(_ => new MauiDataCollectionSettingsUI());
        services.AddScoped<ReportUI>(c => new MauiReportUI(c.UIHub())); // Replaces base no-op ReportUI

        // Connectivity
        services.AddScoped<ConnectivityUI>(c => new MauiConnectivityUI(c.UIHub()));

        // Audio
        services.AddScoped<IAudioRecorderEngine>(c => new MauiRecorderEngine(c.AppUIHub()));
#if WINDOWS
        services.AddScoped<AudioFocusUI>(_ => new AudioFocusUI());
        services.AddScoped<TuneUI>(c => new MauiTuneUI(c.UIHub()));
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
        services.AddSingleton<IAudioCodec, OpusAudioCodec>();
        services.AddScoped<ILocationTracker>(c => new MauiLocationTracker(c.AppUIHub()));
#elif ANDROID
        services.AddScoped<AudioFocusUI>(c => new AndroidAudioFocusUI(c.AppUIHub()));
        services.AddScoped<TuneUI>(c => new MauiTuneUI(c.UIHub()));
        services.AddScoped<AudioWidget>(c => new AndroidAudioWidget(c.AppUIHub()));
        services.AddSingleton<VoiceActivityDetector>(c => new TfLiteVoiceActivityDetector(c));
        services.AddSingleton<IAudioCodec, OpusAudioCodec>();
        services.AddScoped<ILocationTracker>(c => new AndroidLocationTracker(c.AppUIHub()));
#elif IOS || MACCATALYST
        services.AddScoped<AudioFocusUI>(c => new AppleAudioFocusUI(c.AppUIHub()));
        services.AddScoped<TuneUI>(c => new AppleTuneUI(c.UIHub()));
        services.AddScoped<VoiceActivityDetector>(c => new CoreMLVoiceActivityDetector(c));
        services.AddScoped<IAudioCodec, AppleAudioCodec>();
        services.AddScoped<ResamplerFactory>(c => new ResamplerFactory(c.AppUIHub()));
        services.AddScoped<AudioEngines>(c => new AudioEngines(c.AppUIHub()));
#if IOS
        services.AddScoped<Haptics>(c => new Haptics(c.AppUIHub()));
#endif
        services.AddScoped<AudioSession>(c => new AudioSession(c.AppUIHub()));
        services.AddScoped<IAudioCapture>(c => new AppleAudioCapture(c.AppUIHub()));
        services.AddScoped<ILocationTracker>(c => new AppleLocationTracker(c.AppUIHub()));
#endif
        services.AddScoped<IAudioInitializer>(c => new MauiAudioInitializer());
        services.AddScoped<IAudioPlaybackEngineFactory>(c => new MauiAudioPlaybackEngineFactory(c.AppUIHub()));

        // Notifications
        services.AddSingleton<MauiNotifications>(c => new MauiNotifications(c));

        // Contacts
        services.AddScoped<DeviceContacts>(c => new MauiContacts(c));
        services.AddScoped<ContactsPermissionHandler>(c => new MauiContactsPermissionHandler(c.UIHub()));

        // File attachments
#if ANDROID
        services.AddScoped<IAttachmentFilePicker>(c => new AndroidAttachmentFilePicker(c));
#elif IOS || MACCATALYST
        services.AddScoped<IAttachmentFilePicker>(c => new AppleAttachmentFilePicker(c));
        services.AddSingleton<ApplePhotoGalleryFiles>();
#else
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
