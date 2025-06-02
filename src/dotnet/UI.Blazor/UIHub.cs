using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Logging;
using ActualChat.Roulette;
using ActualChat.Search;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;
using ActualLab.Internal;
using ActualLab.Rpc;

namespace ActualChat.UI.Blazor;

// AppUIHub extends this type, and its instance is actually used
public class UIHub : CircuitHub, IDispatcherResolver
{
    private readonly List<Task> _tasks = new();
    private readonly List<object> _disposables = new();

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
    [field: AllowNull, MaybeNull]
    public UIEventHub UIEventHub => field ??= Services.GetRequiredService<UIEventHub>();
    [field: AllowNull, MaybeNull]
    public ITimeZones TimeZones => field ??= Services.GetRequiredService<ITimeZones>();
    [field: AllowNull, MaybeNull]
    public IFusionTime FusionTime => field ??= Services.GetRequiredService<IFusionTime>();
    [field: AllowNull, MaybeNull]
    public LiveTime LiveTime => field ??= Services.GetRequiredService<LiveTime>();
    [field: AllowNull, MaybeNull]
    public ICaptcha Captcha => field ??= Services.GetRequiredService<ICaptcha>();
    [field: AllowNull, MaybeNull]
    public IPhones Phones => field ??= Services.GetRequiredService<IPhones>();
    [field: AllowNull, MaybeNull]
    public DiffEngine DiffEngine => field ??= Services.GetRequiredService<DiffEngine>();
    [field: AllowNull, MaybeNull]
    public SessionTokens SessionTokens => field ??= Services.GetRequiredService<SessionTokens>();
    [field: AllowNull, MaybeNull]
    public IHttpClientFactory HttpClientFactory => field ??= Services.GetRequiredService<IHttpClientFactory>();
    [field: AllowNull, MaybeNull]
    public RpcHub RpcHub => field ??= Services.GetRequiredService<RpcHub>();

    // Account-related & chat-related services
    [field: AllowNull, MaybeNull]
    public IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    [field: AllowNull, MaybeNull]
    public IAvatars Avatars => field ??= Services.GetRequiredService<IAvatars>();
    [field: AllowNull, MaybeNull]
    public Media.IMediaLinkPreviews MediaLinkPreviews => field ??= Services.GetRequiredService<Media.IMediaLinkPreviews>();
    [field: AllowNull, MaybeNull]
    public IRoulette Roulette => field ??= Services.GetRequiredService<IRoulette>();
    [field: AllowNull, MaybeNull]
    public IRouletteProfiles RouletteProfiles => field ??= Services.GetRequiredService<IRouletteProfiles>();
    [field: AllowNull, MaybeNull]
    public ISearch Search => field ??= Services.GetRequiredService<ISearch>();
    [field: AllowNull, MaybeNull]
    public Temporals Temporals => field ??= Services.GetRequiredService<Temporals>();
    [field: AllowNull, MaybeNull]
    public AccountSettings AccountSettings => field ??= Services.GetRequiredService<AccountSettings>();
    [field: AllowNull, MaybeNull]
    public LocalSettings LocalSettings => field ??= Services.GetRequiredService<LocalSettings>();
    [field: AllowNull, MaybeNull]
    public IUserPresences UserPresences => field ??= Services.GetRequiredService<IUserPresences>();
    [field: AllowNull, MaybeNull]
    public ModuleHost ModuleHost => field ??= Services.GetRequiredService<ModuleHost>();
    [field: AllowNull, MaybeNull]
    public AnalyticEvents AnalyticEvents => field ??= Services.GetRequiredService<AnalyticEvents>();
    [field: AllowNull, MaybeNull]
    public LogSinks LogSinks => field ??= Services.GetRequiredService<LogSinks>();

