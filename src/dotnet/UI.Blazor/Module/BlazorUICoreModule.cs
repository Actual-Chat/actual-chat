using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using Blazored.Modal.Services;
using Blazored.SessionStorage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Stl.Plugins;

namespace ActualChat.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
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

        // TODO(AY): Remove ComputedStateComponentOptions.SynchronizeComputeState from default options
        ComputedStateComponent.DefaultOptions =
            ComputedStateComponentOptions.RecomputeOnParametersSet
            | ComputedStateComponentOptions.SynchronizeComputeState;

        // Fusion
        var fusion = services.AddFusion();
        fusion.AddBackendStatus();
        fusion.AddBlazorUIServices();

        // Authentication
        fusion.AddAuthentication();
        services.AddScoped<ClientAuthHelper>();
        services.RemoveAll<PresenceReporter>(); // We replace it with our own one further
        services.AddScoped<AppPresenceReporter>();
        services.AddSingleton(_ => new AppPresenceReporter.Options() {
            AwayTimeout = Constants.Presence.AwayTimeout,
        });

        // Default update delay is 0.2s
        services.AddTransient<IUpdateDelayer>(c => new UpdateDelayer(c.UIActionTracker(), 0.2));

        // Replace BlazorCircuitContext w/ AppBlazorCircuitContext + expose Dispatcher
        services.AddScoped<AppBlazorCircuitContext>();
        services.AddScoped(c => (BlazorCircuitContext)c.GetRequiredService<AppBlazorCircuitContext>());
        services.AddScoped(c => c.GetRequiredService<BlazorCircuitContext>().Dispatcher);

        // Core UI-related services
        services.TryAddSingleton<IHostApplicationLifetime, BlazorHostApplicationLifetime>();
        services.AddScoped<DisposeMonitor>();
        services.AddScoped<BrowserInfo>();

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

        // UI events
        services.AddScoped<LoadingUI>();
        services.AddScoped<UILifetimeEvents>();
        services.AddScoped<UIEventHub>();

        // General UI services
        services.AddScoped<ClipboardUI>();
        services.AddScoped<InteractiveUI>();
        services.AddScoped<ErrorUI>();
        services.AddScoped<HistoryUI>();
        services.AddScoped<ModalUI>();
        services.AddScoped<FocusUI>();
        services.AddScoped<KeepAwakeUI>();
        services.AddScoped<UserActivityUI>();
        services.AddTransient<EscapistSubscription>();
        services.AddScoped<Escapist>();
        services.AddScoped<Func<EscapistSubscription>>(x => x.GetRequiredService<EscapistSubscription>);
        fusion.AddComputeService<ILiveTime, LiveTime>(ServiceLifetime.Scoped);

        // Actual.chat-specific UI services
        services.AddScoped<ThemeUI>();
        services.AddScoped<FeedbackUI>();
        services.AddScoped<ImageViewerUI>();
        fusion.AddComputeService<SearchUI>(ServiceLifetime.Scoped);

        // Misc. helpers
        services.AddScoped<LinkInfoBuilder>();
        services.AddScoped<NotificationNavigationHandler>();

        // Host-specific services
        services.TryAddScoped<IClientAuth, WebClientAuth>();

        services.ConfigureUILifetimeEvents(events => events.OnCircuitContextCreated += InitializeHistoryUI);
    }

    private void InitializeHistoryUI(IServiceProvider services)
        => _ = services.GetRequiredService<HistoryUI>();
}
