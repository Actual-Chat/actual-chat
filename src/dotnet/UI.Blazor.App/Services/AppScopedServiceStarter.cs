using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Chat;
using ActualChat.Chat.UI.Blazor.Services;
using ActualChat.Contacts;
using ActualChat.Contacts.UI.Blazor.Services;
using ActualChat.Hosting;
using ActualChat.UI.Blazor.Components.Internal;
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
    private HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();
    private History History => _history ??= Services.GetRequiredService<History>();
    private AutoNavigationUI AutoNavigationUI => _autoNavigationUI ??= Services.GetRequiredService<AutoNavigationUI>();
    private LoadingUI LoadingUI => _loadingUI ??= Services.GetRequiredService<LoadingUI>();
    private ILogger Log => _log ??= Services.LogFor(GetType());

    public AppScopedServiceStarter(IServiceProvider services)
    {
        Services = services;
        Tracer = Services.Tracer(GetType());
    }

    public static void WarmupStaticServices(HostInfo hostInfo)
    {
        if (hostInfo.ClientKind.HasJit())
            _ = Task.Run(() => {
                WarmupByteSerializer();
                WarmupNewtonsoftJsonSerializer();
                WarmupSystemJsonSerializer();
            });
        _ = Task.Run(() => {
            var markup = "**b** *i* @`a`a:chatId:1 http://google.com `code`\r\n```cs\r\ncode\r\n```";
            return new MarkupParser().Parse(markup);
        });
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
        // Force LoadingUI instantiation to trigger ShowLoadingOverlay
        _ = LoadingUI;
        try {
            // Creating core services - this should be done as early as possible
            var browserInfo = Services.GetRequiredService<BrowserInfo>();

            // Initializing them at once
            Tracer.Point("BulkInitUI.Invoke");
            var browserInit = Services.GetRequiredService<BrowserInit>();
            var baseUri = HostInfo.BaseUrl;
            await browserInfo.Initialize(true).ConfigureAwait(false);
            var browserInitTask = browserInit.Initialize(
                Constants.Api.Version,
                baseUri,
                sessionHash,
                browserInfo.BackendRef!,
                browserInfo.AppKind);

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
            Tracer.Point("Browser init has completed");

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
            var appKind = HostInfo.AppKind;
            Services.GetRequiredService<AudioInitializer>().Start();
            if (appKind.IsClient())
                Services.GetRequiredService<SessionTokens>().Start();
            Services.GetRequiredService<AppPresenceReporter>().Start();
            Services.GetRequiredService<AppIconBadgeUpdater>().Start();
            Services.GetService<RpcPeerStateMonitor>()?.Start(); // Available only on the client
            Services.GetRequiredService<ContactSync>().Start();
            Services.GetRequiredService<TuneUI>(); // Auto-starts on construction
            if (appKind.IsClient()) {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                await StartHostedServices().ConfigureAwait(false);
            }
            Services.GetRequiredService<BackgroundUI>(); // Auto-starts on construction
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

    private static void WarmupByteSerializer()
    {
        var chatId = Constants.Chat.AnnouncementsChatId;
        var userId = Constants.User.Walle.UserId;
        var authorId = new AuthorId(chatId, 1L, AssumeValid.Option);
        var account = new AccountFull(new User(userId, "User"), 1);
        Warmup(new Chat.Chat(chatId) { Rules = new AuthorRules(chatId, new AuthorFull(userId, authorId), account) });
        Warmup(new ThemeSettings(Theme.Dark));
        Warmup(new UserLanguageSettings() { Primary = Languages.English, Secondary = Languages.German });
        Warmup(new UserOnboardingSettings());
        Warmup(new LocalOnboardingSettings());
        Warmup(new UserBubbleSettings() { ReadBubbles = new ApiArray<string>(new [] {"test"})});
        Warmup(new ChatListSettings());
        Warmup(new ApiArray<ActiveChat>(new[] { new ActiveChat(chatId)}));

        static void Warmup<T>(T instance) {
            var s = ByteSerializer.Default;
            using var buffer = s.Write(instance);
            s.Read<T>(buffer.WrittenMemory);
        }
    }

    private static void WarmupNewtonsoftJsonSerializer()
    {
        var media = new Media.Media() {
            ContentId = "1",
            FileName = "1",
            ContentType = "image/jpeg",
            Height = 1,
            Width = 1,
            Length = 1,
        };
        _ = new Media.Media() { MetadataJson = media.MetadataJson };
    }

    private static void WarmupSystemJsonSerializer()
    {
        Warmup(new VirtualListRenderState {
            RenderIndex = 1,
            Query = new VirtualListDataQuery(new Range<string>("1", "2")),
            KeyRange = new Range<string>("1", "2"),
            SpacerSize = 1,
            EndSpacerSize = 1,
            RequestedStartExpansion = 1,
            RequestedEndExpansion = 1,
            StartExpansion = 1,
            EndExpansion = 1,
            HasVeryFirstItem = true,
            HasVeryLastItem = true,
            ScrollToKey = "1",
        });

        static void Warmup<T>(T instance) {
            var s = SystemJsonSerializer.Default;
            var json = s.Write(instance);
            s.Read<T>(json);
        }
    }
}
