using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;

namespace ActualChat.MediaPlayback.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class MediaPlaybackModule : HostModule
{
    public MediaPlaybackModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddScoped<IPlaybackFactory>(c => new PlaybackFactory(c));

        var fusion = services.AddFusion();
        fusion.AddService<ActivePlaybackInfo>(ServiceLifetime.Scoped);
    }
}
