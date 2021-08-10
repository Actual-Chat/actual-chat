using ActualChat.Hosting;
using Blazorise;
using Blazorise.Bootstrap;
using Blazorise.Icons.FontAwesome;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Fusion.Blazor;
using Stl.Fusion.Extensions;
using Stl.Fusion.UI;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.Module
{
    public class BlazorUICoreModule : HostModule, IBlazorUIModule
    {
        public BlazorUICoreModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public BlazorUICoreModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
                return; // Blazor UI only module

            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var fusion = services.AddFusion();
            var fusionAuth = fusion.AddAuthentication();
            fusionAuth.AddBlazor();

            // Blazorise
            services.AddBlazorise().AddBootstrapProviders().AddFontAwesomeIcons();

            // Other UI-related services
            fusion.AddFusionTime();

            // Default update delay is 0.5s
            services.AddTransient<IUpdateDelayer>(c => new UpdateDelayer(c.UICommandTracker(), 0.5));
        }
    }
}
