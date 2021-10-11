using ActualChat.Hosting;
using ActualChat.Playback;
using ActualChat.UI.Blazor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Audio.UI.Blazor;

public class AudioBlazorUIModule: HostModule, IBlazorUIModule
{
    /// <inheritdoc />
    public static string ImportName => "audio";

    public AudioBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public AudioBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module

        services.AddScoped<IMediaTrackPlayerFactory>(serviceProvider => new MediaTrackPlayerFactory()
            .WithFactory(track
                => track.Source is AudioSource
                    ? new AudioTrackPlayer(serviceProvider.GetRequiredService<IJSRuntime>(), track)
                    : null));
        services.AddScoped<IMediaPlayer, MediaPlayer>();
    }
}
