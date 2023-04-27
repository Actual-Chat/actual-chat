using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Stl.Fusion.Bridge;
using Stl.Fusion.Bridge.Interception;
using Stl.Fusion.Diagnostics;

namespace ActualChat.UI.Blazor.Module;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public class BlazorUICoreModule : HostModule<BlazorUISettings>, IBlazorUIModule
{
    public static string ImportName => "ui";

    public BlazorUICoreModule(IServiceProvider services) : base(services) { }

    protected override void InjectServices(IServiceCollection services)
    {
        base.InjectServices(services);
        var appKind = HostInfo.AppKind;
        if (!appKind.HasBlazorUI())
            return; // Blazor UI only module

        // TODO(AY): Remove ComputedStateComponentOptions.SynchronizeComputeState from default options
        if (appKind.IsWasmApp())
            ComputedStateComponent.DefaultOptions =
                ComputedStateComponentOptions.RecomputeOnParametersSet;
        else
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
        services.AddScoped(c => new AppBlazorCircuitContext(c));
        services.AddTransient(c => (BlazorCircuitContext)c.GetRequiredService<AppBlazorCircuitContext>());
        services.AddTransient(c => c.GetRequiredService<AppBlazorCircuitContext>().Dispatcher);

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
        if (appKind.IsServer()) {
            services.AddScoped<TimeZoneConverter>(c => new ServerSideTimeZoneConverter(c));
            MomentClockSet.Default.ServerClock.Offset = TimeSpan.Zero;
        }
        else {
            services.AddScoped<TimeZoneConverter>(c => new ClientSizeTimeZoneConverter(c)); // WASM
            services.AddHostedService(c => new ServerTimeSync(c));
        }
        services.AddScoped<ComponentIdGenerator>(_ => new ComponentIdGenerator());
        services.AddScoped<RenderVars>(_ => new RenderVars());

        // UI events
        services.AddScoped<UIEventHub>(c => new UIEventHub(c));

        services.AddScoped<LoadingUI>(c => new LoadingUI(c));
        services.AddScoped<ClipboardUI>(c => new ClipboardUI(
            c.GetRequiredService<IJSRuntime>()));
        services.AddScoped<InteractiveUI>(c => new InteractiveUI(c));
        services.AddScoped<ErrorUI>(c => new ErrorUI(
            c.GetRequiredService<UIActionTracker>()));
        services.AddScoped<History>(c => new History(c));
        services.AddScoped<HistoryItemIdFormatter>(_ => new HistoryItemIdFormatter());
        services.AddScoped<ModalUI>(c => new ModalUI(c));
        services.AddScoped<BannerUI>(c => new BannerUI(c));
        services.AddScoped<FocusUI>(c => new FocusUI(
            c.GetRequiredService<IJSRuntime>()));
        services.TryAddScoped<KeepAwakeUI>(c => new KeepAwakeUI(c));
        services.AddScoped<DeviceAwakeUI>(c => new DeviceAwakeUI(c));
        services.AddScoped<UserActivityUI>(c => new UserActivityUI(c));
        services.AddScoped<Escapist>(c => new Escapist(
            c.GetRequiredService<IJSRuntime>()));
        services.AddScoped<TuneUI>(c => new TuneUI(c));
        services.AddScoped<VibrationUI>(c => new VibrationUI(c));
        fusion.AddComputeService<LiveTime>(ServiceLifetime.Scoped);

        // Actual Chat-specific UI services
        services.AddScoped<ThemeUI>(c => new ThemeUI(c));
        services.AddScoped<FeedbackUI>(c => new FeedbackUI(c));
        services.AddScoped<VisualMediaViewerUI>(c => new VisualMediaViewerUI(
            c.GetRequiredService<ModalUI>()));
        fusion.AddComputeService<AccountUI>(ServiceLifetime.Scoped);
        fusion.AddComputeService<SearchUI>(ServiceLifetime.Scoped);

        // Host-specific services
        services.TryAddScoped<IClientAuth>(c => new WebClientAuth(c));
        services.AddScoped<IRestartService, WebpageReloadService>(c => new WebpageReloadService(
            c.GetRequiredService<NavigationManager>()));

        InjectDiagnosticsServices(services);

        // Force app replica cache store flushing just after updating value for Users.IAccounts.GetOwn
        services.ConfigureAppReplicaCache(c =>
            c.ForceFlush(typeof(Users.IAccounts), nameof(Users.IAccounts.GetOwn)));

        // IModalViews
        services.AddTypeMapper<IModalView>(map => map
            .Add<FeatureRequestModal.Model, FeatureRequestModal>()
            .Add<VisualMediaViewerModal.Model, VisualMediaViewerModal>()
            .Add<DemandUserInteractionModal.Model, DemandUserInteractionModal>()
        );
        // IBannerViews
        services.AddTypeMapper<IBannerView>();

        // Temporarily disabled for WASM due to bad performance
        if (false && appKind.IsWasmApp()) {
            services.AddSingleton<AppReplicaCacheConfigurator>();
            services.AddSingleton<ReplicaCache>(c => {
                var store = new IndexedDbKeyValueStore(c).Start();
                var configurator = c.GetRequiredService<AppReplicaCacheConfigurator>();
                var options = new AppReplicaCache.Options(store) {
                    ShouldForceFlushAfterSet = configurator.ShouldForceFlushAfterSet,
                };
                return new AppReplicaCache(options, c);
            });
        }
    }

    private void InjectDiagnosticsServices(IServiceCollection services)
    {
        // Diagnostics
        var isDev = HostInfo.IsDevelopmentInstance;
        var appKind = HostInfo.AppKind;
        var isServer = appKind.IsServer();
        var isClient = appKind.IsClient();
        var isWasmApp = appKind.IsWasmApp();

        services.AddScoped(c => new DebugUI(c));

        if (isClient) {
            services.AddSingleton(c => new TaskMonitor(c));
            services.AddSingleton(c => new TaskEventListener(c));
        }
        services.AddSingleton(c => {
            return new FusionMonitor(c) {
                SleepPeriod = isDev ? TimeSpan.Zero : TimeSpan.FromMinutes(5).ToRandom(0.2),
                CollectPeriod = TimeSpan.FromSeconds(isDev ? 10 : 60),
                AccessFilter = isWasmApp
                    ? static computed => computed.Input.Function is IReplicaMethodFunction
                    : static _ => true,
                AccessStatisticsPreprocessor = StatisticsPreprocessor,
                RegistrationStatisticsPreprocessor = StatisticsPreprocessor,
            };

            void StatisticsPreprocessor(Dictionary<string, (int, int)> stats)
            {
                if (isServer) {
                    foreach (var key in stats.Keys.ToList()) {
                        if (key.OrdinalStartsWith("DbAuthService"))
                            continue;
                        if (key.OrdinalContains("Backend."))
                            continue;
                        stats.Remove(key);
                    }
                }
                else {
                    foreach (var key in stats.Keys.ToList()) {
                        if (key.OrdinalContains(".Pseudo"))
                            stats.Remove(key);
                        if (key.OrdinalStartsWith("FusionTime."))
                            stats.Remove(key);
                        if (key.OrdinalStartsWith("LiveTime."))
                            stats.Remove(key);
                        if (key.OrdinalStartsWith("LiveTimeDelta"))
                            stats.Remove(key);
                    }
                }
            }
        });
        if (isServer && (!isDev || Constants.DebugMode.ServerFusionMonitor)) // Auto-start FusionMonitor on server
            services.AddHostedService(c => c.GetRequiredService<FusionMonitor>());
    }
}
