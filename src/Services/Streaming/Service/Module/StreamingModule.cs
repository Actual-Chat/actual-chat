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
            services.AddSingleton<IStreamer<BlobPart>, Streamer<BlobPart>>();
            services.AddSingleton<IStreamer<TranscriptPart>, Streamer<TranscriptPart>>();
            services.AddSingleton<IAudioUploader, AudioUploader>();
            services.AddSingleton<IServerSideStreamer<BlobPart>, ServerSideStreamer<BlobPart>>();
            services.AddSingleton<IServerSideStreamer<TranscriptPart>, ServerSideStreamer<TranscriptPart>>();
            services.AddSingleton<IServerSideRecorder<AudioRecord>, ServerSideRecorder<AudioRecord>>();

            services.AddSignalR()
                .AddMessagePackProtocol(); // TODO: no AOT compilation support yet
        }
    }
}
