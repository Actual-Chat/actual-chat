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
        services.AddScoped<ModalService>(c => new ModalService(c));

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
        services.AddScoped<ClientAuthHelper>(c => new ClientAuthHelper(
            c.GetRequiredService<IAuth>(),
            c.GetRequiredService<ISessionResolver>(),
            c.GetRequiredService<ICommander>(),
            c.GetRequiredService<IJSRuntime>()));
        services.RemoveAll<PresenceReporter>(); // We replace it with our own one further

        // Default update delay is 0.2s
        services.AddTransient<IUpdateDelayer>(c => new UpdateDelayer(c.UIActionTracker(), 0.2));

        // Replace BlazorCircuitContext w/ AppBlazorCircuitContext + expose Dispatcher
        services.AddScoped<AppBlazorCircuitContext>(c => new AppBlazorCircuitContext(c));
        services.AddScoped(c => (BlazorCircuitContext)c.GetRequiredService<AppBlazorCircuitContext>());
        services.AddScoped(c => c.GetRequiredService<BlazorCircuitContext>().Dispatcher);

        // Core UI-related services
        services.TryAddSingleton<IHostApplicationLifetime>(_ => new BlazorHostApplicationLifetime());
        services.AddScoped<DisposeMonitor>(_ => new DisposeMonitor());
        services.AddScoped<BrowserInfo>(c => new BrowserInfo(c));

        // Settings
        services.AddSingleton<LocalSettings.Options>(_ => new LocalSettings.Options());
        services.AddScoped<LocalSettingsBackend>(c => new LocalSettingsBackend(c));
        services.AddScoped<LocalSettings>(c => new LocalSettings(
            c.GetRequiredService<LocalSettings.Options>(),
            c.GetRequiredService<LocalSettingsBackend>(),
            c.GetRequiredService<ILogger<LocalSettings>>()));
        services.AddScoped<AccountSettings>(c => new AccountSettings(
            c.GetRequiredService<IServerKvas>(),
            c.GetRequiredService<Session>()));
        if (isServerSideBlazor)
            services.AddScoped<TimeZoneConverter>(c => new ServerSideTimeZoneConverter(c));
        else
            services.AddScoped<TimeZoneConverter>(c => new ClientSizeTimeZoneConverter(c)); // WASM
        services.AddScoped<ComponentIdGenerator>(_ => new ComponentIdGenerator());
        services.AddScoped<RenderVars>(c => new RenderVars(
            c.GetRequiredService<IStateFactory>()));

        // UI events
        services.AddScoped<LoadingUI>(c => new LoadingUI(
            c.GetRequiredService<ILogger<LoadingUI>>()));
        services.AddScoped<UILifetimeEvents>(c => new UILifetimeEvents(
            c.GetRequiredService<IEnumerable<Action<UILifetimeEvents>>>()));
        services.AddScoped<UIEventHub>(c => new UIEventHub(c));

        // General UI services
        services.AddScoped<ClipboardUI>(c => new ClipboardUI(
            c.GetRequiredService<IJSRuntime>()));
        services.AddScoped<InteractiveUI>(c => new InteractiveUI(c));
        services.AddScoped<ErrorUI>(c => new ErrorUI(
            c.GetRequiredService<UIActionTracker>()));
        services.AddScoped<HistoryUI>(c => new HistoryUI(c));
        services.AddScoped<ModalUI>(c => new ModalUI(c));
        services.AddScoped<BannerUI>(c => new BannerUI(c));
        services.AddScoped<FocusUI>(c => new FocusUI(
            c.GetRequiredService<IJSRuntime>()));
        services.AddScoped<KeepAwakeUI>(c => new KeepAwakeUI(
            c.GetRequiredService<IJSRuntime>()));
        services.AddScoped<UserActivityUI>(c => new UserActivityUI(c));
        services.AddScoped<Escapist>(c => new Escapist(
            c.GetRequiredService<IJSRuntime>()));
        services.AddScoped<TuneUI>(c => new TuneUI(c));
        services.AddScoped<VibrationUI>(c => new VibrationUI(c));
        fusion.AddComputeService<ILiveTime, LiveTime>(ServiceLifetime.Scoped);
        fusion.AddComputeService<AuthUI>(ServiceLifetime.Scoped);

        // Actual.chat-specific UI services
        services.AddScoped<ThemeUI>(c => new ThemeUI(c));
        services.AddScoped<FeedbackUI>(c => new FeedbackUI(c));
        services.AddScoped<ImageViewerUI>(c => new ImageViewerUI(
            c.GetRequiredService<ModalUI>()));
        fusion.AddComputeService<SearchUI>(ServiceLifetime.Scoped);

        // Misc. helpers
        services.AddScoped<NotificationNavigationHandler>(c => new NotificationNavigationHandler(c));

        // Host-specific services
        services.TryAddScoped<IClientAuth>(c => new WebClientAuth(
            c.GetRequiredService<ClientAuthHelper>()));

        services.ConfigureUILifetimeEvents(events => events.OnCircuitContextCreated += InitializeHistoryUI);
    }

    private void InitializeHistoryUI(IServiceProvider services)
        => _ = services.GetRequiredService<HistoryUI>();
}
