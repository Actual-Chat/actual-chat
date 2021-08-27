using ActualChat.Hosting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Distribution.Client.Module
{
    public class DistributionClientModule : HostModule
    {
        public DistributionClientModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public DistributionClientModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
                return; // Client-side only module

            var navigationManager = services.BuildServiceProvider().GetRequiredService<NavigationManager>();
            var streamConnection = new HubConnectionBuilder()
                .WithUrl(navigationManager.ToAbsoluteUri("/api/stream"))
                .WithAutomaticReconnect()
                .Build();
            services.AddSingleton(streamConnection);
            services.AddSingleton<IHubConnectionSentinel, HubConnectionSentinel>();
            services.AddTransient<IStreamingService<AudioMessage>, AudioStreamingServiceClient>();
            services.AddTransient<IStreamingService<VideoMessage>, VideoStreamingServiceClient>();
            services.AddTransient<IStreamingService<TranscriptMessage>, TranscriptStreamingServiceClient>();
        }
    }
}
