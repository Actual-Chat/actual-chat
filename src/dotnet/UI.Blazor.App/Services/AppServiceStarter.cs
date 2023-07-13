using ActualChat.Chat;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public class AppServiceStarter
{
    private HostInfo? _hostInfo;

    private IServiceProvider Services { get; }
    private Tracer Tracer { get; }
    private HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();

    public AppServiceStarter(IServiceProvider services)
    {
        Services = services;
        Tracer = Services.Tracer(GetType());
    }

    public Task PostSessionWarmup(Session session, CancellationToken cancellationToken)
        => Task.Run(async () => {
            // NOTE(AY): This code runs in the root scope, so you CAN'T access any scoped services here!
            using var _1 = Tracer.Region();
            try {
                // _ = Task.Run(WarmupSystemJsonSerializer, CancellationToken.None);
                var accountUI = Services.GetRequiredService<AccountUI>();
                Services.GetRequiredService<ChatUI>();
                Services.GetRequiredService<IContacts>();
                Services.GetRequiredService<IChats>();
                await accountUI.WhenLoaded.ConfigureAwait(false);
            }
            catch (SessionError e) {
                Tracer.Point($"{nameof(PostSessionWarmup)} failed, error: " + e);
                throw;
            }
            catch (Exception e) {
                Tracer.Point($"{nameof(PostSessionWarmup)} failed, error: " + e);
            }
        }, cancellationToken);

    public async Task ReadyToRender(CancellationToken cancellationToken)
    {
        using var _1 = Tracer.Region();

        // Starting initial navigation URL resolving.
        // This requires AccountUI.OwnAccount & ChatUI.SelectedChatId,
        // so both of these services are started even earlier
        var autoNavigationUI = Services.GetRequiredService<AutoNavigationUI>();
        var history = autoNavigationUI.History;
        // Note that GetAutoNavigationUrl must start in Blazor Dispatcher
        var autoNavigationUrlTask = autoNavigationUI.GetAutoNavigationUrl(cancellationToken);

        // Creating core services - this should be done as early as possible
        var jsAppSettings = Services.GetRequiredService<JavaScriptAppSettings>();
        var browserInfo = Services.GetRequiredService<BrowserInfo>();

        // Initializing them at once
        Tracer.Point("BulkInitUI.Invoke");
        var browserInit = Services.GetRequiredService<BrowserInit>();
        var session = Services.Session();
        var sessionHash = session == Session.Default ? null : session.Hash; // Session.Default is used only in WASM
        var browserInitTask = browserInit.Initialize(Constants.Api.Version, sessionHash, async initCalls => {
            await jsAppSettings.Initialize(initCalls).ConfigureAwait(false);
            await browserInfo.Initialize(initCalls).ConfigureAwait(false);
        });

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
        var autoNavigationUrl = await autoNavigationUrlTask.ConfigureAwait(false);
        await browserInitTask.ConfigureAwait(false); // Must be completed before the next call
        await history.Initialize(autoNavigationUrl).ConfigureAwait(false);
    }

    public async Task AfterRender(CancellationToken cancellationToken)
    {
        // Starting more important UI services
        await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken).ConfigureAwait(false);
        Services.GetRequiredService<SignInStateChangeReloader>().Start();

        // Starting less important UI services
        await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken).ConfigureAwait(false);
        Services.GetRequiredService<AppPresenceReporter>().Start();
        Services.GetRequiredService<AppIconBadgeUpdater>().Start();
        Services.GetService<RpcPeerStateMonitor>()?.Start(); // Available only on the client
        if (HostInfo.AppKind.IsClient()) {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            await StartHostedServices(cancellationToken).ConfigureAwait(false);
        }
        if (!HostInfo.IsProductionInstance)
            Services.GetRequiredService<DebugUI>();
    }

    // Private methods

    private async Task StartHostedServices(CancellationToken cancellationToken)
    {
        using var _ = Tracer.Region();
        foreach (var hostedService in Services.HostedServices()) {
            Tracer.Point($"{nameof(StartHostedServices)}: starting {hostedService.GetType().Name}");
            await hostedService.StartAsync(default).ConfigureAwait(false);
        }
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
