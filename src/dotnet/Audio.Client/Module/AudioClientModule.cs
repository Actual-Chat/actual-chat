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

            services.AddFusion().AddRestEaseClient();

            services.AddSingleton<AudioClient>();
            services.AddTransient<IAudioRecorder>(c => c.GetRequiredService<AudioClient>());
            services.AddTransient<IAudioStreamProvider>(c => c.GetRequiredService<AudioClient>());
            services.AddTransient<ITranscriptStreamProvider>(c => c.GetRequiredService<AudioClient>());
        }
    }
}
