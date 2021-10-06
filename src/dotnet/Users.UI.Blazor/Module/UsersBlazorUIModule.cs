using ActualChat.Hosting;
using ActualChat.UI.Blazor;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
using Stl.Fusion.Authentication;
using Stl.Plugins;

namespace ActualChat.Users.UI.Blazor;

public class UsersBlazorUIModule : HostModule, IBlazorUIModule
{
    /// <inheritdoc />
    public static string ImportName => "users";

    public UsersBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public UsersBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        services.AddSingleton(_ => new PresenceService.Options() {
            UpdatePeriod = TimeSpan.FromSeconds(50),
        });
    }
}

