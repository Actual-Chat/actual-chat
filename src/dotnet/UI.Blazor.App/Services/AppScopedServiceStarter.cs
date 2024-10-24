using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppScopedServiceStarter
{
    private HostInfo? _hostInfo;
    private History? _history;
    private AutoNavigationUI? _autoNavigationUI;
    private LoadingUI? _loadingUI;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private Tracer Tracer { get; }
    private HostInfo HostInfo => _hostInfo ??= Services.HostInfo();
    private History History => _history ??= Services.GetRequiredService<History>();
    private AutoNavigationUI AutoNavigationUI => _autoNavigationUI ??= Services.GetRequiredService<AutoNavigationUI>();
    private LoadingUI LoadingUI => _loadingUI ??= Services.GetRequiredService<LoadingUI>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public AppScopedServiceStarter(IServiceProvider services)
    {
        Services = services;
        Tracer = Services.Tracer(GetType());
    }

    public async Task PrepareFirstRender(string sessionHash)
    {
        // Starts in Blazor dispatcher
        using var _1 = Tracer.Region();
        try {
            var baseUri = HostInfo.BaseUrl;

            // Creating core services - this should be done as early as possible
            var recaptchaUI = Services.GetRequiredService<CaptchaUI>();
            var browserInfo = Services.GetRequiredService<BrowserInfo>();
            var browserInit = Services.GetRequiredService<BrowserInit>();
            _ = browserInit.Initialize(
                HostInfo.HostKind,
                HostInfo.AppKind,
                Constants.Api.StringVersion,
                baseUri,
                sessionHash,
                browserInfo.BlazorRef);
            _ = recaptchaUI.EnsureInitialized();

            // Start AccountUI & UIEventHub
            Services.GetRequiredService<AccountUI>();
            Services.GetRequiredService<UIEventHub>();

            // Awaiting for completion of initialization tasks.
            // NOTE(AY): it's fine to use .ConfigureAwait(false) below this point,
            //           coz tasks were started on Dispatcher thread already.

            // Finishing w/ BrowserInfo
            await browserInfo.WhenReady.ConfigureAwait(false);
            // ReSharper disable once ExplicitCallerInfoArgument
            Tracer.Point("BrowserInfo is ready");

            Services.GetRequiredService<ThemeUI>().Start();
            var dateTimeConverter = Services.GetRequiredService<DateTimeConverter>();
            if (dateTimeConverter is ServerSideDateTimeConverter serverSideDateTimeConverter)
                serverSideDateTimeConverter.Initialize(browserInfo.UtcOffset);

            // Finishing with BrowserInit
            await browserInit.WhenInitialized.ConfigureAwait(false); // Must be completed before the next call
            // ReSharper disable once ExplicitCallerInfoArgument
            Tracer.Point("BrowserInit completed");

            // Finishing with auto-navigation & History init
            var url = await AutoNavigationUI.GetAutoNavigationUrl().ConfigureAwait(false);
            // Instantiate PanelsUI to register correspondent history states for left and right panels.
            // It's needed to make first history step always has BackStepCount == 0.
            _ = Services.GetRequiredService<PanelsUI>();
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
            _ = Services.GetRequiredService<OnboardingUI>().TryShow();
            var hostKind = HostInfo.HostKind;
            var baseDelay = TimeSpan.FromSeconds(hostKind.IsServer() ? 0.25 : 1);

            // Starting less important UI services
            await Task.Delay(baseDelay, cancellationToken).ConfigureAwait(false);
            Services.GetRequiredService<ReconnectUI>().Start();
            if (hostKind.IsApp())
                Services.GetRequiredService<SessionTokens>().Start();
            Services.GetRequiredService<AppPresenceReporter>().Start();
            Services.GetRequiredService<AppIconBadgeUpdater>().Start();
            Services.GetRequiredService<AppActivity>().Start();
            Services.GetRequiredService<TuneUI>(); // Auto-starts on construction
            if (!HostInfo.IsProductionInstance)
                Services.GetRequiredService<DebugUI>();

            await Task.Delay(baseDelay * 2, cancellationToken).ConfigureAwait(false);
            Services.GetRequiredService<AudioInitializer>().Start();
            if (hostKind.IsApp())
                await StartHostedServices().ConfigureAwait(false);

            await ConfigureDataCollection(cancellationToken).ConfigureAwait(false);

            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
            Services.GetRequiredService<ContactSync>().Start();
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"{nameof(AfterFirstRender)} failed");
            throw;
        }
    }

    // Private methods

    private async Task ConfigureDataCollection(CancellationToken cancellationToken)
    {
        var dataCollectionSettingsUI = Services.GetRequiredService<IDataCollectionSettingsUI>();
        if (await dataCollectionSettingsUI.IsConfigured(cancellationToken).ConfigureAwait(false))
            return;

        var accountSettings = Services.AccountSettings();
        var settings = await accountSettings.GetUserAppSettings(cancellationToken).ConfigureAwait(false);
        var isDataCollectionEnabled = settings.IsDataCollectionEnabled;
        if (!isDataCollectionEnabled.HasValue)
            return;

        await dataCollectionSettingsUI.UpdateState(isDataCollectionEnabled.Value, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartHostedServices()
    {
        using var _ = Tracer.Region();
        var tasks = new List<Task>();
        var tracePrefix = nameof(StartHostedServices) + ": starting ";
        foreach (var hostedService in Services.HostedServices()) {
            Tracer.Point(tracePrefix + hostedService.GetType().Name);
            tasks.Add(hostedService.StartAsync(default));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
