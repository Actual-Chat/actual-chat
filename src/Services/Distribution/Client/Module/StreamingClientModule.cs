using ActualChat.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Distribution.Client.Module
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

            var hostUriProvider = services.BuildServiceProvider().GetRequiredService<IHostUriProvider>();
            var streamConnection = new HubConnectionBuilder()
                .WithUrl(hostUriProvider.GetAbsoluteUri("/api/stream"))
                .WithAutomaticReconnect()
                // .ConfigureLogging(logging =>
                // {
                //     // logging.AddConsole();
                //     logging.SetMinimumLevel(LogLevel.Debug);
                // })
                .Build();
            services.AddSingleton(streamConnection);
            services.AddSingleton<IHubConnectionSentinel, HubConnectionSentinel>();
            services.AddTransient<IStreamingService<AudioMessage>, AudioStreamingServiceClient>();
            services.AddTransient<IStreamingService<VideoMessage>, VideoStreamingServiceClient>();
            services.AddTransient<IStreamingService<TranscriptMessage>, TranscriptStreamingServiceClient>();
        }
    }
}
