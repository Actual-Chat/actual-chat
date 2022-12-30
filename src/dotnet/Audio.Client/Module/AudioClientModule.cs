using System.Diagnostics.CodeAnalysis;
using System.Net;
using ActualChat.Hosting;
using ActualChat.Transcription;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Audio.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class AudioClientModule : HostModule
{
    public AudioClientModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public AudioClientModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
            return; // Client-side only module

        var fusionClient = services.AddFusion().AddRestEaseClient();
        fusionClient.ConfigureHttpClient((c, name, o) => {
            o.HttpClientActions.Add(client => {
                client.DefaultRequestVersion = HttpVersion.Version30;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            });
        });

        services.AddScoped<AudioDownloader>(sp => new AudioDownloader(sp));
        services.AddScoped<AudioClient>(sp => new AudioClient(sp));
        services.AddTransient<IAudioStreamer>(c => c.GetRequiredService<AudioClient>());
        services.AddTransient<ITranscriptStreamer>(c => c.GetRequiredService<AudioClient>());
    }
}
