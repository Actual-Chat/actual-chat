using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Hosting;
using ActualChat.MediaPlayback;
using Stl.Plugins;

namespace ActualChat.Audio.UI.Blazor.Module;

public class AudioBlazorUIModule: HostModule, IBlazorUIModule
{
    public static string ImportName => "audio";

    public AudioBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public AudioBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        services.AddScoped<ITrackPlayerFactory, AudioTrackPlayerFactory>();
        services.AddScoped<AudioRecorder>();
        fusion.AddComputeService<AudioRecorderState>(ServiceLifetime.Scoped);
    }
}
