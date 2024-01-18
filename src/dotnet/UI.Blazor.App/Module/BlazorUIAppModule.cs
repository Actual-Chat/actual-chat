using System.Diagnostics.CodeAnalysis;
using ActualChat.Chat.UI.Blazor;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.App.Pages.Landing;
using ActualChat.UI.Blazor.App.Pages.Test;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.UI.Blazor.App.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class BlazorUIAppModule(IServiceProvider moduleServices)
    : HostModule(moduleServices), IBlazorUIModule
{
    public static string ImportName => "blazorApp";

    protected override void InjectServices(IServiceCollection services)
    {
        var hostKind = HostInfo.HostKind;
        if (!hostKind.HasBlazorUI())
            return; // Blazor UI only module

        services.AddScoped<AppScopedServiceStarter>(c => new AppScopedServiceStarter(c));
        services.AddSingleton<AppNonScopedServiceStarter>(c => new AppNonScopedServiceStarter(c));
        services.AddScoped<AppIconBadgeUpdater>(c => new AppIconBadgeUpdater(c.ChatUIHub()));
        services.AddScoped<AutoNavigationUI>(c => new AppAutoNavigationUI(c.UIHub()));

        var fusion = services.AddFusion();
        fusion.AddService<AppPresenceReporter>(ServiceLifetime.Scoped);

        // IModalViews
        services.AddTypeMap<IModalView>(map => map
            .Add<LandingVideoModal.Model, LandingVideoModal>()
            .Add<PremiumFeaturesModal.Model, PremiumFeaturesModal>()
            .Add<SignInModal.Model, SignInModal>()
        );

        // Test Pages
        services.TryAddSingleton<IWebViewCrasher, NoopWebViewCrasher>();
    }
}
