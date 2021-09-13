using ActualChat.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Streaming.Client.Module
{
    public class StreamingClientModule : HostModule
    {
        public StreamingClientModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public StreamingClientModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
                return; // Client-side only module

            services.AddSingleton<HubConnection>(c => {
                var uriMapper = c.GetRequiredService<UriMapper>();
                var hubConnection = new HubConnectionBuilder()
                    .WithUrl(uriMapper.ToAbsolute("/api/stream"))
                    .WithAutomaticReconnect()
                    .AddMessagePackProtocol()
                    // .ConfigureLogging(logging =>
                    // {
                    //     // logging.AddConsole();
                    //     logging.SetMinimumLevel(LogLevel.Debug);
                    // })
                    .Build();
                return hubConnection;
            });
            services.AddSingleton<IHubConnectionSentinel, HubConnectionSentinel>();
            services.AddTransient<IRecordingService<AudioRecordingConfiguration>, AudioRecordingServiceClient>();
            services.AddTransient<IAudioRecordingService, AudioRecordingServiceClient>();
            services.AddTransient<IStreamingService<BlobMessage>, BlobStreamingServiceClient>();
            services.AddTransient<IStreamingService<TranscriptMessage>, TranscriptStreamingServiceClient>();
        }
    }
}
