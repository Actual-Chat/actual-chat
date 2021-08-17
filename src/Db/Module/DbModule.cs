using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Plugins;

namespace ActualChat.Db.Module
{
    public class DbModule : HostModule
    {
        public DbModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public DbModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var fusion = services.AddFusion();
            fusion.AddOperationReprocessor();
        }
    }
}
