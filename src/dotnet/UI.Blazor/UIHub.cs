using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Search;
using ActualChat.UI.Blazor.Services;
using ActualLab.Internal;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor;

/// <summary>
/// Central hub providing access to all UI services and utilities for Blazor components.
/// </summary>
// AppUIHub extends this type, and its instance is actually used
public class UIHub : CircuitHub, IDispatcherResolver
{
    private readonly List<(string Name, Task Task)> _tasks = new();
    private readonly List<(string Name, object DisposableOrAction)> _disposables = new();

    public ComponentBase RootComponent {
        get => field ?? throw Errors.NotInitialized();
        private set;
    } = null!;

    // Generic services
    public HostInfo HostInfo { get; }
    public Features Features { get; }
    public MomentClockSet Clocks { get; }
    public UrlMapper UrlMapper { get; }
    public ILoggerFactory LoggerFactory { get; }
    public Tracer Tracer { get; }

    // Generic lazy services
    public BlazorAppLifecycle BlazorAppLifecycle => field ??= Services.GetRequiredService<BlazorAppLifecycle>();
    public UIEventHub UIEventHub => field ??= Services.GetRequiredService<UIEventHub>();
    public ITimeZones TimeZones => field ??= Services.GetRequiredService<ITimeZones>();
    public IFusionTime FusionTime => field ??= Services.GetRequiredService<IFusionTime>();
    public LiveTime LiveTime => field ??= Services.GetRequiredService<LiveTime>();
    public ICaptcha Captcha => field ??= Services.GetRequiredService<ICaptcha>();
    public IPhones Phones => field ??= Services.GetRequiredService<IPhones>();
    public DiffEngine DiffEngine => field ??= Services.GetRequiredService<DiffEngine>();
    public SessionTokens SessionTokens => field ??= Services.GetRequiredService<SessionTokens>();
    public IHttpClientFactory HttpClientFactory => field ??= Services.GetRequiredService<IHttpClientFactory>();
    public RpcHub RpcHub => field ??= Services.GetRequiredService<RpcHub>();
    public LocalStorage LocalStorage => field ??= Services.GetRequiredService<LocalStorage>();

    // Account-related & chat-related services
    public ISystemProperties SystemProperties => field ??= Services.GetRequiredService<ISystemProperties>();
    public ISessionTemporals SessionTemporals => field ??= Services.GetRequiredService<ISessionTemporals>();
    public IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    public IAvatars Avatars => field ??= Services.GetRequiredService<IAvatars>();
    public IMediaLinkPreviews MediaLinkPreviews => field ??= Services.GetRequiredService<IMediaLinkPreviews>();
    public ISearch Search => field ??= Services.GetRequiredService<ISearch>();
    public Temporals Temporals => field ??= Services.GetRequiredService<Temporals>();
    public UserSettingsUI UserSettingsUI => field ??= Services.UserSettingsUI(Session);
    public LocalSettings LocalSettings => field ??= Services.GetRequiredService<LocalSettings>();
    public IUserPresences UserPresences => field ??= Services.GetRequiredService<IUserPresences>();
    public ModuleHost ModuleHost => field ??= Services.GetRequiredService<ModuleHost>();
    public AnalyticEvents AnalyticEvents => field ??= Services.GetRequiredService<AnalyticEvents>();

    // UI services
    public LoadingUI LoadingUI => field ??= Services.GetRequiredService<LoadingUI>();
    public ReloadUI ReloadUI => field ??= Services.GetRequiredService<ReloadUI>();
    public ReconnectUI ReconnectUI => field ??= Services.GetRequiredService<ReconnectUI>();
    public AccountUI AccountUI => field ??= Services.GetRequiredService<AccountUI>();
    public AutoNavigationUI AutoNavigationUI => field ??= Services.GetRequiredService<AutoNavigationUI>();
    public UserActivityUI UserActivityUI => field ??= Services.GetRequiredService<UserActivityUI>();
    public DeviceAwakeUI DeviceAwakeUI => field ??= Services.GetRequiredService<DeviceAwakeUI>();
    public InteractiveUI InteractiveUI => field ??= Services.GetRequiredService<InteractiveUI>();
    public KeepAwakeUI KeepAwakeUI => field ??= Services.GetRequiredService<KeepAwakeUI>();
    public ClipboardUI ClipboardUI => field ??= Services.GetRequiredService<ClipboardUI>();
    public PanelsUI PanelsUI => field ??= Services.GetRequiredService<PanelsUI>();
    public ShareUI ShareUI => field ??= Services.GetRequiredService<ShareUI>();
    public FocusUI FocusUI => field ??= Services.GetRequiredService<FocusUI>();
    public ModalUI ModalUI => field ??= Services.GetRequiredService<ModalUI>();
    public FontSizeUI FontSizeUI => field ??= Services.GetRequiredService<FontSizeUI>();
    public ThemeUI ThemeUI => field ??= Services.GetRequiredService<ThemeUI>();
    public TuneUI TuneUI => field ??= Services.GetRequiredService<TuneUI>();
    public ToastUI ToastUI => field ??= Services.GetRequiredService<ToastUI>();
    public BubbleUI BubbleUI => field ??= Services.GetRequiredService<BubbleUI>();
    public BannerUI BannerUI => field ??= Services.GetRequiredService<BannerUI>();
    public NavbarUI NavbarUI => field ??= Services.GetRequiredService<NavbarUI>();
    public IOnboardingUI OnboardingUI => field ??= Services.GetRequiredService<IOnboardingUI>();
    public INotificationUI NotificationUI => field ??= Services.GetRequiredService<INotificationUI>();
    public VisualMediaViewerUI VisualMediaViewerUI => field ??= Services.GetRequiredService<VisualMediaViewerUI>();
    public TotpUI TotpUI => field ??= Services.GetRequiredService<TotpUI>();
    public CaptchaUI CaptchaUI => field ??= Services.GetRequiredService<CaptchaUI>();
    public ConnectivityUI ConnectivityUI => field ??= Services.GetRequiredService<ConnectivityUI>();
    public AudioFocusUI AudioFocusUI => field ??= Services.GetRequiredService<AudioFocusUI>();
    public IDataCollectionSettingsUI DataCollectionSettingsUI => field ??= Services.GetRequiredService<IDataCollectionSettingsUI>();
    public LogUI LogUI => field ??= Services.GetRequiredService<LogUI>();
    public ReportUI ReportUI => field ??= Services.GetRequiredService<ReportUI>();

