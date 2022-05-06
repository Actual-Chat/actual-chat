using ActualChat.Hosting;
using ActualChat.UI.Blazor.Authorization;
using ActualChat.Users.UI.Blazor.Authorization;
using Microsoft.AspNetCore.Authorization;
using Stl.Plugins;

namespace ActualChat.Users.UI.Blazor.Module;

public class UsersBlazorUIModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "users";

    public UsersBlazorUIModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public UsersBlazorUIModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        // Presence
        services.AddSingleton(_ => new PresenceService.Options() {
            UpdatePeriod = TimeSpan.FromSeconds(50),
        });

        // Authorization
        services.AddScoped<IAuthorizationHandler, IsUserActiveRequirementHandler>();
        services.AddScoped<IAuthorizationHandler, IsAdminRequirementHandler>();
        services.Configure<AuthorizationOptions>(o => {
            o.AddPolicy(KnownPolicies.IsUserActive, builder => builder.AddRequirements(new IsUserActiveRequirement()));
            o.AddPolicy(KnownPolicies.IsAdmin, builder => builder.AddRequirements(new IsAdminRequirement()));
        });
    }
}
