using ActualChat.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.DependencyInjection;
using Stl.Plugins;

namespace ActualChat.Chat.Module;

public class ChatModule : HostModule
{
    public ChatModule(IPluginInfoProvider.Query _) : base(_) { }

    [ServiceConstructor]
    public ChatModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    { }
}
