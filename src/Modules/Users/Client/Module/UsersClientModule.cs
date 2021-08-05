﻿using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
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
            if (HostInfo.ServiceScope != ServiceScope.Client)
                return; // Client-side only module

            base.InjectServices(services);
        }
    }
}
