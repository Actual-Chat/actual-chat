using System;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Hosting
{
    public abstract class HostModule : Plugin
    {
        protected HostInfo HostInfo { get; } = null!;

        protected HostModule(IPluginInfoProvider.Query _) : base(_) { }
        protected HostModule(IPluginHost plugins) : base(plugins)
            => HostInfo = plugins.GetRequiredService<HostInfo>();

        public abstract void InjectServices(IServiceCollection services);
    }
}
