using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Chat.Client.Module
{
    public class ChatClientModule : HostModule
    {
        public ChatClientModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public ChatClientModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (HostInfo.ServiceScope != ServiceScope.Client)
                return; // Client-side only module
        }
    }
}
