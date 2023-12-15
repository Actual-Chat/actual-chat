using ActualChat.Audio;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.MediaPlayback;
using ActualChat.Notification.UI.Blazor;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed class ChatUIHub(IServiceProvider services) : UIHub(services)
{
    private IChats? _chats;
    private IChatPositions? _chatPositions;
    private IPlaces? _places;
    private IMentions? _mentions;
    private IRoles? _roles;
    private IAuthors? _authors;
    private IReactions? _reactions;
    private IInvites? _invites;
    private IContacts? _contacts;
    private IUserPresences? _userPresences;
    private ChatActivity? _chatActivity;
    private ChatUI? _chatUI;
    private ActiveChatsUI? _activeChatsUI;
    private AuthorUI? _authorUI;
    private SelectionUI? _selectionUI;
    private ChatEditorUI? _chatEditorUI;
    private ChatListUI? _chatListUI;
    private ChatAudioUI? _chatAudioUI;
    private NotificationUI? _notificationUI;
    private LanguageUI? _languageUI;
    private EditMembersUI? _editMembersUI;
    private ChatPlayers? _chatPlayers;
    private AudioSettings? _audioSettings;
    private AudioRecorder? _audioRecorder;
    private IAudioStreamer? _audioStreamer;
    private AudioDownloader? _audioDownloader;
    private AudioInitializer? _audioInitializer;
    private IAudioOutputController? _audioOutputController;
    private IPlaybackFactory? _playbackFactory;
    private KeyedFactory<IChatMarkupHub, ChatId>? _chatMarkupHubFactory;

    public IChats Chats => _chats ??= Services.GetRequiredService<IChats>();
    public IChatPositions ChatPositions => _chatPositions ??= Services.GetRequiredService<IChatPositions>();
    public IPlaces Places => _places ??= Services.GetRequiredService<IPlaces>();
    public IMentions Mentions => _mentions ??= Services.GetRequiredService<IMentions>();
    public IAuthors Authors => _authors ??= Services.GetRequiredService<IAuthors>();
    public IReactions Reactions => _reactions ??= Services.GetRequiredService<IReactions>();
    public IRoles Roles => _roles ??= Services.GetRequiredService<IRoles>();
    public IInvites Invites => _invites ??= Services.GetRequiredService<IInvites>();
    public IContacts Contacts => _contacts ??= Services.GetRequiredService<IContacts>();
    public IUserPresences UserPresences => _userPresences ??= Services.GetRequiredService<IUserPresences>();
    public ChatActivity ChatActivity => _chatActivity ??= Services.GetRequiredService<ChatActivity>();
    public ChatUI ChatUI => _chatUI ??= Services.GetRequiredService<ChatUI>();
    public ActiveChatsUI ActiveChatsUI => _activeChatsUI ??= Services.GetRequiredService<ActiveChatsUI>();
    public AuthorUI AuthorUI => _authorUI ??= Services.GetRequiredService<AuthorUI>();
    public SelectionUI SelectionUI => _selectionUI ??= Services.GetRequiredService<SelectionUI>();
    public ChatEditorUI ChatEditorUI => _chatEditorUI ??= Services.GetRequiredService<ChatEditorUI>();
    public ChatListUI ChatListUI => _chatListUI ??= Services.GetRequiredService<ChatListUI>();
    public ChatAudioUI ChatAudioUI => _chatAudioUI ??= Services.GetRequiredService<ChatAudioUI>();
    public NotificationUI NotificationUI => _notificationUI ??= Services.GetRequiredService<NotificationUI>();
    public LanguageUI LanguageUI => _languageUI ??= Services.GetRequiredService<LanguageUI>();
    public EditMembersUI EditMembersUI => _editMembersUI ??= Services.GetRequiredService<EditMembersUI>();
    public ChatPlayers ChatPlayers => _chatPlayers ??= Services.GetRequiredService<ChatPlayers>();
    public AudioSettings AudioSettings => _audioSettings ??= Services.GetRequiredService<AudioSettings>();
    public AudioRecorder AudioRecorder => _audioRecorder ??= Services.GetRequiredService<AudioRecorder>();
    public IAudioStreamer AudioStreamer => _audioStreamer ??= Services.GetRequiredService<IAudioStreamer>();
    public AudioDownloader AudioDownloader => _audioDownloader ??= Services.GetRequiredService<AudioDownloader>();
    public AudioInitializer AudioInitializer => _audioInitializer ??= Services.GetRequiredService<AudioInitializer>();
    public IAudioOutputController AudioOutputController => _audioOutputController ??= Services.GetRequiredService<IAudioOutputController>();
    public IPlaybackFactory PlaybackFactory => _playbackFactory ??= Services.GetRequiredService<IPlaybackFactory>();
    public KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory
        => _chatMarkupHubFactory ??= Services.GetRequiredService<KeyedFactory<IChatMarkupHub, ChatId>>();

    // Some handy helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChatEntryReader NewEntryReader(ChatId chatId, ChatEntryKind entryKind, TileLayer<long>? idTileLayer = null)
        => new(Chats, Session(), chatId, entryKind, idTileLayer);
}
