using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Streaming.Module
{
    public class StreamingModule : HostModule
    {
        public StreamingModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public StreamingModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            services.AddSettings<StreamingSettings>();
            var settings = services.BuildServiceProvider().GetRequiredService<StreamingSettings>();
            var multiplexer = ConnectionMultiplexer.Connect(settings.Redis);
            services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            services.AddTransient<IHubRegistrar,HubRegistrar>();
            services.AddTransient<StreamingServiceHub>();
            services.AddSingleton<IAudioStreamingService, AudioStreamingService>();
            services.AddSingleton<IStreamingService<AudioMessage>, AudioStreamingService>();
            services.AddSingleton<IStreamingService<VideoMessage>, StreamingService<VideoMessage>>();
            services.AddSingleton<IStreamingService<TranscriptMessage>, StreamingService<TranscriptMessage>>();
            services.AddSingleton<IServerSideAudioStreamingService, ServerSideAudioStreamingService>();
            services.AddSingleton<IServerSideStreamingService<AudioMessage>, ServerSideAudioStreamingService>();
            services.AddSingleton<IServerSideStreamingService<VideoMessage>, ServerSideStreamingService<VideoMessage>>();
            services.AddSingleton<IServerSideStreamingService<TranscriptMessage>, ServerSideStreamingService<TranscriptMessage>>();
            
            services.AddSignalR()
                .AddMessagePackProtocol(); // TODO: no AOT compilation support yet 
        }
    }
}