using System.Diagnostics.CodeAnalysis;
using ActualChat.Streaming.UI.Blazor.Components;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using ActualChat.Permissions;
using ActualChat.Streaming.UI.Blazor.Services;
using AudioInitializer = ActualChat.Streaming.UI.Blazor.Services.AudioInitializer;

namespace ActualChat.Streaming.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class StreamingBlazorUIModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    public static string ImportName => "streaming";

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.HostKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddFusion();

        services.AddScoped<ITrackPlayerFactory>(c => new AudioTrackPlayerFactory(c));
        services.AddScoped<AudioInitializer>(c => new AudioInitializer(c.UIHub()));
        services.AddScoped<AudioRecorder>(c => new AudioRecorder(c));
        if (HostInfo.HostKind != HostKind.MauiApp) {
            services.AddScoped<MicrophonePermissionHandler>(c => new WebMicrophonePermissionHandler(c.UIHub()));
            services.AddScoped<IRecordingPermissionRequester>(_ => new WebRecordingPermissionRequester());
        }

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<RecordingTroubleshooterModal.Model, RecordingTroubleshooterModal>()
        );
    }
}