    // UI-related services w/o UI suffix
    public RenderVars RenderVars => field ??= Services.GetRequiredService<RenderVars>();
    public BrowserInfo BrowserInfo => field ??= Services.GetRequiredService<BrowserInfo>();
    public DateTimeConverter DateTimeConverter => field ??= Services.GetRequiredService<DateTimeConverter>();
    public ComponentIdGenerator ComponentIdGenerator => field ??= Services.GetRequiredService<ComponentIdGenerator>();
    public History History => field ??= Services.GetRequiredService<History>();

    public Tracer TracerFor(string name) => Tracer[name];
    public Tracer TracerFor(Type type) => Tracer[type];
    public Tracer TracerFor<TService>() => Tracer[typeof(TService)];

    public ILogger<T> LogFor<T>() => LoggerFactory.CreateLogger<T>();
    public ILogger LogFor(Type type) => LoggerFactory.CreateLogger(type.NonProxyType());
    public ILogger LogFor(string category) => LoggerFactory.CreateLogger(category);

    public UIHub(IServiceProvider services) : base(services)
    {
        if (!OSInfo.IsWebAssembly)
            Log.LogInformation("[+] #{Id}", Id.Format());

        HostInfo = services.HostInfo();
        Features = services.Features();
        Clocks = services.GetRequiredService<MomentClockSet>();
        UrlMapper = services.UrlMapper();
        LoggerFactory = services.GetRequiredService<ILoggerFactory>();
        Tracer = services.Tracer();
    }

    protected override async Task DisposeAsyncCore()
    {
        if (!OSInfo.IsWebAssembly)
            Log.LogInformation("[-] #{Id}", Id.Format());

        var timeout = CoreConstants.DisposeTimeout;
        // This type is used in UI scopes - that's why we never throw from dispose;
        // instead, we time out individual awaits and log so subsequent dispose stages still run.
        try {
            await Task.WhenAll(_tasks.Select(static x => x.Task)).WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException) {
            var stuck = _tasks
                .Where(x => !x.Task.IsCompleted)
                .Select(x => x.Name)
                .ToList();
            Log.LogWarning(
                "DisposeAsyncCore: {StuckCount} awaitable(s) didn't complete in {Timeout}: {StuckNames}",
                stuck.Count, timeout, string.Join(", ", stuck));
        }
        catch {
            // Faulted/cancelled awaitables are intentionally ignored — they shouldn't block dispose.
        }
        for (var i = _disposables.Count - 1; i >= 0; i--) {
            var (name, item) = _disposables[i];
            try {
                await DisposeOne(item).AsTask().WaitAsync(timeout).ConfigureAwait(false);
            }
            catch (TimeoutException) {
                Log.LogWarning(
                    "DisposeAsyncCore: disposable '{Name}' didn't dispose in {Timeout}, skipping",
                    name, timeout);
            }
            catch (Exception e) {
                Log.LogWarning(e, "DisposeAsyncCore: disposable '{Name}' threw while disposing", name);
            }
        }
        return;

        static ValueTask DisposeOne(object? disposableOrAction) {
            switch (disposableOrAction) {
            case IAsyncDisposable ad:
                return ad.DisposeAsync();
            case IDisposable d:
                d.Dispose();
                break;
            case Func<ValueTask> f:
                return f.Invoke();
            case Action a:
                a.Invoke();
                break;
            }
            return default;
        }
    }

    public override void Initialize(
        Dispatcher dispatcher,
        RenderModeDef renderMode)
        => throw StandardError.NotSupported("Use another implementation of Initialize.");

    public void Initialize(ComponentBase rootComponent, RenderModeDef renderMode)
    {
        var dispatcher = rootComponent.GetDispatcher();
        lock (Lock) {
            if (WhenInitializedSource.Task.IsCompleted) {
                if (Dispatcher == dispatcher && RenderMode == renderMode) {
                    RootComponent = rootComponent;
                    return;
                }

                throw Errors.AlreadyInitialized();
            }

            RootComponent = rootComponent;
            Dispatcher = dispatcher;
            RenderMode = renderMode;
            WhenInitializedSource.TrySetResult();
        }
    }

    public void RegisterAwaitable(Task task, [CallerArgumentExpression(nameof(task))] string name = "")
    {
        lock (_tasks) {
            StopToken.ThrowIfCancellationRequested();
            _tasks.Add((name, task));
        }
    }

    public void RegisterDisposable(
        object disposableOrAction,
        [CallerArgumentExpression(nameof(disposableOrAction))] string name = "")
    {
        var isDisposed = false;
        lock (_tasks) {
            if (IsDisposed)
                isDisposed = true;
            else
                _disposables.Add((name, disposableOrAction));
        }
        if (isDisposed)
            _ = DisposableExt.DisposeUnknownSilently(disposableOrAction);
    }
}
