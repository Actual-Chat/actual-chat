using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Chat.UI.Blazor.Module
{
    public class ChatBlazorUIModule: HostModule, IBlazorUIModule
    {
        public ChatBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public ChatBlazorUIModule(IPluginHost plugins) : base(plugins) { }
    }
}
