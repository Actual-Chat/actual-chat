using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Distribution.Module
{
    public class DistributionModule : HostModule
    {
        public DistributionModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public DistributionModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            services.AddSettings<DistributionSettings>();
            var settings = services.BuildServiceProvider().GetRequiredService<DistributionSettings>();
            var multiplexer = ConnectionMultiplexer.Connect(settings.Redis);
            services.AddSingleton<IConnectionMultiplexer>(multiplexer);
            services.AddTransient<IHubRegistrar,HubRegistrar>();
            services.AddTransient<IStreamingService<AudioMessage>, AudioStreamingService>();
            services.AddTransient<IStreamingService<VideoMessage>, VideoStreamingService>();
            services.AddTransient<IStreamingService<TranscriptMessage>, TranscriptStreamingService>();
            services.AddTransient<IServerSideStreamingService<AudioMessage>, ServerSideStreamingService<AudioMessage>>();
            services.AddTransient<IServerSideStreamingService<VideoMessage>, ServerSideStreamingService<VideoMessage>>();
            services.AddTransient<IServerSideStreamingService<TranscriptMessage>, ServerSideStreamingService<TranscriptMessage>>();
            
            services.AddSignalR()
                .AddMessagePackProtocol(); // TODO: no AOT compilation support yet 
        }
    }
}