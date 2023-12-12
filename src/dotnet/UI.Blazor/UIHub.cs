using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.UI.Blazor;

public class UIHub(IServiceProvider services) : Hub(services)
{
    private IFusionTime? _fusionTime;
    private LiveTime? _liveTime;
    private IAccounts? _accounts;
    private IAvatars? _avatars;
    private Media.IMediaLinkPreviews? _linkPreviews;
    private LoadingUI? _loadingUI;
    private AccountUI? _accountUI;
    private AutoNavigationUI? _autoNavigationUI;
    private UserActivityUI? _userActivityUI;
    private DeviceAwakeUI? _deviceAwakeUI;
    private InteractiveUI? _interactiveUI;
    private KeepAwakeUI? _keepAwakeUI;
    private ClipboardUI? _clipboardUI;
    private PanelsUI? _panelsUI;
    private SearchUI? _searchUI;
    private ShareUI? _shareUI;
    private ModalUI? _modalUI;
    private FocusUI? _focusUI;
    private TuneUI? _tuneUI;
    private ToastUI? _toastUI;
    private BannerUI? _bannerUI;
    private NavbarUI? _navbarUI;
    private FeedbackUI? _feedbackUI;
    private VisualMediaViewerUI? _visualMediaViewerUI;
    private Escapist? _escapist;
    private UICommander? _uiCommander;
    private UIEventHub? _uiEventHub;
    private RenderVars? _renderVars;
    private RenderModeSelector? _renderModeSelector;
    private BrowserInfo? _browserInfo;
    private TimeZoneConverter? _timeZoneConverter;
    private NavigationManager? _nav;
    private History? _history;
    private Dispatcher? _dispatcher;
    private AppBlazorCircuitContext? _circuitContext;
    private IJSRuntime? _jsRuntime;

    public IFusionTime FusionTime => _fusionTime ??= Services.GetRequiredService<IFusionTime>();
    public LiveTime LiveTime => _liveTime ??= Services.GetRequiredService<LiveTime>();
    public IAccounts Accounts => _accounts ??= Services.GetRequiredService<IAccounts>();
    public IAvatars Avatars => _avatars ??= Services.GetRequiredService<IAvatars>();
    public Media.IMediaLinkPreviews MediaLinkPreviews => _linkPreviews ??= Services.GetRequiredService<Media.IMediaLinkPreviews>();
    public LoadingUI LoadingUI => _loadingUI ??= Services.GetRequiredService<LoadingUI>();
    public AccountUI AccountUI => _accountUI ??= Services.GetRequiredService<AccountUI>();
    public AutoNavigationUI AutoNavigationUI => _autoNavigationUI ??= Services.GetRequiredService<AutoNavigationUI>();
    public UserActivityUI UserActivityUI => _userActivityUI ??= Services.GetRequiredService<UserActivityUI>();
    public DeviceAwakeUI DeviceAwakeUI => _deviceAwakeUI ??= Services.GetRequiredService<DeviceAwakeUI>();
    public InteractiveUI InteractiveUI => _interactiveUI ??= Services.GetRequiredService<InteractiveUI>();
    public KeepAwakeUI KeepAwakeUI => _keepAwakeUI ??= Services.GetRequiredService<KeepAwakeUI>();
    public ClipboardUI ClipboardUI => _clipboardUI ??= Services.GetRequiredService<ClipboardUI>();
    public PanelsUI PanelsUI => _panelsUI ??= Services.GetRequiredService<PanelsUI>();
    public SearchUI SearchUI => _searchUI ??= Services.GetRequiredService<SearchUI>();
    public ShareUI ShareUI => _shareUI ??= Services.GetRequiredService<ShareUI>();
    public ModalUI ModalUI => _modalUI ??= Services.GetRequiredService<ModalUI>();
    public FocusUI FocusUI => _focusUI ??= Services.GetRequiredService<FocusUI>();
    public TuneUI TuneUI => _tuneUI ??= Services.GetRequiredService<TuneUI>();
    public ToastUI ToastUI => _toastUI ??= Services.GetRequiredService<ToastUI>();
    public BannerUI BannerUI => _bannerUI ??= Services.GetRequiredService<BannerUI>();
    public NavbarUI NavbarUI => _navbarUI ??= Services.GetRequiredService<NavbarUI>();
    public FeedbackUI FeedbackUI => _feedbackUI ??= Services.GetRequiredService<FeedbackUI>();
    public VisualMediaViewerUI VisualMediaViewerUI => _visualMediaViewerUI ??= Services.GetRequiredService<VisualMediaViewerUI>();
    public Escapist Escapist => _escapist ??= Services.GetRequiredService<Escapist>();
    public RenderVars RenderVars => _renderVars ??= Services.GetRequiredService<RenderVars>();
    public RenderModeSelector RenderModeSelector => _renderModeSelector ??= Services.GetRequiredService<RenderModeSelector>();
    public BrowserInfo BrowserInfo => _browserInfo ??= Services.GetRequiredService<BrowserInfo>();
    public TimeZoneConverter TimeZoneConverter => _timeZoneConverter ??= Services.GetRequiredService<TimeZoneConverter>();
    public NavigationManager Nav => _nav ??= Services.GetRequiredService<NavigationManager>();
    public History History => _history ??= Services.GetRequiredService<History>();
    public Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    public AppBlazorCircuitContext CircuitContext => _circuitContext ??= Services.GetRequiredService<AppBlazorCircuitContext>();

    // These properties are exposed as methods to "close" the static ones on IServiceProvider
    public UICommander UICommander() => _uiCommander ??= Services.UICommander();
    public UIEventHub UIEventHub() => _uiEventHub ??= Services.UIEventHub();
    public IJSRuntime JSRuntime() => _jsRuntime ??= Services.JSRuntime();
}
