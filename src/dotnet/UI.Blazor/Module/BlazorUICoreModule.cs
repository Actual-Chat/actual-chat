using ActualChat.Hardware;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Pages.DiveInModalTestPage;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Client.Interception;
using ActualLab.Fusion.Diagnostics;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor.Module;

public sealed class BlazorUICoreModule(IServiceProvider moduleServices)
    : HostModule<BlazorUISettings>(moduleServices), IBlazorUIModule
{
    public static string ImportName => "ui";

    protected override void InjectServices(IServiceCollection services)
    {
        var hostKind = HostInfo.HostKind;
        var isServer = hostKind.IsServer();
        var isMauiApp = hostKind.IsMauiApp();

        // Just to test how it impacts the performance
        // FusionComponentBase.DefaultParameterComparisonMode = ParameterComparisonMode.Standard;

        // Fusion
        var fusion = services.AddFusion();
        fusion.AddBlazor();

        // Authentication
        // fusion.AddAuthClient();

        // Default update delay is 0.2s
        services.AddTransient<IUpdateDelayer>(c => new UpdateDelayer(c.UIActionTracker(), 0.2));

        // Alias UIHub -> CircuitHub, IDispatcherResolver + expose Dispatcher
        // services.AddScoped(c => new UIHub(c)); // BlazorUIAppModule does ~this
        services.AddAlias<CircuitHub, UIHub>(ServiceLifetime.Scoped);
        services.AddAlias<IDispatcherResolver, UIHub>(ServiceLifetime.Scoped);
        services.AddTransient(c => c.GetRequiredService<UIHub>().Dispatcher);

        // Core UI-related services
        if (!isServer)
            services.TryAddSingleton<IHostApplicationLifetime>(_ => new FakeHostApplicationLifetime());
        services.AddScoped(c => new BrowserInit(c.UIHub()));
        services.AddScoped(c => new CaptchaUI(c.UIHub()));
        services.AddScoped(c => new BrowserInfo(c.UIHub()));
        services.AddScoped(c => new WebShareInfo(c.UIHub()));
        services.AddScoped(c => new FileDownloadUI(c.UIHub()));
        services.AddScoped(_ => new ComponentIdGenerator());
        services.AddScoped(_ => new RenderVars());

        // Settings
        services.AddScoped(c => new LocalStorage(c.JSRuntime()));
        services.AddSingleton(_ => new LocalSettings.Options() {
            StoreFactory = c => new BatchingKvas(new BatchingKvas.Options(), c) {
                Backend = new WebKvasBackend($"{ImportName}.localSettings", c),
            }.Start(),
        });
        services.AddScoped(c => new LocalSettings(c.GetRequiredService<LocalSettings.Options>(), c));
        services.AddScoped(c => new UserSettingsUI(c, c.Session()));
        services.AddSingleton(_ => new ServerClockSyncStats());
        if (isServer) {
            services.AddScoped<DateTimeConverter>(c => new ServerSideDateTimeConverter(c));
            MomentClockSet.Default.ServerClock.Offset = TimeSpan.Zero;
        }
        else
            services.AddScoped<DateTimeConverter>(c => new ClientSizeDateTimeConverter(c)); // WASM & MAUI
        // ServerTimeSync needs the IJSRuntime bound to the active scope:
        // - Server: the per-circuit IJSRuntime;
        // - MAUI: the per-WebView-page IJSRuntime — the root (non-scoped) one isn't attached to a
        //   WebView, so its JS calls throw "Cannot invoke JavaScript outside of a WebView context".
        // Both start the background loop from AppScopedServiceStarter.AfterFirstRender, once JS is ready;
        // EnsureSynced() additionally forces an immediate sync before the first recording.
        // WASM has a single, always-ready IJSRuntime, so a hosted service is fine there.
        if (isServer || isMauiApp)
            services.AddScoped(c => new ServerTimeSync(c));
        else {
            // Singleton + hosted alias, not AddHostedService alone: that registers only
            // IHostedService, so GetService<ServerTimeSync>() returned null in WASM and
            // every EnsureSynced call site silently skipped the sync.
            services.AddSingleton(c => new ServerTimeSync(c));
            services.AddHostedService(c => c.GetRequiredService<ServerTimeSync>());
        }
        services.AddScoped(c => new FontSizeUI(c.UIHub()));

        // UI events
        services.AddScoped(c => new UIEventHub(c));

        // UI services
        services.AddScoped(c => new LoadingUI(c.UIHub()));
        services.AddScoped(c => new ReconnectUI(c.UIHub()));
        if (!isServer)
            services.AddSingleton<RpcClientPeerReconnectDelayer>(c => new AppRpcClientPeerReconnectDelayer(c));
        services.AddScoped(c => new ReloadUI(c));
        if (isServer)
            services.AddScoped<ConnectivityUI>(c => new ServerConnectivityUI(c.UIHub()));
        else if (!isMauiApp) // MauiConnectivityUI is registered in MauiApp
            services.AddScoped<ConnectivityUI>(c => new WebConnectivityUI(c.UIHub()));
        if (!isMauiApp) {
            services.AddScoped<BackgroundStateTracker>(c => new WebBackgroundStateTracker(c));
            services.AddScoped<ThermalTracker>(c => new WebThermalTracker(c));
            services.AddScoped<AudioFocusUI>(_ => new AudioFocusUI());
            services.AddScoped<TuneUI>(c => new WebTuneUI(c.UIHub()));
        }
        services.AddScoped(c => new ClipboardUI(c.UIHub()));
        services.AddScoped(c => new ExternalUrlOpener(c.UIHub()));
        services.AddScoped(c => new ExternalMapOpener(c.UIHub()));
        services.AddScoped(c => new InteractiveUI(c.UIHub()));
        services.AddScoped(c => new AutoNavigationUI(c.UIHub()));
        services.AddScoped(_ => new AppNavigationQueue.ContainerDisposalTracker());
        services.AddScoped(c => new History(c.UIHub()));
        services.AddScoped(c => new HistoryStepper(c));
        services.AddScoped(_ => new HistoryItemIdFormatter());
        services.AddScoped(c => new ModalUI(c.UIHub()));
        services.AddScoped(c => new BannerUI(c.UIHub()));
        services.AddScoped(c => new FocusUI(c.UIHub()));
        services.AddScoped(c => new KeepAwakeUI(c.UIHub()));
        services.AddScoped(c => new DeviceAwakeUI(c.UIHub()));
        services.AddScoped(c => (ISleepDurationProvider)c.GetRequiredService<DeviceAwakeUI>());
        services.AddScoped(c => new UserActivityUI(c.UIHub()));
        services.AddScoped(c => new BubbleUI(c.UIHub()));
        services.AddScoped(c => new ShareUI(c.UIHub()));
        services.AddScoped(_ => new ToastUI());
        services.AddScoped(c => new ThemeUI(c.UIHub()));
        services.AddScoped(c => new VisualMediaViewerUI(c.UIHub()));
        services.AddScoped(_ => new BlazorAppLifecycle());

        // Uploads
        services.AddScoped<IFileUploader, WebSourceUploader>();
        if (isMauiApp)
            services.AddScoped<IFileUploader, StreamRpcUploader>();
        else
            services.AddScoped<IFileUploader, StreamUploader>();
        services.AddScoped<FileUploader>();

        // Fusion-based UI services
        if (hostKind == HostKind.Server)
            services.AddScoped(_ => FakeTemporals.Instance);
        else
            fusion.AddService<Temporals, RealTemporals>(ServiceLifetime.Scoped);
        fusion.AddService<LiveTime>(ServiceLifetime.Scoped);
        fusion.AddService<AccountUI>(ServiceLifetime.Scoped);
        fusion.AddService<TotpUI>(ServiceLifetime.Scoped);
        fusion.AddService<LogUI>(ServiceLifetime.Scoped);
        services.AddScoped(c => new ReportUI(c.UIHub()));

        // Host-specific services
        services.AddScoped<SessionTokens>(c => new SessionTokens(c.UIHub()));

        InjectDiagnosticsServices(services);

        // IModalViews
        services.AddTypeMapper<IModalView>(map => map
            .Add<VisualMediaViewerModal.Model, VisualMediaViewerModal>()
            .Add<VisualMediaInfoModal.Model, VisualMediaInfoModal>()
            .Add<DemandUserInteractionModal.Model, DemandUserInteractionModal>()
            .Add<DiveInModal.Model, DiveInModal>()
            .Add<ConfirmModal.Model, ConfirmModal>()
        );
        // IBannerViews
        services.AddTypeMapper<IBannerView>();
        // IEmbeddedViews
        services.AddTypeMapper<IEmbeddedView>();

        // RemoteComputedCache
        if (hostKind.IsWasmApp() && !HostInfo.IsTested) {
            services.AddSingleton(_ => new WebRemoteComputedCache.Options());
            services.AddSingleton<IRemoteComputedCache>(c => {
                var options = c.GetRequiredService<WebRemoteComputedCache.Options>();
                return new WebRemoteComputedCache(options, c);
            });
        }
    }

    private void InjectDiagnosticsServices(IServiceCollection services)
    {
        // Diagnostics
        var isDev = HostInfo.IsDevelopmentInstance;
        var hostKind = HostInfo.HostKind;
        var isApp = hostKind.IsApp();
        var isWasmApp = hostKind.IsWasmApp();
        var isServer = hostKind.IsServer();

        services.AddScoped(c => new DebugUI(c.UIHub()));

        if (isApp) {
            services.AddSingleton(c => new TaskMonitor(c));
            services.AddSingleton(c => new TaskEventListener(c));
        }
        services.AddSingleton(c => {
            return new FusionMonitor(c) {
                SleepPeriod = isDev ? TimeSpan.Zero : TimeSpan.FromMinutes(5).ToRandom(0.2),
                CollectPeriod = TimeSpan.FromSeconds(isDev ? 10 : 60),
                AccessFilter = isWasmApp
                    ? static computed => computed.Input.Function is RemoteComputeMethodFunction
                    : static _ => true,
                AccessStatisticsPreprocessor = StatisticsPreprocessor,
                RegistrationStatisticsPreprocessor = StatisticsPreprocessor,
            };

            void StatisticsPreprocessor(Dictionary<string, (int, int)> stats)
            {
                if (isServer) {
                    foreach (var key in stats.Keys.ToList()) {
                        if (key.StartsWith("DbAuthService"))
                            continue;
                        if (key.Contains("Backend."))
                            continue;
                        stats.Remove(key);
                    }
                }
                else {
                    foreach (var key in stats.Keys.ToList()) {
                        if (key.Contains(".Pseudo"))
                            stats.Remove(key);
                        if (key.StartsWith("FusionTime."))
                            stats.Remove(key);
                        if (key.StartsWith("LiveTime."))
                            stats.Remove(key);
                        if (key.StartsWith("LiveTimeDelta"))
                            stats.Remove(key);
                    }
                }
            }
        });
        if (isServer && (!isDev || Constants.DebugMode.ServerFusionMonitor)) // Auto-start FusionMonitor on server
            services.AddHostedService(c => c.GetRequiredService<FusionMonitor>());
    }
}
