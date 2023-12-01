using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Contacts;
using ActualChat.Hosting;
using ActualChat.Invite;
using ActualChat.Kvas;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public record ChatHub(IServiceProvider Services, Session Session) : IHasServices, IServiceProvider
{
    private IChats? _chats;
    private IPlaces? _places;
    private IChatPositions? _chatPositions;
    private IMentions? _mentions;
    private Media.IMediaLinkPreviews? _linkPreviews;
    private IRoles? _roles;
    private IAuthors? _authors;
    private IReactions? _reactions;
    private IInvites? _invites;
    private IContacts? _contacts;
    private IAvatars? _avatars;
    private IAccounts? _accounts;
    private IUserPresences? _userPresences;
    private ChatActivity? _chatActivity;
    private ChatUI? _chatUI;
    private ActiveChatsUI? _activeChatsUI;
    private AuthorUI? _authorUI;
    private AccountUI? _accountUI;
    private SelectionUI? _selectionUI;
    private ChatEditorUI? _chatEditorUI;
    private ChatListUI? _chatListUI;
    private ChatAudioUI? _chatAudioUI;
    private ChatPlayers? _chatPlayers;
    private AudioSettings? _audioSettings;
    private AudioRecorder? _audioRecorder;
    private IAudioStreamer? _audioStreamer;
    private AudioDownloader? _audioDownloader;
    private AudioInitializer? _audioInitializer;
    private IAudioOutputController? _audioOutputController;
    private IPlaybackFactory? _playbackFactory;
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
    private ToastUI? _toastUI;
    private BannerUI? _bannerUI;
    private LanguageUI? _languageUI;
    private FeedbackUI? _feedbackUI;
    private TuneUI? _tuneUI;
    private LoadingUI? _loadingUI;
    private EditMembersUI? _editMembersUI;
    private ICommander? _commander;
    private UICommander? _uiCommander;
    private UIEventHub? _uiEventHub;
    private History? _history;
    private NavbarUI? _navbarUI;
    private Features? _features;
    private BrowserInfo? _browserInfo;
    private TimeZoneConverter? _timeZoneConverter;
    private UrlMapper? _urlMapper;
    private NavigationManager? _nav;
    private Dispatcher? _dispatcher;
    private MomentClockSet? _clocks;
    private IStateFactory? _stateFactory;
    private AccountSettings? _accountSettings;
    private LocalSettings? _localSettings;
    private KeyedFactory<IChatMarkupHub, ChatId>? _chatMarkupHubFactory;
    private BlazorCircuitContext? _circuitContext;
    private HostInfo? _hostInfo;
    private IJSRuntime? _js;

    public IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    public IChatPositions ChatPositions => _chatPositions ??= Services.GetRequiredService<IChatPositions>();
    public IMentions Mentions => _mentions ??= Services.GetRequiredService<IMentions>();
    public IPlaces Places => _places ??= Services.GetRequiredService<IPlaces>();
    public Media.IMediaLinkPreviews MediaLinkPreviews => _linkPreviews ??= Services.GetRequiredService<Media.IMediaLinkPreviews>();
    public IAuthors Authors => _authors ??= Services.GetRequiredService<IAuthors>();
    public IReactions Reactions => _reactions ??= Services.GetRequiredService<IReactions>();
    public IRoles Roles => _roles ??= Services.GetRequiredService<IRoles>();
    public IInvites Invites => _invites ??= Services.GetRequiredService<IInvites>();
    public IContacts Contacts => _contacts ??= Services.GetRequiredService<IContacts>();
    public IAvatars Avatars => _avatars ??= Services.GetRequiredService<IAvatars>();
    public IAccounts Accounts => _accounts ??= Services.GetRequiredService<IAccounts>();
    public IUserPresences UserPresences => _userPresences ??= Services.GetRequiredService<IUserPresences>();
    public ChatActivity ChatActivity => _chatActivity ??= Services.GetRequiredService<ChatActivity>();
    public ChatUI ChatUI => _chatUI ??= Services.GetRequiredService<ChatUI>();
    public ActiveChatsUI ActiveChatsUI => _activeChatsUI ??= Services.GetRequiredService<ActiveChatsUI>();
    public AuthorUI AuthorUI => _authorUI ??= Services.GetRequiredService<AuthorUI>();
    public AccountUI AccountUI => _accountUI ??= Services.GetRequiredService<AccountUI>();
    public SelectionUI SelectionUI => _selectionUI ??= Services.GetRequiredService<SelectionUI>();
    public ChatEditorUI ChatEditorUI => _chatEditorUI ??= Services.GetRequiredService<ChatEditorUI>();
    public ChatListUI ChatListUI => _chatListUI ??= Services.GetRequiredService<ChatListUI>();
    public ChatAudioUI ChatAudioUI => _chatAudioUI ??= Services.GetRequiredService<ChatAudioUI>();
    public ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    public AudioSettings AudioSettings => _audioSettings ??= Services.GetRequiredService<AudioSettings>();
    public AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    public IAudioStreamer AudioStreamer => _audioStreamer ??= Services.GetRequiredService<IAudioStreamer>();
    public AudioDownloader AudioDownloader => _audioDownloader ??= Services.GetRequiredService<AudioDownloader>();
    public AudioInitializer AudioInitializer => _audioInitializer ??= Services.GetRequiredService<AudioInitializer>();
    public IAudioOutputController AudioOutputController => _audioOutputController ??= Services.GetRequiredService<IAudioOutputController>();
    public IPlaybackFactory PlaybackFactory => _playbackFactory ??= Services.GetRequiredService<IPlaybackFactory>();
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
    public ToastUI ToastUI => _toastUI ??= Services.GetRequiredService<ToastUI>();
    public BannerUI BannerUI => _bannerUI ??= Services.GetRequiredService<BannerUI>();
    public LanguageUI LanguageUI => _languageUI ??= Services.GetRequiredService<LanguageUI>();
    public FeedbackUI FeedbackUI => _feedbackUI ??= Services.GetRequiredService<FeedbackUI>();
    public TuneUI TuneUI => _tuneUI ??= Services.GetRequiredService<TuneUI>();
    public LoadingUI LoadingUI => _loadingUI ??= Services.GetRequiredService<LoadingUI>();
    public EditMembersUI EditMembersUI => _editMembersUI ??= Services.GetRequiredService<EditMembersUI>();
    public History History => _history ??= Services.GetRequiredService<History>();
    public NavbarUI NavbarUI => _navbarUI ??= Services.GetRequiredService<NavbarUI>();
    public Features Features => _features ??= Services.GetRequiredService<Features>();
    public BrowserInfo BrowserInfo => _browserInfo ??= Services.GetRequiredService<BrowserInfo>();
    public TimeZoneConverter TimeZoneConverter => _timeZoneConverter ??= Services.GetRequiredService<TimeZoneConverter>();
    public NavigationManager Nav => _nav ??= Services.GetRequiredService<NavigationManager>();
    public Dispatcher Dispatcher => _dispatcher ??= Services.GetRequiredService<Dispatcher>();
    public KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory
        => _chatMarkupHubFactory ??= Services.GetRequiredService<KeyedFactory<IChatMarkupHub, ChatId>>();
    public BlazorCircuitContext CircuitContext => _circuitContext ??= Services.GetRequiredService<BlazorCircuitContext>();
    public HostInfo HostInfo => _hostInfo ??= Services.GetRequiredService<HostInfo>();
    public IJSRuntime JS => _js ??= Services.JSRuntime();

    // These properties are exposed as methods to "close" the static ones on IServiceProvider
    public IStateFactory StateFactory() => _stateFactory ??= Services.StateFactory();
    public AccountSettings AccountSettings() => _accountSettings ??= Services.AccountSettings();
    public LocalSettings LocalSettings() => _localSettings ??= Services.LocalSettings();
    public ICommander Commander() => _commander ??= Services.Commander();
    public UICommander UICommander() => _uiCommander ??= Services.UICommander();
    public UIEventHub UIEventHub() => _uiEventHub ??= Services.UIEventHub();
    public UrlMapper UrlMapper() => _urlMapper ??= Services.UrlMapper();
    public MomentClockSet Clocks() => _clocks ??= Services.Clocks();

    // Some handy helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChatEntryReader NewEntryReader(ChatId chatId, ChatEntryKind entryKind, TileLayer<long>? idTileLayer = null)
        => new(Chats, Session, chatId, entryKind, idTileLayer);

    // IServiceProvider
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? GetService(Type serviceType)
        => Services.GetService(serviceType);

    // This record relies on referential equality
    public virtual bool Equals(ChatHub? other)
        => ReferenceEquals(this, other);
    public override int GetHashCode()
        => RuntimeHelpers.GetHashCode(this);
}
