using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using ActualChat.Transcription;
using Stl.Fusion.Client;

namespace ActualChat.Audio.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class AudioClientModule : HostModule
{
    public AudioClientModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.IsClient())
            return; // Client-side only module

        var fusion = services.AddFusion();
        services.AddScoped<AudioDownloader>(c => new AudioDownloader(c));
        services.AddScoped<AudioClient>(c => new AudioClient(c));
        services.AddTransient<IAudioStreamer>(c => c.GetRequiredService<AudioClient>());
        services.AddTransient<ITranscriptStreamer>(c => c.GetRequiredService<AudioClient>());
    }
}
