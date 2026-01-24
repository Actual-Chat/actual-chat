using ActualChat.App.Maui.Audio;
using ActualChat.App.Maui.Services;
using ActualChat.App.Maui.Services.Playback;
using ActualChat.App.Maui.Services.Recording;
using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.Maui.Services;
using ActualChat.MediaPlayback;
using ActualChat.UI;
using ActualChat.UI.App.Services.NativeAppSettings;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Components;
using ActualChat.UI.Blazor.Services;
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
        services.AddScoped<MauiSentryInitializer>();

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

        // Contacts
        services.AddScoped<DeviceContacts>(c => new MauiContacts(c));
        services.AddScoped<ContactsPermissionHandler>(c => new MauiContactsPermissionHandler(c.UIHub()));

        // Audio Focus
#if ANDROID
        services.AddScoped<AudioFocusService>(c => new AndroidAudioFocusService(c.AppUIHub()));
#endif
        // File attachments
#if ANDROID
        services.AddScoped<IAttachmentFilePicker>(c => new AndroidAttachmentFilePicker(c));
#elif IOS
        services.AddScoped<IAttachmentFilePicker>(c => new IosAttachmentFilePicker(c));
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
