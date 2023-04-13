using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;

namespace ActualChat.Audio.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public partial class AudioBlazorUIModule: HostModule, IBlazorUIModule
{
    public static string ImportName => "audio";

    [ServiceConstructor]
    public AudioBlazorUIModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddFusion();

        services.AddScoped<ITrackPlayerFactory>(c => new AudioTrackPlayerFactory(c));
        services.AddScoped<AudioInfo>(c => new AudioInfo(c));
        services.AddScoped<AudioRecorder>(c => new AudioRecorder(c));
        services.AddScoped(c => new MicrophonePermissionHandler(c));
        services.AddScoped<IRecordingPermissionRequester>(_ => new WebRecordingPermissionRequester());

        // Matching type finder
        services.AddSingleton<IMatchingTypeRegistry>(c => new AudioBlazorUIMatchingTypeRegistry());
    }
}
