using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Todos.Client.Module
{
    public class TodosClientModule : HostModule
    {
        public TodosClientModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public TodosClientModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (HostInfo.ServiceScope != ServiceScope.Client)
                return; // Client-side only module

            var fusionClient = services.AddFusion().AddRestEaseClient();
            fusionClient.AddReplicaService<ITodoService, ITodoClientDef>();
        }
    }
}
