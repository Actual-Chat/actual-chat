using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Plugins;
using Stl.Time;

namespace ActualChat.Module
{
    public class CoreModule : HostModule
    {
        public CoreModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public CoreModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var fusion = services.AddFusion();
        }
    }
}
