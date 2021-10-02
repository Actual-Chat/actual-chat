using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Playback.Module;

public class PlaybackModule : HostModule
{
    public PlaybackModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public PlaybackModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module

        // Common services
        var fusion = services.AddFusion();
        fusion.AddComputeService<IPlaybackManager, PlaybackManager>(ServiceLifetime.Scoped);
    }
}
