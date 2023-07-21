using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Pages.Landing;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class BlazorUIAppModule : HostModule, IBlazorUIModule
{
    public static string ImportName => "blazorApp";

    public BlazorUIAppModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected override void InjectServices(IServiceCollection services)
    {
        var appKind = HostInfo.AppKind;
        if (!appKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddScoped<AppServiceStarter>(c => new AppServiceStarter(c));
        services.AddScoped<AppIconBadgeUpdater>(c => new AppIconBadgeUpdater(c));
        services.AddScoped<AutoNavigationUI>(c => new AppAutoNavigationUI(c));

        var fusion = services.AddFusion();
        fusion.AddService<AppPresenceReporter>(ServiceLifetime.Scoped);

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<LandingVideoModal.Model, LandingVideoModal>()
            .Add<SignInModal.Model, SignInModal>()
        );
    }
}
