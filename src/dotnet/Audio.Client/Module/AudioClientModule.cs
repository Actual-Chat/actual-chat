using ActualChat.Hosting;
using ActualChat.Transcription;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Audio.Client.Module;

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

        services.AddSingleton<AudioDownloader>();
        services.AddSingleton<AudioClient>();
        services.AddTransient<ISourceAudioRecorder>(c => c.GetRequiredService<AudioClient>());
        services.AddTransient<IAudioSourceStreamer>(c => c.GetRequiredService<AudioClient>());
        services.AddTransient<ITranscriptStreamer>(c => c.GetRequiredService<AudioClient>());
    }
}
