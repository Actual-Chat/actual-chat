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

        services.AddScoped<ITrackPlayerFactory, AudioTrackPlayerFactory>();

        var fusion = services.AddFusion();
        fusion.AddComputeService<AudioRecorderService>(ServiceLifetime.Scoped);
        fusion.AddComputeService<AudioRecorderStatus>(ServiceLifetime.Scoped);
        services.AddScoped<AudioRecorderController>();
        services.AddScoped<AudioRecorder>();
        services.AddScoped<AudioRecorderCommandProcessor>();
    }
}
