using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using Stl.Plugins;

namespace ActualChat.Audio.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class AudioBlazorUIModule: HostModule, IBlazorUIModule
{
    public static string ImportName => "audio";

    public AudioBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public AudioBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddFusion();

        services.AddScoped<ITrackPlayerFactory>(c => new AudioTrackPlayerFactory(c));
        services.AddScoped<AudioRecorder>(c => new AudioRecorder(c));
    }
}
