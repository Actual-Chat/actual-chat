﻿using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Audio.Client
{
    public class AudioClientModule : HostModule
    {
        public AudioClientModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public AudioClientModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (HostInfo.ServiceScope != ServiceScope.Client)
                return; // Client-side only module

            base.InjectServices(services);
        }
    }
}