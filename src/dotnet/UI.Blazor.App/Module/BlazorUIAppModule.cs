using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class BlazorUIAppModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "blazorApp";

    public BlazorUIAppModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        if (!HostInfo.AppKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddScoped<AppServiceStarter>(c => new AppServiceStarter(c));
        services.AddScoped<SignOutReloader>(c => new SignOutReloader(c));
        services.AddScoped<AutoNavigationUI>(c => new AppAutoNavigationUI(c));

        var fusion = services.AddFusion();
        fusion.AddComputeService<AppPresenceReporter>(ServiceLifetime.Scoped);
        services.AddSingleton(_ => new AppPresenceReporter.Options {
            AwayTimeout = Constants.Presence.AwayTimeout,
        });
    }
}
