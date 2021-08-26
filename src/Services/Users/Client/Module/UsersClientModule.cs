using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Client;
using Stl.Plugins;

namespace ActualChat.Users.Client.Module
{
    public class UsersClientModule : HostModule
    {
        public UsersClientModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public UsersClientModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Client))
                return; // Client-side only module

            var fusion = services.AddFusion();
            var fusionClient = services.AddFusion().AddRestEaseClient();
            var fusionAuth = fusion.AddAuthentication().AddRestEaseClient();

            fusionClient.AddReplicaService<IUserInfoService, IUserInfoClientDef>();
            fusionClient.AddReplicaService<IUserStateService, IUserStateClientDef>();
        }
    }
}
