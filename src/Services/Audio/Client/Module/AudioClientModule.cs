using ActualChat.Blobs;
using ActualChat.Hosting;
using ActualChat.Streaming;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Audio.Client.Module
{
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

            services.AddSingleton<AudioClient>();
            services.AddTransient<IAudioUploader>(c => c.GetRequiredService<AudioClient>());
            services.AddTransient<IStreamer<BlobPart>>(c => c.GetRequiredService<AudioClient>());
            services.AddTransient<IStreamer<TranscriptPart>>(c => c.GetRequiredService<AudioClient>());
        }
    }
}
