using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Kvas;
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
        services.AddScoped<ModalService>(_ => new ModalService());

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
        services.AddScoped<ClientAuthHelper>(sp => new ClientAuthHelper(
            sp.GetRequiredService<IAuth>(),
            sp.GetRequiredService<ISessionResolver>(),
            sp.GetRequiredService<ICommander>(),
            sp.GetRequiredService<IJSRuntime>()));
        services.RemoveAll<PresenceReporter>(); // We replace it with our own one further

        // Default update delay is 0.2s
        services.AddTransient<IUpdateDelayer>(c => new UpdateDelayer(c.UIActionTracker(), 0.2));

        // Replace BlazorCircuitContext w/ AppBlazorCircuitContext + expose Dispatcher
        services.AddScoped<AppBlazorCircuitContext>(sp => new AppBlazorCircuitContext(sp));
        services.AddScoped(c => (BlazorCircuitContext)c.GetRequiredService<AppBlazorCircuitContext>());
        services.AddScoped(c => c.GetRequiredService<BlazorCircuitContext>().Dispatcher);

        // Core UI-related services
        services.TryAddSingleton<IHostApplicationLifetime>(_ => new BlazorHostApplicationLifetime());
        services.AddScoped<DisposeMonitor>(_ => new DisposeMonitor());
        services.AddScoped<BrowserInfo>(sp => new BrowserInfo(sp));

        // Settings
        services.AddSingleton<LocalSettings.Options>(_ => new LocalSettings.Options());
        services.AddScoped<LocalSettingsBackend>(sp => new LocalSettingsBackend(sp));
        services.AddScoped<LocalSettings>(sp => new LocalSettings(
            sp.GetRequiredService<LocalSettings.Options>(),
            sp.GetRequiredService<LocalSettingsBackend>(),
            sp.GetRequiredService<ILogger<LocalSettings>>()));
        services.AddScoped<AccountSettings>(sp => new AccountSettings(
            sp.GetRequiredService<IServerKvas>(),
            sp.GetRequiredService<Session>()));
        if (isServerSideBlazor)
            services.AddScoped<TimeZoneConverter>(sp => new ServerSideTimeZoneConverter(sp));
        else
            services.AddScoped<TimeZoneConverter>(sp => new ClientSizeTimeZoneConverter(sp)); // WASM
        services.AddScoped<ComponentIdGenerator>(_ => new ComponentIdGenerator());
        services.AddScoped<RenderVars>(sp => new RenderVars(
            sp.GetRequiredService<IStateFactory>()));

        // UI events
        services.AddScoped<LoadingUI>(sp => new LoadingUI(
            sp.GetRequiredService<ILogger<LoadingUI>>()));
        services.AddScoped<UILifetimeEvents>(sp => new UILifetimeEvents(
            sp.GetRequiredService<IEnumerable<Action<UILifetimeEvents>>>()));
        services.AddScoped<UIEventHub>(sp => new UIEventHub(sp));

        // General UI services
        services.AddScoped<ClipboardUI>(sp => new ClipboardUI(
            sp.GetRequiredService<IJSRuntime>()));
        services.AddScoped<InteractiveUI>(sp => new InteractiveUI(sp));
        services.AddScoped<ErrorUI>(sp => new ErrorUI(
            sp.GetRequiredService<UIActionTracker>()));
        services.AddScoped<HistoryUI>(sp => new HistoryUI(sp));
        services.AddScoped<ModalUI>(sp => new ModalUI(sp));
        services.AddScoped<BannerUI>(sp => new BannerUI(sp));
        services.AddScoped<FocusUI>(sp => new FocusUI(
            sp.GetRequiredService<IJSRuntime>()));
        services.AddScoped<Vibration>(sp => new Vibration(
            sp.GetRequiredService<IJSRuntime>()));
        services.AddScoped<KeepAwakeUI>(sp => new KeepAwakeUI(
            sp.GetRequiredService<IJSRuntime>()));
        services.AddScoped<UserActivityUI>(sp => new UserActivityUI(sp));
        services.AddScoped<Escapist>(sp => new Escapist(
            sp.GetRequiredService<IJSRuntime>()));
        services.AddScoped<Func<EscapistSubscription>>(x => x.GetRequiredService<EscapistSubscription>);
        fusion.AddComputeService<ILiveTime, LiveTime>(ServiceLifetime.Scoped);

        // Actual.chat-specific UI services
        services.AddScoped<ThemeUI>(sp => new ThemeUI(sp));
        services.AddScoped<FeedbackUI>(sp => new FeedbackUI(sp));
        services.AddScoped<ImageViewerUI>(sp => new ImageViewerUI(
            sp.GetRequiredService<ModalUI>()));
        fusion.AddComputeService<SearchUI>(ServiceLifetime.Scoped);

        // Misc. helpers
        services.AddScoped<NotificationNavigationHandler>(sp => new NotificationNavigationHandler(sp));

        // Host-specific services
        services.TryAddScoped<IClientAuth>(sp => new WebClientAuth(
            sp.GetRequiredService<ClientAuthHelper>()));

        services.ConfigureUILifetimeEvents(events => events.OnCircuitContextCreated += InitializeHistoryUI);
    }

    private void InitializeHistoryUI(IServiceProvider services)
        => _ = services.GetRequiredService<HistoryUI>();
}
