using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class AppScopedServiceStarter
{
    private string? _sessionHash;

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

            // Must happen before anything renders: the string localizer reads it synchronously,
            // and a component rendered before it lands stays English until it re-renders.
            // A headless scope never gets here, so its LocalizationUI keeps the English default.
            Hub.LocalizationUI.SetLanguage(browserInfo.UILanguage);
            Hub.LanguageUI.Start();
            Hub.Services.GetRequiredService<ThemeUI>().Start();
            var dateTimeConverter = Hub.DateTimeConverter;
            if (dateTimeConverter is ServerSideDateTimeConverter serverSideDateTimeConverter)
                serverSideDateTimeConverter.Initialize(browserInfo.UtcOffset);

            // Finishing with BrowserInit
            await browserInit.WhenInitialized.ConfigureAwait(false); // Must be completed before the next call
            // ReSharper disable once ExplicitCallerInfoArgument
            Tracer.Point("BrowserInit completed");

            // Here rather than in AfterFirstRender, whose render gate + 1s delay left gestures dead
            // for the first seconds after launch - but not before this point: everything below
            // pushes to JS modules that don't exist until BrowserInit has completed.
            StartScopedServices(Hub.Services);

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
            _ = Hub.Services.GetRequiredService<PrefetchUI>().Initialize();
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

            // Ahead of the delay below: these two drive the OS-level activity UI, and on Android
            // that includes the armed PTT notification the user expects at launch.
            Hub.Services.GetRequiredService<ActivitiesUI>().Start();
            _ = Hub.ActivitiesBackend; // Touch. Auto-starts on construction; WebView-only - see StartScopedServices

            // Starting less important UI services
            await Task.Delay(baseDelay, cancellationToken).ConfigureAwait(false);
            Hub.Services.GetRequiredService<SessionTokens>().Start();
            Hub.Services.GetRequiredService<ReconnectUI>().Start();
            _ = Hub.NotificationsPanelUI; // Touch. Auto-starts read-retention tracking on construction.
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
            Hub.Services.GetRequiredService<SeenNotificationDismisser>().Start();
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

    public static void StartScopedServices(IServiceProvider services)
    {
        // Exactly once per scope: PrepareFirstRender for a WebView scope, HeadlessBlazorScope for
        // a headless one. AudioFocusUI.WarmUp flips the audio mode for ~300ms, so a second call
        // would re-prime the HAL for nothing.
        // Runs for any scope, headless or WebView. Everything here must work with a disconnected
        // SafeJSRuntime - see HeadlessBlazorScope. ActivitiesBackend is deliberately absent: the wake
        // handler owns the foreground service, and every widget output parks on DispatchToBlazor.
        var hub = services.GetRequiredService<AppUIHub>();
        // These three belong here rather than in AfterFirstRender because a headless scope never
        // renders, and each one silently wedges an audio path when it doesn't run:
        // - ServerTimeSync: ServerClock.WhenReady never completes, so the listening player hangs.
        // - ConnectivityUI: IsConnected is seeded false on MAUI and only its worker ever sets it,
        //   so the recorder's WhenConnected wait never returns and no PTT reply can record.
        // - AudioFocusUI.WarmUp: without it every wake takes a cold Normal -> InCommunication
        //   transition, and the first track is built before the route reaches the speaker.
        services.GetService<ServerTimeSync>()?.Start();
        services.GetRequiredService<ConnectivityUI>().Start();
        services.GetRequiredService<RpcEndpointMonitor>().Start();
        _ = hub.AudioFocusUI.WarmUp();
        _ = hub.TuneUI;
        _ = hub.IncomingVoiceActivityUI;
        hub.GestureUI.Start();
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

        await dataCollectionSettingsUI.UpdateState(isDataCollectionEnabled.Value, cancellationToken)
            .ConfigureAwait(false);
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
