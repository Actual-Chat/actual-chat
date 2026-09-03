#if ANDROID
using ActualChat.App.Maui.Activities;
#endif
#if !MACOS
using ActualChat.App.Maui.Audio;
#endif
#if ANDROID || IOS || MACCATALYST
using ActualChat.App.Maui.Location;
#endif
using ActualChat.App.Maui.Services;
using ActualChat.App.Maui.Services.Playback;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.Audio;
using ActualChat.MediaPlayback;
using ActualChat.UI;
using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.App.Services.Gestures;
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
        services.AddSingleton(c => new MauiWebAuthenticator(c));
        fusion.AddService<AccountUI, MauiAccountUI>(ServiceLifetime.Scoped);

        // UI
        // Scoped in WASM/SSB, singleton in MAUI
        services.AddSingleton<ScopedServicesAccessor>(_ => static () => TryGetScopedServices(out var c) ? c : null);
        services.AddScoped<BrowserInfo>(c => new MauiBrowserInfo(c.UIHub()));
        services.AddScoped<KeepAwakeUI>(c => new MauiKeepAwakeUI(c.UIHub()));
        services.AddScoped<KeepWebViewAliveUI>(c => new (c.UIHub()));
        services.AddScoped<IMauiShare>(c => new MauiShare(c));
        services.AddScoped<AppServerInstanceSelector>(c => new MauiAppServerInstanceSelector(c.UIHub()));
        services.AddScoped<SystemSettingsUI>(_ => new MauiSystemSettingsUI());
        services.AddScoped<ExternalUrlOpener>(c => new MauiExternalUrlOpener(c.UIHub()));
#if ANDROID
        services.AddScoped<ExternalMapOpener>(c => new AndroidMapOpener(c.AppUIHub()));
#elif IOS || MACCATALYST
        services.AddScoped<ExternalMapOpener>(c => new AppleMapOpener(c.AppUIHub()));
#else
        services.AddScoped<ExternalMapOpener>(c => new MauiMapOpener(c.AppUIHub()));
#endif
        services.AddScoped<IMediaMetadataUI>(c => new MediaMetadataUI(c.AppUIHub()));
        services.AddScoped<ReloadUI>(c => new MauiReloadUI(c)); // Replaces base ReloadUI
        // Replaces scoped WebBackgroundStateTracker
        services.AddSingleton<BackgroundStateTracker>(_ => new MauiBackgroundStateTracker());
        services.AddSingleton<ThermalTracker>(c => new MauiThermalTracker(c)); // Replaces scoped WebThermalTracker
        services.AddSingleton<MauiTestPage.IMauiTestPageBackend>(_ => new MauiTestPageBackend());

        // Permissions
#if MACOS
        // The labs Essentials package has no Permissions implementation - the Maui* handlers
        // would throw from every check - so the AppKit ones ask TCC directly.
        services.AddScoped<MicrophonePermissionHandler>(c => new MacOSMicrophonePermissionHandler(c.UIHub()));
        services.AddScoped<CameraPermissionHandler>(c => new MacOSCameraPermissionHandler(c.UIHub()));
        services.AddScoped<LocationPermissionHandler>(c => new MacOSLocationPermissionHandler(c.UIHub()));
#else
        services.AddScoped<MicrophonePermissionHandler>(c => new MauiMicrophonePermissionHandler(c.UIHub()));
        services.AddScoped<CameraPermissionHandler>(c => new MauiCameraPermissionHandler(c.UIHub()));
        services.AddScoped<LocationPermissionHandler>(c => new MauiLocationPermissionHandler(c.UIHub()));
#endif
        services.AddScoped<IDataCollectionSettingsUI>(_ => new MauiDataCollectionSettingsUI());
        services.AddScoped<ReportUI>(c => new MauiReportUI(c.UIHub())); // Replaces base no-op ReportUI

        // Connectivity
        services.AddScoped<ConnectivityUI>(c => new MauiConnectivityUI(c.UIHub()));

        // Audio
#if MACOS
        // Native audio capture isn't ported to the AppKit backend, so recording runs the same
        // JS pipeline the web app uses in Safari; WKWebView covers it (getUserMedia + worklets).
        // TODO(FC): switch to MauiRecorderEngine once native capture is ported - see the
        // migration TODO in the MACOS audio branch above.
        services.AddScoped<IAudioRecorderEngine>(c => new WebRecorderEngine(c.AppUIHub()));
#else
        services.AddScoped<IAudioRecorderEngine>(c => new MauiRecorderEngine(c.AppUIHub()));
#endif
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
        services.AddScoped<ActivitiesBackend>(c => new AndroidActivitiesBackend(c.AppUIHub()));
        services.AddSingleton<VoiceActivityDetector>(c => new TfLiteVoiceActivityDetector(c));
        services.AddSingleton<IAudioCodec, OpusAudioCodec>();
        services.AddScoped<ILocationTracker>(c => new AndroidLocationTracker(c.AppUIHub()));
#elif MACOS
        // Leanest possible audio stack for the AppKit experiment: playback goes through the
        // WebView's JS audio engine (see MauiAudioPlaybackEngineFactory's fallback branch),
        // native capture / VAD / audio session management are not ported yet.
        // TODO(FC): migrate to native audio capture + playback, like the other MAUI platforms.
        // The port needs: an AVAudioSession-free rewrite of MaciOS/Audio's AudioSession +
        // AppleAudioFocusUI (the API doesn't exist on macOS - CoreAudio device notifications
        // instead), a net-macos target for ActualLab.Opus.MaciOS (or an OpusSharp-based codec),
        // and per-TFM bundling of the CoreML VAD model. Until then the JS pipeline below and
        // the Web* registrations under "#if MACOS" in this file are the recording/playback path.
        services.AddScoped<AudioFocusUI>(_ => new AudioFocusUI());
        // MauiTuneUI plays through Plugin.Maui.Audio, which has no macos TFM, and AppleTuneUI needs
        // the AVAudioSession-based engine + focus stack; both wait for the native audio port above.
        services.AddScoped<TuneUI>(c => new WebTuneUI(c.UIHub()));
        services.AddSingleton<VoiceActivityDetector>(c => new NoopVoiceActivityDetector(c));
        services.AddSingleton<IAudioCodec, OpusAudioCodec>();
        services.AddScoped<ILocationTracker>(c => new MauiLocationTracker(c.AppUIHub()));
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
        services.AddScoped<SensorFeed>(c => new MauiSensorFeed(c.AppUIHub()));
#if MACOS
        services.AddScoped<IAudioInitializer>(c => new AudioInitializer(c.UIHub()));
#else
        services.AddScoped<IAudioInitializer>(c => new MauiAudioInitializer());
#endif
        services.AddScoped<IAudioPlaybackEngineFactory>(c => new MauiAudioPlaybackEngineFactory(c.AppUIHub()));

        // Notifications
        services.AddSingleton<MauiNotifications>(c => new MauiNotifications(c));

        // Contacts
#if MACOS
        // Same as the permission handlers above: Essentials Contacts is unimplemented on macos
        services.AddScoped<DeviceContacts>(c => new MacOSContacts(c));
        services.AddScoped<ContactsPermissionHandler>(c => new MacOSContactsPermissionHandler(c.UIHub()));
#else
        services.AddScoped<DeviceContacts>(c => new MauiContacts(c));
        services.AddScoped<ContactsPermissionHandler>(c => new MauiContactsPermissionHandler(c.UIHub()));
#endif

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
