using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using ActualChat.Permissions;

namespace ActualChat.Audio.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class AudioBlazorUIModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    public static string ImportName => "audio";

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddFusion();

        services.AddScoped<ITrackPlayerFactory>(c => new AudioTrackPlayerFactory(c));
        services.AddScoped<AudioInitializer>(c => new AudioInitializer(c.UIHub()));
        services.AddScoped<AudioRecorder>(c => new AudioRecorder(c));
        if (HostInfo.AppKind != AppKind.MauiApp) {
            services.AddScoped<MicrophonePermissionHandler>(c => new WebMicrophonePermissionHandler(c.UIHub()));
            services.AddScoped<IRecordingPermissionRequester>(_ => new WebRecordingPermissionRequester());
        }

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<RecordingTroubleshooterModal.Model, RecordingTroubleshooterModal>()
        );
    }
}
