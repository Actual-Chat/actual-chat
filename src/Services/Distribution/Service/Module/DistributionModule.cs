using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.SignalR;
using Stl.Plugins;

namespace ActualChat.Distribution.Module
{
    public class DistributionModule : HostModule
    {
        public DistributionModule(IPluginInfoProvider.Query _) : base(_)
        {
        }

        public DistributionModule(IPluginHost plugins) : base(plugins)
        {
        }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            services.AddSignalR();
            // TODO: Register some hub discovery services
        }
    }
}