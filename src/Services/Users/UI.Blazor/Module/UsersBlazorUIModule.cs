using System;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Plugins;

namespace ActualChat.Users.UI.Blazor.Module
{
    public class UsersBlazorUIModule: HostModule, IBlazorUIModule
    {
        public UsersBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public UsersBlazorUIModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            services.RemoveAll(typeof(PresenceService.Options));
            services.AddSingleton(_ => new PresenceService.Options() {
                UpdatePeriod = TimeSpan.FromSeconds(50),
            });
        }
    }
}
