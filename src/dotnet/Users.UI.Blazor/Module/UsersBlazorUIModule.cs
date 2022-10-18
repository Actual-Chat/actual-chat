using ActualChat.Hosting;
using Stl.Plugins;

namespace ActualChat.Users.UI.Blazor.Module;

public class UsersBlazorUIModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "users";

    public UsersBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public UsersBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    { }
}
