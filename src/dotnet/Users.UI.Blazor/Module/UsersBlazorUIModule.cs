using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Users.UI.Blazor.Services;
using Stl.Plugins;

namespace ActualChat.Users.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class UsersBlazorUIModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "users";

    public UsersBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public UsersBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module

        var fusion = services.AddFusion();

        // Account UI
        fusion.AddComputeService<AccountUI>(ServiceLifetime.Scoped);
    }
}
