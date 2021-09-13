using ActualChat.Chat.UI.Blazor.Testing;
using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion;
using Stl.Plugins;

namespace ActualChat.Chat.UI.Blazor.Module
{
    public class ChatBlazorUIModule: HostModule, IBlazorUIModule
    {
        public string[] CssPaths => new [] {"css/chat-page.css"};

        public ChatBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
        [ServiceConstructor]
        public ChatBlazorUIModule(IPluginHost plugins) : base(plugins) { }

        public override void InjectServices(IServiceCollection services)
        {
            if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
                return; // Blazor UI only module

            var fusion = services.AddFusion();
            fusion.AddComputeService<TestListService>();
        }
    }
}
