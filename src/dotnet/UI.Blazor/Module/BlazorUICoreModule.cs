using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Blazored.Modal;
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
        services.AddBlazoredModal();
        services.AddBlazorContextMenu(options =>
        {
            options.ConfigureTemplate(defaultTemplate =>
            {
                defaultTemplate.MenuCssClass = "context-menu";
                defaultTemplate.MenuItemCssClass = "context-menu-item";
                defaultTemplate.MenuListCssClass = "context-menu-list";
                defaultTemplate.SeperatorCssClass = "context-menu-separator";
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
        services.AddSingleton<ContentUrlMapper>();

        // Misc. UI services
        services.AddScoped<LinkInfoBuilder>();
        services.AddScoped<ClipboardUI>();
        services.AddScoped<UserInteractionUI>();
        services.AddScoped<FeedbackUI>();
        services.AddScoped<NavbarUI>();
        services.AddScoped<ImagePreviewUI>();
        services.AddScoped<ErrorUI>();
        services.AddScoped<ModalUI>();
        services.AddScoped<ThemeUI>();
        services.AddScoped<KeepAwakeUI>();
        fusion.AddComputeService<SearchUI>(ServiceLifetime.Scoped);
        services.AddTransient<EscapistSubscription>();
        services.AddScoped<Escapist>();
        services.AddScoped<Func<EscapistSubscription>>(x => x.GetRequiredService<EscapistSubscription>);
        fusion.AddComputeService<ILiveTime, LiveTime>(ServiceLifetime.Scoped);

        // UI events
        services.AddScoped<IGlobalEvents, GlobalEvents>();

        // Host-specific services
        services.TryAddScoped<IClientAuth, WebClientAuth>();
    }
}
