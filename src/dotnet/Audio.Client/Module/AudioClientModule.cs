using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Transcription;

namespace ActualChat.Audio.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class AudioClientModule(IServiceProvider moduleServices) : HostModule(moduleServices)
{
    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.HostKind.IsApp())
            return; // Client-side only module

        var fusion = services.AddFusion();
        services.AddScoped<AudioDownloader>(c => new AudioDownloader(c));
        services.AddScoped<AudioClient>(c => new AudioClient(c));
        services.AddTransient<IAudioStreamer>(c => c.GetRequiredService<AudioClient>());
        services.AddTransient<ITranscriptStreamer>(c => c.GetRequiredService<AudioClient>());
    }
}
