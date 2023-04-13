using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.MediaPlayback.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class PlaybackModule : HostModule
{
    [ServiceConstructor]
    public PlaybackModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        services.TryAddScoped<IPlaybackFactory>(sp=> new PlaybackFactory(sp));

        var fusion = services.AddFusion();
        fusion.AddComputeService<ActivePlaybackInfo>(ServiceLifetime.Scoped);
    }
}