    // UI services
    [field: AllowNull, MaybeNull]
    public LoadingUI LoadingUI => field ??= Services.GetRequiredService<LoadingUI>();
    [field: AllowNull, MaybeNull]
    public ReloadUI ReloadUI => field ??= Services.GetRequiredService<ReloadUI>();
    [field: AllowNull, MaybeNull]
    public AccountUI AccountUI => field ??= Services.GetRequiredService<AccountUI>();
    [field: AllowNull, MaybeNull]
    public AutoNavigationUI AutoNavigationUI => field ??= Services.GetRequiredService<AutoNavigationUI>();
    [field: AllowNull, MaybeNull]
    public UserActivityUI UserActivityUI => field ??= Services.GetRequiredService<UserActivityUI>();
    [field: AllowNull, MaybeNull]
    public DeviceAwakeUI DeviceAwakeUI => field ??= Services.GetRequiredService<DeviceAwakeUI>();
    [field: AllowNull, MaybeNull]
    public InteractiveUI InteractiveUI => field ??= Services.GetRequiredService<InteractiveUI>();
    [field: AllowNull, MaybeNull]
    public KeepAwakeUI KeepAwakeUI => field ??= Services.GetRequiredService<KeepAwakeUI>();
    [field: AllowNull, MaybeNull]
    public ClipboardUI ClipboardUI => field ??= Services.GetRequiredService<ClipboardUI>();
    [field: AllowNull, MaybeNull]
    public PanelsUI PanelsUI => field ??= Services.GetRequiredService<PanelsUI>();
    [field: AllowNull, MaybeNull]
    public ShareUI ShareUI => field ??= Services.GetRequiredService<ShareUI>();
    [field: AllowNull, MaybeNull]
    public FocusUI FocusUI => field ??= Services.GetRequiredService<FocusUI>();
    [field: AllowNull, MaybeNull]
    public ModalUI ModalUI => field ??= Services.GetRequiredService<ModalUI>();
    [field: AllowNull, MaybeNull]
    public FontSizeUI FontSizeUI => field ??= Services.GetRequiredService<FontSizeUI>();
    [field: AllowNull, MaybeNull]
    public ThemeUI ThemeUI => field ??= Services.GetRequiredService<ThemeUI>();
    [field: AllowNull, MaybeNull]
    public TuneUI TuneUI => field ??= Services.GetRequiredService<TuneUI>();
    [field: AllowNull, MaybeNull]
    public ToastUI ToastUI => field ??= Services.GetRequiredService<ToastUI>();
    [field: AllowNull, MaybeNull]
    public BubbleUI BubbleUI => field ??= Services.GetRequiredService<BubbleUI>();
    [field: AllowNull, MaybeNull]
    public BannerUI BannerUI => field ??= Services.GetRequiredService<BannerUI>();
    [field: AllowNull, MaybeNull]
    public NavbarUI NavbarUI => field ??= Services.GetRequiredService<NavbarUI>();
    [field: AllowNull, MaybeNull]
    public IOnboardingUI OnboardingUI => field ??= Services.GetRequiredService<IOnboardingUI>();
    [field: AllowNull, MaybeNull]
    public INotificationUI NotificationUI => field ??= Services.GetRequiredService<INotificationUI>();
    [field: AllowNull, MaybeNull]
    public VisualMediaViewerUI VisualMediaViewerUI => field ??= Services.GetRequiredService<VisualMediaViewerUI>();
    [field: AllowNull, MaybeNull]
    public TotpUI TotpUI => field ??= Services.GetRequiredService<TotpUI>();
    [field: AllowNull, MaybeNull]
    public CaptchaUI CaptchaUI => field ??= Services.GetRequiredService<CaptchaUI>();
    [field: AllowNull, MaybeNull]
    public IDataCollectionSettingsUI DataCollectionSettingsUI => field ??= Services.GetRequiredService<IDataCollectionSettingsUI>();
    [field: AllowNull, MaybeNull]
    public LogUI LogUI => field ??= Services.GetRequiredService<LogUI>();

    // UI-related services w/o UI suffix
    [field: AllowNull, MaybeNull]
    public Escapist Escapist => field ??= Services.GetRequiredService<Escapist>();
    [field: AllowNull, MaybeNull]
    public RenderVars RenderVars => field ??= Services.GetRequiredService<RenderVars>();
    [field: AllowNull, MaybeNull]
    public BrowserInfo BrowserInfo => field ??= Services.GetRequiredService<BrowserInfo>();
    [field: AllowNull, MaybeNull]
    public DateTimeConverter DateTimeConverter => field ??= Services.GetRequiredService<DateTimeConverter>();
    [field: AllowNull, MaybeNull]
    public ComponentIdGenerator ComponentIdGenerator => field ??= Services.GetRequiredService<ComponentIdGenerator>();
    [field: AllowNull, MaybeNull]
    public History History => field ??= Services.GetRequiredService<History>();
	[field: AllowNull, MaybeNull]
    public UpgradeUI UpgradeUI => field ??= Services.GetRequiredService<UpgradeUI>();

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

        // This type is used in UI scopes - that's why SilentAwait(true)
        await Task.WhenAll(_tasks).SilentAwait();
        for (var i = _disposables.Count - 1; i >= 0; i--)
            await DisposeOne(_disposables[i]).SilentAwait();
        return;

        static ValueTask DisposeOne(object? disposableOrAction) {
            switch (disposableOrAction) {
            case IAsyncDisposable ad:
                return ad.DisposeSilentlyAsync();
            case IDisposable d:
                d.DisposeSilently();
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

    public void RegisterAwaitable(Task task)
    {
        lock (_tasks) {
            StopToken.ThrowIfCancellationRequested();
            _tasks.Add(task);
        }
    }

    public void RegisterDisposable(object disposableOrAction)
    {
        var isDisposed = false;
        lock (_tasks) {
            if (IsDisposed)
                isDisposed = true;
            else
                _disposables.Add(disposableOrAction);
        }
        if (isDisposed)
            _ = DisposableExt.DisposeUnknownSilently(disposableOrAction);
    }
}
