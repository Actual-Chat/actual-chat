using ActualChat.Hosting;
using ActualChat.Roulette;
using ActualChat.Search;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor;

public class UIHub(IServiceProvider services) : Hub(services)
{
    private UICommander? _uiCommander;
    private UIEventHub? _uiEventHub;
    private IJSRuntime? _jsRuntime;

    [field: AllowNull, MaybeNull]
    public IFusionTime FusionTime => field ??= Services.GetRequiredService<IFusionTime>();
    [field: AllowNull, MaybeNull]
    public LiveTime LiveTime => field ??= Services.GetRequiredService<LiveTime>();
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
    public NavigationManager Nav => field ??= Services.GetRequiredService<NavigationManager>();
    [field: AllowNull, MaybeNull]
    public History History => field ??= Services.GetRequiredService<History>();
    [field: AllowNull, MaybeNull]
    public Dispatcher Dispatcher => field ??= Services.GetRequiredService<Dispatcher>();
    [field: AllowNull, MaybeNull]
    public JSRuntimeInfo JSRuntimeInfo => field ??= CircuitContext.JSRuntimeInfo;
    [field: AllowNull, MaybeNull]
    public AppBlazorCircuitContext CircuitContext => field ??= Services.GetRequiredService<AppBlazorCircuitContext>();
    [field: AllowNull, MaybeNull]
    public ISessionResolver SessionResolver => field ??= Services.GetRequiredService<ISessionResolver>();
    [field: AllowNull, MaybeNull]
    public ModuleHost ModuleHost => field ??= Services.GetRequiredService<ModuleHost>();
    [field: AllowNull, MaybeNull]
    public AnalyticEvents AnalyticEvents => field ??= Services.GetRequiredService<AnalyticEvents>();

    // Shortcuts
    public bool IsPrerendering => JSRuntimeInfo.IsPrerendering;
    public bool IsInteractive => JSRuntimeInfo.IsInteractive;

    // These properties are exposed as methods to "close" the static ones on IServiceProvider
    public UICommander UICommander() => _uiCommander ??= Services.UICommander();
    public UIEventHub UIEventHub() => _uiEventHub ??= Services.UIEventHub();
    public IJSRuntime JSRuntime() => _jsRuntime ??= Services.JSRuntime();
}
