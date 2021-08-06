using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Blobs.Client.Module
{
    public class BlobsClientModule : HostModule
    {
        public BlobsClientModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public BlobsClientModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (HostInfo.ServiceScope != ServiceScope.Client)
                return; // Client-side only module

            base.InjectServices(services);
        }
    }
}
