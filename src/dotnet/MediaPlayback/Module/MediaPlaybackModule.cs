using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.MediaPlayback.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MediaPlaybackModule : HostModule
{
    public MediaPlaybackModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddScoped<IPlaybackFactory>(c => new PlaybackFactory(c));

        var fusion = services.AddFusion();
        fusion.AddService<ActivePlaybackInfo>(ServiceLifetime.Scoped);
    }
}
