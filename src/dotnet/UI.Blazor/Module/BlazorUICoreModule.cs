using System.Diagnostics.CodeAnalysis;
using ActualChat.Hardware;
using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Diagnostics;
using ActualChat.UI.Blazor.Pages.ComputeStateTestPage;
using ActualChat.UI.Blazor.Pages.DiveInModalTestPage;
using ActualChat.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ActualLab.Fusion.Client.Caching;
using ActualLab.Fusion.Client.Interception;
using ActualLab.Fusion.Diagnostics;

namespace ActualChat.UI.Blazor.Module;

#pragma warning disable IL2026 // Fine for modules

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
public sealed class BlazorUICoreModule(IServiceProvider moduleServices)
    : HostModule<BlazorUISettings>(moduleServices), IBlazorUIModule
{
    public static string ImportName => "ui";

    protected override void InjectServices(IServiceCollection services)
    {
        var hostKind = HostInfo.HostKind;

        // Just to test how it impacts the performance
        // FusionComponentBase.DefaultParameterComparisonMode = ParameterComparisonMode.Standard;

        // Fusion
        var fusion = services.AddFusion();
        fusion.AddBlazor();

        // Authentication
        // fusion.AddAuthClient();

        // Default update delay is 0.2s
        services.AddTransient<IUpdateDelayer>(c => new UpdateDelayer(c.UIActionTracker(), 0.2));

        // Replace BlazorCircuitContext w/ AppBlazorCircuitContext + expose Dispatcher
        services.AddScoped(c => new AppBlazorCircuitContext(c));
        services.AddTransient(c => (IDispatcherResolver)c.GetRequiredService<AppBlazorCircuitContext>());
        services.AddTransient(c => (BlazorCircuitContext)c.GetRequiredService<AppBlazorCircuitContext>());
        services.AddTransient(c => c.GetRequiredService<AppBlazorCircuitContext>().Dispatcher);

        // Core UI-related services
        services.AddScoped(c => new UIHub(c));
        services.AddAlias<Hub, UIHub>(ServiceLifetime.Scoped); // Required for PermissionHandler descendants
        if (!hostKind.IsServer())
            services.TryAddSingleton<IHostApplicationLifetime>(_ => new FakeHostApplicationLifetime());
        if (hostKind.IsApp())
            services.AddSingleton(_ => new BlazorRenderMode()); // No-op on the client
        else
            services.AddScoped(_ => new BlazorRenderMode());
        services.AddScoped(c => new BrowserInit(c));
        services.AddScoped(c => new CaptchaUI(c.UIHub()));
        services.AddScoped(c => new TotpUI(c.UIHub()));
        services.AddScoped(c => new BrowserInfo(c.UIHub()));
        services.AddScoped(c => new WebShareInfo(c));
        services.AddScoped(_ => new ComponentIdGenerator());
        services.AddScoped(_ => new RenderVars());

        // Settings
        services.AddSingleton(_ => new LocalSettings.Options() {
            BackendFactory = c => new WebKvasBackend($"{ImportName}.localSettings", c),
        });
        services.AddScoped(c => new LocalSettings(c.GetRequiredService<LocalSettings.Options>(), c));
        services.AddScoped(c => c.AccountSettings(c.Session()));
        services.AddScoped(c => c.ServerSettingsKvasClient(c.Session()));
        if (hostKind.IsServer()) {
            services.AddScoped<DateTimeConverter>(c => new ServerSideDateTimeConverter(c));
            MomentClockSet.Default.ServerClock.Offset = TimeSpan.Zero;
        }
        else {
            services.AddScoped<DateTimeConverter>(c => new ClientSizeDateTimeConverter(c)); // WASM
            services.AddHostedService(c => new ServerTimeSync(c));
        }
        services.AddScoped(c => new FontSizeUI(c.UIHub()));

        // UI events
        services.AddScoped(c => new UIEventHub(c));

        // UI services
        services.AddScoped(c => new LoadingUI(c));
        services.AddScoped(c => new ReconnectUI(c.UIHub()));
        services.AddScoped(c => new ReloadUI(c));
        if (hostKind.IsMauiApp())
            services.AddSingleton<BackgroundStateTracker>(c => new MauiBackgroundStateTracker(c));
        else
            services.AddScoped<BackgroundStateTracker>(c => new WebBackgroundStateTracker(c));
        services.AddScoped(c => new ClipboardUI(c.GetRequiredService<IJSRuntime>()));
        services.AddScoped(c => new InteractiveUI(c.UIHub()));
        services.AddScoped(c => new History(c.UIHub()));
        services.AddScoped(c => new HistoryStepper(c));
        services.AddScoped(_ => new HistoryItemIdFormatter());
        services.AddScoped(c => new ModalUI(c));
        services.AddScoped(c => new BannerUI(c.UIHub()));
        services.AddScoped(c => new FocusUI(c.GetRequiredService<IJSRuntime>()));
        services.AddScoped(c => new KeepAwakeUI(c));
        services.AddScoped(c => new DeviceAwakeUI(c.UIHub()));
        services.AddScoped(c => (ISleepDurationProvider)c.GetRequiredService<DeviceAwakeUI>());
        services.AddScoped(c => new UserActivityUI(c.UIHub()));
        services.AddScoped(c => new Escapist(c.GetRequiredService<IJSRuntime>()));
        services.AddScoped(c => new TuneUI(c));
        services.AddScoped(c => new BubbleUI(c.UIHub()));
        services.AddScoped(c => new ShareUI(c.UIHub()));
        services.AddScoped(_ => new ToastUI());
        fusion.AddService<LiveTime>(ServiceLifetime.Scoped);

        // Actual Chat-specific UI services
        services.AddScoped(c => new ThemeUI(c.UIHub()));
        services.AddScoped(c => new VisualMediaViewerUI(c.UIHub()));
        fusion.AddService<AccountUI>(ServiceLifetime.Scoped);

        // Host-specific services
        services.AddScoped<IClientAuth>(c => new WebAuth(c.UIHub()));
        services.AddScoped<SessionTokens>(c => new SessionTokens(c.UIHub()));

        InjectDiagnosticsServices(services);

        // IModalViews
        services.AddTypeMapper<IModalView>(map => map
            .Add<VisualMediaViewerModal.Model, VisualMediaViewerModal>()
            .Add<DemandUserInteractionModal.Model, DemandUserInteractionModal>()
            .Add<DiveInModal.Model, DiveInModal>()
            .Add<ConfirmModal.Model, ConfirmModal>()
        );
        // IBannerViews
        services.AddTypeMapper<IBannerView>();

        // RemoteComputedCache
        if (hostKind.IsWasmApp() && !HostInfo.IsTested) {
            services.AddSingleton(_ => new WebClientComputedCache.Options());
            services.AddSingleton<IRemoteComputedCache>(c => {
                var options = c.GetRequiredService<WebClientComputedCache.Options>();
                return new WebClientComputedCache(options, c);
            });
        }

        // validation
        services.AddScoped<AsyncValidator>();
        services.AddScoped<ValidationModelStore>();

        // Test services
        if (HostInfo.IsDevelopmentInstance)
            fusion.AddService<ComputeStateTestService>(ServiceLifetime.Scoped);
    }

    private void InjectDiagnosticsServices(IServiceCollection services)
    {
        // Diagnostics
        var isDev = HostInfo.IsDevelopmentInstance;
        var hostKind = HostInfo.HostKind;
        var isApp = hostKind.IsApp();
        var isWasmApp = hostKind.IsWasmApp();
        var isServer = hostKind.IsServer();

        services.AddScoped(c => new DebugUI(c));

        if (isApp) {
            services.AddSingleton(c => new TaskMonitor(c));
            services.AddSingleton(c => new TaskEventListener(c));
        }
        services.AddSingleton(c => {
            return new FusionMonitor(c) {
                SleepPeriod = isDev ? TimeSpan.Zero : TimeSpan.FromMinutes(5).ToRandom(0.2),
                CollectPeriod = TimeSpan.FromSeconds(isDev ? 10 : 60),
                AccessFilter = isWasmApp
                    ? static computed => computed.Input.Function is IRemoteComputeMethodFunction
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
