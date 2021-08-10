using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Blazor;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.Module
{
    public class BlazorUICoreModule : HostModule
    {
        public BlazorUICoreModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public BlazorUICoreModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var fusion = services.AddFusion();
            fusion.AddAuthentication().AddBlazor();
        }
    }
}
