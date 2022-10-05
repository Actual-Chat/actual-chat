using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Blazored.Modal;
using Blazored.Modal.Services;
using Blazored.SessionStorage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.Module;

public class BlazorUICoreModule : HostModule<BlazorUISettings>, IBlazorUIModule
{
    public static string ImportName => "ui";

    public BlazorUICoreModule(IPluginInfoProvider.Query _) : base(_) { }
    [ServiceConstructor]
    public BlazorUICoreModule(IPluginHost plugins) : base(plugins) { }

    public override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        if (!HostInfo.RequiredServiceScopes.Contains(ServiceScope.BlazorUI))
            return; // Blazor UI only module
        var isServerSideBlazor = HostInfo.RequiredServiceScopes.Contains(ServiceScope.Server);

        // Third-party Blazor components
        services.AddBlazoredSessionStorage();
        services.AddScoped<ModalService>();
        services.AddBlazorContextMenu(options =>
        {
            options.ConfigureTemplate(defaultTemplate =>
            {
                defaultTemplate.MenuClass += " context-menu";
                defaultTemplate.MenuItemClass += " context-menu-item";
                defaultTemplate.MenuListClass += " context-menu-list";
                defaultTemplate.SeparatorClass += " context-menu-separator";
            });

            options.ConfigureTemplate("horizontal",
                template => {
                    template.MenuClass = "blazor-context-menu--horizontal context-menu";
                    template.MenuItemClass = "blazor-context-menu__item--horizontal";
                    template.MenuListClass += " blazor-context-menu__list--horizontal context-menu-list";
                    template.SeparatorClass += " context-menu-separator";
                });
        });

        // TODO(AY): Remove ComputedStateComponentOptions.SynchronizeComputeState from default options
        ComputedStateComponent.DefaultOptions =
            ComputedStateComponentOptions.RecomputeOnParametersSet
            | ComputedStateComponentOptions.SynchronizeComputeState;

        // Fusion
        var fusion = services.AddFusion();
        fusion.AddBackendStatus();
        var fusionAuth = fusion.AddAuthentication().AddBlazor();
        // Default update delay is 0.2s
        services.AddTransient<IUpdateDelayer>(c => new UpdateDelayer(c.UIActionTracker(), 0.2));

        // Replace BlazorCircuitContext w/ AppBlazorCircuitContext
        services.AddScoped<BlazorCircuitContext, AppBlazorCircuitContext>();
        services.AddTransient(c => (AppBlazorCircuitContext)c.GetRequiredService<BlazorCircuitContext>());

        // Core UI-related services
        services.TryAddSingleton<IHostApplicationLifetime, BlazorHostApplicationLifetime>();
        services.AddScoped<DisposeMonitor>();

        // Settings
        services.AddSingleton<LocalSettings.Options>();
        services.AddScoped<LocalSettingsBackend>();
        services.AddScoped<LocalSettings>();
        services.AddScoped<AccountSettings>();

        if (isServerSideBlazor)
            services.AddScoped<TimeZoneConverter, ServerSideTimeZoneConverter>();
        else
            services.AddScoped<TimeZoneConverter, ClientSizeTimeZoneConverter>(); // WASM
        services.AddScoped<ComponentIdGenerator>();
        services.AddScoped<RenderVars>();

        // Misc. UI services
        services.AddScoped<UILifetimeEvents>();
        services.AddScoped<ClipboardUI>();
        services.AddScoped<UserInteractionUI>();
        services.AddScoped<FeedbackUI>();
        services.AddScoped<NavbarUI>();
        services.AddScoped<ImageViewerUI>();
        services.AddScoped<ErrorUI>();
        services.AddScoped<ModalUI>();
        services.AddScoped<ThemeUI>();
        services.AddScoped<KeepAwakeUI>();
        fusion.AddComputeService<SearchUI>(ServiceLifetime.Scoped);
        services.AddTransient<EscapistSubscription>();
        services.AddScoped<Escapist>();
        services.AddScoped<Func<EscapistSubscription>>(x => x.GetRequiredService<EscapistSubscription>);
        fusion.AddComputeService<ILiveTime, LiveTime>(ServiceLifetime.Scoped);
        services.AddScoped<LinkInfoBuilder>();
        services.AddScoped<NotificationNavigationHandler>();

        // UI events
        services.AddScoped<UIEventHub>();

        // Host-specific services
        services.TryAddScoped<IClientAuth, WebClientAuth>();
    }
}
