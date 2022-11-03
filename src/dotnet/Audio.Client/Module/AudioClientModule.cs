using ActualChat.Hosting;
using ActualChat.Transcription;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Audio.Module;

public class AudioClientModule : HostModule
{
    public AudioClientModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public AudioClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
            return; // Client-side only module

        services.AddFusion().AddRestEaseClient();

        services.AddScoped<AudioDownloader>();
        services.AddScoped<AudioClient>();
        services.AddTransient<IAudioStreamer>(c => c.GetRequiredService<AudioClient>());
        services.AddTransient<ITranscriptStreamer>(c => c.GetRequiredService<AudioClient>());
    }
}
