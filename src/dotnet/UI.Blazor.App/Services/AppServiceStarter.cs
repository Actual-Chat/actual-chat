using ActualChat.Chat;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Contacts;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using Stl.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public class AppServiceStarter
{
    private HostInfo? _hostInfo;
    private History? _history;
    private AutoNavigationUI? _autoNavigationUI;
    private LoadingUI? _loadingUI;
    private ILogger? _log;

    private IServiceProvider Services { get; }
    private Tracer Tracer { get; }
    private HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();
    private History History => _history ??= Services.GetRequiredService<History>();
    private AutoNavigationUI AutoNavigationUI => _autoNavigationUI ??= Services.GetRequiredService<AutoNavigationUI>();
    private LoadingUI LoadingUI => _loadingUI ??= Services.GetRequiredService<LoadingUI>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public AppServiceStarter(IServiceProvider services)
    {
        Services = services;
        Tracer = Services.Tracer(GetType());
    }

    public Task StartNonScopedServices()
        => Task.Run(async () => {
            using var _1 = Tracer.Region();
            try {
                // NOTE(AY): !!! This code runs in the root scope,
                // so you CAN'T access any scoped services here!

                var startHostedServicesTask = StartHostedServices();
                if (HostInfo.AppKind.IsWasmApp()) {
                    await startHostedServicesTask.ConfigureAwait(false);
                    return; // Further code warms up some services, which isn't necessary in WASM
                }

                var session = Session.Default; // All clients use default session
                var cancellationToken = CancellationToken.None; // No cancellation here

                // Access key services
                var accounts = Services.GetRequiredService<IAccounts>();
                var contacts = Services.GetRequiredService<IContacts>();
                Services.GetRequiredService<IChats>();

                // Preload own account
                var ownAccountTask = accounts.GetOwn(session, cancellationToken);

                // Start preloading top contacts
                var contactIdsTask = contacts.ListIds(session, cancellationToken);
                var contactIds = await contactIdsTask.ConfigureAwait(false);
                foreach (var contactId in contactIds.Take(Constants.Contacts.MinLoadLimit))
                    _ = contacts.Get(session, contactId, cancellationToken);

                // _ = Task.Run(WarmupSystemJsonSerializer, CancellationToken.None);

                // Complete the tasks we started earlier
                await ownAccountTask.ConfigureAwait(false);
                await startHostedServicesTask.ConfigureAwait(false);
            }
            catch (Exception e) {
                Tracer.Point($"{nameof(StartNonScopedServices)} failed, error: " + e);
            }
        }, CancellationToken.None);

    public async Task PrepareFirstRender(string sessionHash)
    {
        // Starts in Blazor dispatcher
        using var _1 = Tracer.Region();
        try {
            // Creating core services - this should be done as early as possible
            var browserInfo = Services.GetRequiredService<BrowserInfo>();

            // Initializing them at once
            Tracer.Point("BulkInitUI.Invoke");
            var browserInit = Services.GetRequiredService<BrowserInit>();
            var baseUri = HostInfo.BaseUrl;
            var browserInitTask = browserInit.Initialize(
                Constants.Api.Version, baseUri, sessionHash,
                async initCalls => {
                    await browserInfo.Initialize(initCalls).ConfigureAwait(false);
                });

            // Start AccountUI & UIEventHub
            Services.GetRequiredService<AccountUI>();
            Services.GetRequiredService<UIEventHub>();

            // Starting ThemeUI
            Tracer.Point("ThemeUI.Start");
            var themeUI = Services.GetRequiredService<ThemeUI>();
            _ = Task.Run(() => themeUI.Start(), CancellationToken.None);

            // Awaiting for completion of initialization tasks.
            // NOTE(AY): it's fine to use .ConfigureAwait(false) below this point,
            //           coz tasks were started on Dispatcher thread already.

            // Finishing w/ BrowserInfo
            await browserInfo.WhenReady.ConfigureAwait(false);
            Tracer.Point("BrowserInfo is ready");

            var timeZoneConverter = Services.GetRequiredService<TimeZoneConverter>();
            if (timeZoneConverter is ServerSideTimeZoneConverter serverSideTimeZoneConverter)
                serverSideTimeZoneConverter.Initialize(browserInfo.UtcOffset);

            // Finishing w/ ThemeUI
            await themeUI.WhenReady.ConfigureAwait(false);
            Tracer.Point("ThemeUI is ready");

            // Finishing with BrowserInit
            await browserInitTask.ConfigureAwait(false); // Must be completed before the next call

            // Finishing with auto-navigation & History init
            var url = await AutoNavigationUI.GetAutoNavigationUrl().ConfigureAwait(false);
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

            // Starting less important UI services
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            if (HostInfo.AppKind.IsClient())
                Services.GetRequiredService<SessionTokens>().Start();
            (Services.GetRequiredService<TuneUI>() as INotifyInitialized).Initialized();
            Services.GetRequiredService<AppPresenceReporter>().Start();
            Services.GetRequiredService<AppIconBadgeUpdater>().Start();
            Services.GetService<RpcPeerStateMonitor>()?.Start(); // Available only on the client
            Services.GetRequiredService<ContactSync>().Start();
            if (HostInfo.AppKind.IsClient()) {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                await StartHostedServices().ConfigureAwait(false);
            }
            if (!HostInfo.IsProductionInstance)
                Services.GetRequiredService<DebugUI>();
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogError(e, $"{nameof(AfterFirstRender)} failed");
            throw;
        }
    }

    // Private methods

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

    private void WarmupSystemJsonSerializer()
    {
        Read<ThemeSettings>("{\"theme\":1,\"origin\":\"\"}");
        Read<UserLanguageSettings>("{\"primary\":\"fr-FR\",\"secondary\":null,\"origin\":\"\"}");
        Read<UserOnboardingSettings>("{\"isPhoneStepCompleted\":false,\"isAvatarStepCompleted\":true,\"lastShownAt\":\"1970-01-01T00:00:00.0000000Z\",\"origin\":\"\"}");
        Read<UserBubbleSettings>("{\"readBubbles\":[\"x\"],\"origin\":\"\"}");
        Read<ChatListSettings>("{\"order\":3,\"filterId\":\"\"}");
        Read<ApiArray<ActiveChat>>("[{\"chatId\":\"dpwo1tm0tw\",\"isListening\":false,\"isRecording\":false,\"recency\":\"1970-01-01T00:00:00.0000000Z\",\"listeningRecency\":\"1970-01-01T00:00:00.0000000Z\"}]");

        static void Read<T>(string json)
        {
            try {
                SystemJsonSerializer.Default.Read<T>(json);
            }
            catch {
                // Intended
            }
        }
    }
}
