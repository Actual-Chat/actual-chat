using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Plugins;

namespace ActualChat.MediaPlayback.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class PlaybackModule : HostModule
{
    public PlaybackModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public PlaybackModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module

        services.TryAddScoped<IPlaybackFactory, PlaybackFactory>();

        var fusion = services.AddFusion();
        fusion.AddComputeService<IActivePlaybackInfo, ActivePlaybackInfo>(ServiceLifetime.Scoped);
    }
}
