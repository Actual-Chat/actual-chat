using ActualChat.Audio;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.MediaPlayback;
using ActualChat.MLSearch;
using ActualChat.Notification;
using ActualChat.Streaming;
using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services;

public sealed class AppUIHub(IServiceProvider services) : UIHub(services)
{
    [field: AllowNull, MaybeNull]
    public IChats Chats => field ??= Services.GetRequiredService<IChats>();
    [field: AllowNull, MaybeNull]
    public IChatThreads ChatThreads => field ??= Services.GetRequiredService<IChatThreads>();
    [field: AllowNull, MaybeNull]
    public IConversations Conversations => field ??= Services.GetRequiredService<IConversations>();
    [field: AllowNull, MaybeNull]
    public IChatPositions ChatPositions => field ??= Services.GetRequiredService<IChatPositions>();
    [field: AllowNull, MaybeNull]
    public IPlaces Places => field ??= Services.GetRequiredService<IPlaces>();
    [field: AllowNull, MaybeNull]
    public IMentions Mentions => field ??= Services.GetRequiredService<IMentions>();
    [field: AllowNull, MaybeNull]
    public IAliases Aliases => field ??= Services.GetRequiredService<IAliases>();
    [field: AllowNull, MaybeNull]
    public IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    [field: AllowNull, MaybeNull]
    public IReactions Reactions => field ??= Services.GetRequiredService<IReactions>();
    [field: AllowNull, MaybeNull]
    public IRoles Roles => field ??= Services.GetRequiredService<IRoles>();
    [field: AllowNull, MaybeNull]
    public IInvites Invites => field ??= Services.GetRequiredService<IInvites>();
    [field: AllowNull, MaybeNull]
    public IContacts Contacts => field ??= Services.GetRequiredService<IContacts>();
    [field: AllowNull, MaybeNull]
    public IChatUsages ChatUsages => field ??= Services.GetRequiredService<IChatUsages>();
    [field: AllowNull, MaybeNull]
    public INotifications Notifications => field ??= Services.GetRequiredService<INotifications>();
    [field: AllowNull, MaybeNull]
    public IMLSearch MLSearch => field ??= Services.GetRequiredService<IMLSearch>();

    [field: AllowNull, MaybeNull]
    public ChatUI ChatUI => field ??= Services.GetRequiredService<ChatUI>();
    [field: AllowNull, MaybeNull]
    public SendingMessages SendingMessages => field ??= Services.GetRequiredService<SendingMessages>();
    [field: AllowNull, MaybeNull]
    public UploadSessions UploadSessions => field ??= Services.GetRequiredService<UploadSessions>();
    [field: AllowNull, MaybeNull]
    public ActiveChatsUI ActiveChatsUI => field ??= Services.GetRequiredService<ActiveChatsUI>();
    [field: AllowNull, MaybeNull]
    public AuthorUI AuthorUI => field ??= Services.GetRequiredService<AuthorUI>();
    [field: AllowNull, MaybeNull]
    public SelectionUI SelectionUI => field ??= Services.GetRequiredService<SelectionUI>();
    [field: AllowNull, MaybeNull]
    public ChatEditorUI ChatEditorUI => field ??= Services.GetRequiredService<ChatEditorUI>();
    [field: AllowNull, MaybeNull]
    public ChatListUI ChatListUI => field ??= Services.GetRequiredService<ChatListUI>();
    [field: AllowNull, MaybeNull]
    public ChatAudioUI ChatAudioUI => field ??= Services.GetRequiredService<ChatAudioUI>();
    [field: AllowNull, MaybeNull]
    public new NotificationUI NotificationUI => field ??= Services.GetRequiredService<NotificationUI>();
    [field: AllowNull, MaybeNull]
    public LanguageUI LanguageUI => field ??= Services.GetRequiredService<LanguageUI>();
    [field: AllowNull, MaybeNull]
    public EditMembersUI EditMembersUI => field ??= Services.GetRequiredService<EditMembersUI>();
    [field: AllowNull, MaybeNull]
    public HighlightUI HighlightUI => field ??= Services.GetRequiredService<HighlightUI>();
    public new OnboardingUI OnboardingUI => (OnboardingUI)base.OnboardingUI;
    [field: AllowNull, MaybeNull]
    public SearchUI SearchUI => field ??= Services.GetRequiredService<SearchUI>();
    [field: AllowNull, MaybeNull]
    public RouletteUI RouletteUI => field ??= Services.GetRequiredService<RouletteUI>();

    [field: AllowNull, MaybeNull]
    public ChatActivity ChatActivity => field ??= Services.GetRequiredService<ChatActivity>();
    [field: AllowNull, MaybeNull]
    public ChatPlayers ChatPlayers => field ??= Services.GetRequiredService<ChatPlayers>();
    [field: AllowNull, MaybeNull]
    public AudioSettings AudioSettings => field ??= Services.GetRequiredService<AudioSettings>();
    [field: AllowNull, MaybeNull]
    public AudioRecorder AudioRecorder => field ??= Services.GetRequiredService<AudioRecorder>();
    [field: AllowNull, MaybeNull]
    public AudioDownloader AudioDownloader => field ??= Services.GetRequiredService<AudioDownloader>();
    [field: AllowNull, MaybeNull]
    public AudioInitializer AudioInitializer => field ??= Services.GetRequiredService<AudioInitializer>();
    [field: AllowNull, MaybeNull]
    public IPlaybackFactory PlaybackFactory => field ??= Services.GetRequiredService<IPlaybackFactory>();
    [field: AllowNull, MaybeNull]
    public ActivePlaybackInfo ActivePlaybackInfo => field ??= Services.GetRequiredService<ActivePlaybackInfo>();
    [field: AllowNull, MaybeNull]
    public PlayableTextPaletteProvider PlayableTextPaletteProvider => field ??= Services.GetRequiredService<PlayableTextPaletteProvider>();
    [field: AllowNull, MaybeNull]
    public KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory
        => field ??= Services.GetRequiredService<KeyedFactory<IChatMarkupHub, ChatId>>();
    [field: AllowNull, MaybeNull]
    public IStreamClient StreamClient => field ??= Services.GetRequiredService<IStreamClient>();
    [field: AllowNull, MaybeNull]
    public ITranslations Translations => field ??= Services.GetRequiredService<ITranslations>();
    [field: AllowNull, MaybeNull]
    public TranslationUI TranslationUI => field ??= Services.GetRequiredService<TranslationUI>();
    [field: AllowNull, MaybeNull]
    public TranscriptUI TranscriptUI => field ??= Services.GetRequiredService<TranscriptUI>();
    [field: AllowNull, MaybeNull]
    public LinkPreviewUI LinkPreviewUI => field ??= Services.GetRequiredService<LinkPreviewUI>();
    [field: AllowNull, MaybeNull]
    public MarkupHelpers MarkupHelpers => field ??= new MarkupHelpers(this);

    // Some handy helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChatEntryReader NewEntryReader(ChatId chatId, ChatEntryKind entryKind)
        => new(Chats, Session, chatId, entryKind);
}
