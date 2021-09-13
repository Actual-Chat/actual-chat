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
            services.AddSingleton<IStreamingService<BlobMessage>, StreamingService<BlobMessage>>();
            services.AddSingleton<IStreamingService<TranscriptMessage>, StreamingService<TranscriptMessage>>();
            services.AddSingleton<IRecordingService<AudioRecordingConfiguration>, AudioRecordingService>();
            services.AddSingleton<IAudioRecordingService, AudioRecordingService>();
            services.AddSingleton<IServerSideStreamingService<BlobMessage>, ServerSideStreamingService<BlobMessage>>();
            services.AddSingleton<IServerSideStreamingService<TranscriptMessage>, ServerSideStreamingService<TranscriptMessage>>();
            services.AddSingleton<IServerSideRecordingService<AudioRecording>, ServerSideRecordingService<AudioRecording>>();
            
            services.AddSignalR()
                .AddMessagePackProtocol(); // TODO: no AOT compilation support yet 
        }
    }
}