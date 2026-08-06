using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class AppScopedServiceStarter
{
    private volatile string? _sessionHash;

    private AppUIHub Hub { get; }
    private Tracer Tracer { get; }
    private HostInfo HostInfo => Hub.HostInfo;
    private History History => Hub.History;
    private AutoNavigationUI AutoNavigationUI => Hub.AutoNavigationUI;
    private LoadingUI LoadingUI => Hub.LoadingUI;
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public AppScopedServiceStarter(AppUIHub hub)
    {
        Hub = hub;
        Tracer = Hub.TracerFor(GetType());
    }

    public async Task PrepareFirstRender(string sessionHash)
    {
        var oldSessionHash = Interlocked.CompareExchange(ref _sessionHash, sessionHash, null);
        if (oldSessionHash == sessionHash) {
            Log.LogError("{Method} is called more than once", nameof(PrepareFirstRender));
            return; // Already prepared
        }

        if (oldSessionHash is not null)
            throw StandardError.Internal("Session hash is already set.");

        // Starts in Blazor dispatcher
        using var _1 = Tracer.MethodRegion();
        try {
            var baseUri = HostInfo.BaseUrl;

            // Creating core services - this should be done as early as possible
            var browserInfo = Hub.BrowserInfo;
            var browserInit = Hub.Services.GetRequiredService<BrowserInit>();
            _ = browserInit.Initialize(
                HostInfo.HostKind,
                HostInfo.AppKind,
                ApiConstants.VersionString,
                baseUri,
                sessionHash,
                AppConstants.Instance,
                browserInfo.BlazorRef,
                browserInfo.ClipboardHandlersRef);
            var rightPanelStoredState = Hub.Services.GetRequiredService<RightPanelStoredState>();

            // Start AccountUI & UIEventHub
            _ = Hub.AccountUI; // Touch
            _ = Hub.UIEventHub; // Touch
            _ = Hub.ChatUI; // Touch

            // Awaiting completion of initialization tasks.
            // NOTE(AY): It's fine to use .ConfigureAwait(false) below this point,
            //           coz tasks were started on the Dispatcher thread already.

            // Finishing w/ BrowserInfo
            await browserInfo.WhenReady.ConfigureAwait(false);
            // ReSharper disable once ExplicitCallerInfoArgument
            Tracer.Point("BrowserInfo is ready");

            Hub.Services.GetRequiredService<ThemeUI>().Start();
            var dateTimeConverter = Hub.DateTimeConverter;
            if (dateTimeConverter is ServerSideDateTimeConverter serverSideDateTimeConverter)
                serverSideDateTimeConverter.Initialize(browserInfo.UtcOffset);

            // Finishing with BrowserInit
            await browserInit.WhenInitialized.ConfigureAwait(false); // Must be completed before the next call
            // ReSharper disable once ExplicitCallerInfoArgument
            Tracer.Point("BrowserInit completed");

            // Finishing with AccountUI
            await Hub.AccountUI.WhenReady.ConfigureAwait(false);
            await Hub.ChatUI.RestoreNavbarSelectedGroup().ConfigureAwait(false);

            // Finishing with auto-navigation & History init
            var url = await AutoNavigationUI.GetAutoNavigationUrl().ConfigureAwait(false);
            // Instantiate PanelsUI to register correspondent history states for left and right panels.
            // It's necessary to make a first history step always has BackStepCount == 0.
            if (browserInfo.ScreenSize.Value.IsWide())
                // Ensure that the right panel state is preloaded.
                await rightPanelStoredState.WhenRead.ConfigureAwait(false);
            _ = Hub.PanelsUI; // Touch
            if (url.IsChat() && browserInfo.ScreenSize.Value.IsNarrow()) {
                // We have to open chat root first - to make sure "Back" leads to it
                await History.Initialize(Links.Chats).ConfigureAwait(false);
                await AutoNavigationUI
                    .DispatchNavigateTo(url, AutoNavigationReason.SecondAutoNavigation)
                    .ConfigureAwait(false);
            }
            else
                await History.Initialize(url).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(PrepareFirstRender)} failed");
            throw;
        }
        finally {
            LoadingUI.MarkLoaded();
        }
    }

    public async Task AfterFirstRender(CancellationToken cancellationToken)
    {
        // Starts in Blazor dispatcher
        try {
            await LoadingUI.WhenRendered.WaitAsync(cancellationToken).ConfigureAwait(true);
            _ = Hub.OnboardingUI.TryShow();
            var hostKind = HostInfo.HostKind;
            var baseDelay = TimeSpan.FromSeconds(hostKind.IsServer() ? 0.25 : 1);

            // Starting less important UI services
            await Task.Delay(baseDelay, cancellationToken).ConfigureAwait(false);
            if (hostKind.IsServer() || hostKind.IsMauiApp())
                // Server & MAUI register ServerTimeSync scoped (bound to the per-circuit / per-WebView-page
                // IJSRuntime), not hosted, so its background sync loop must be started here — once first
                // render guarantees JS is ready (see BlazorUICoreModule).
                Hub.Services.GetService<ServerTimeSync>()?.Start();
            Hub.Services.GetRequiredService<SessionTokens>().Start();
            Hub.Services.GetRequiredService<BackgroundActivityUI>().Start();
            Hub.Services.GetRequiredService<ConnectivityUI>().Start();
            Hub.Services.GetRequiredService<ReconnectUI>().Start();
            _ = Hub.AudioFocusUI.WarmUp(); // Pre-initialize audio HAL for faster first recording
            _ = Hub.TuneUI; // Touch. Auto-starts on construction
            _ = Hub.AudioWidget; // Touch. Auto-starts on construction
            _ = Hub.VideoQualityUI; // Touch. Constructor calls Start(); chains gate on first video activity.
            Hub.Services.GetRequiredService<ThrottledTranslations>().Start();
            if (!HostInfo.IsProductionInstance)
                Hub.Services.GetRequiredService<DebugUI>();

            await Task.Delay(baseDelay * 2, cancellationToken).ConfigureAwait(false);
            Hub.AudioInitializer.StartInitialization();
            Hub.Services.GetRequiredService<AppPresenceReporter>().Start();
            Hub.Services.GetRequiredService<LiveLocationReporter>().Start();
            Hub.Services.GetRequiredService<AppIconBadgeUpdater>().Start();
            Hub.Services.GetRequiredService<NotificationReconciler>().Start();
            if (hostKind.IsApp())
                await StartHostedServices().ConfigureAwait(false);

            await ConfigureDataCollection(cancellationToken).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            Hub.Services.GetRequiredService<ContactSync>().Start();
            _ = Hub.SendingMessages; // Touch
            _ = Hub.UploadSessions; // Touch
            _ = Hub.LogUI; // Touch
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"{nameof(AfterFirstRender)} failed");
            throw;
        }
    }

    // Private methods

    private async Task ConfigureDataCollection(CancellationToken cancellationToken)
    {
        var dataCollectionSettingsUI = Hub.Services.GetRequiredService<IDataCollectionSettingsUI>();
        if (await dataCollectionSettingsUI.IsConfigured(cancellationToken).ConfigureAwait(false))
            return;

        var settings = await Hub.UserSettingsUI.UserAppSettings().Get(cancellationToken).ConfigureAwait(false);
        var isDataCollectionEnabled = settings.IsDataCollectionEnabled;
        if (!isDataCollectionEnabled.HasValue)
            return;

        await dataCollectionSettingsUI.UpdateState(isDataCollectionEnabled.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartHostedServices()
    {
        using var _ = Tracer.MethodRegion();
        var tracePrefix = nameof(StartHostedServices) + ": starting ";
        foreach (var hostedService in Hub.Services.HostedServices()) {
            Tracer.Point(tracePrefix + hostedService.GetType().Name);
            await hostedService.StartAsync(default).ConfigureAwait(true);
            await Task.Yield();
        }
    }
}
