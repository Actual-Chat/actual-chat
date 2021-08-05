using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Storage.Module
{
    public class StorageModule : HostModule
    {
        public StorageModule(IPluginInfoProvider.Query _) : base(_) { }

        [ServiceConstructor]
        public StorageModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (HostInfo.ServiceScope != ServiceScope.Server)
                return; // Server-side only module

            base.InjectServices(services);
            
            var settings = services.BuildServiceProvider().GetRequiredService<StorageSettings>();
            if (settings.StorageType == "disk") services.AddTransient<LocalAudioBlobStorage>();
        }
    }
}