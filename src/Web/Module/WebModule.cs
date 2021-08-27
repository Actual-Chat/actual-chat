using ActualChat.Hosting;
using ActualChat.Web.Internal;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Plugins;

namespace ActualChat.Web.Module
{
    public class WebModule : HostModule
    {
        public WebModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public WebModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server))
                return; // Server-side only module

            var fusion = services.AddFusion();
            var isDevelopmentInstance = HostInfo.IsDevelopmentInstance;
            var mvcBuilder = services.AddMvcCore(options => {
                options.ModelBinderProviders.Insert(0, new RangeModelBinderProvider());
            });
        }
    }
}
