using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;

namespace ActualChat.Audio.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class AudioBlazorUIModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "audio";

    public AudioBlazorUIModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddFusion();

        services.AddScoped<ITrackPlayerFactory>(c => new AudioTrackPlayerFactory(c));
        services.AddScoped<AudioInitializer>(c => new AudioInitializer(c));
        services.AddScoped<AudioRecorder>(c => new AudioRecorder(c));
        services.AddScoped(c => new MicrophonePermissionHandler(c));
        services.AddScoped<IRecordingPermissionRequester>(_ => new WebRecordingPermissionRequester());

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<GuideModal.Model, GuideModal>()
        );
    }
}
