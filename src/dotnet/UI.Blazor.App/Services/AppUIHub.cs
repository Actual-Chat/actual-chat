using ActualChat.Audio;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.MediaPlayback;
using ActualChat.Notifications;
using ActualChat.Streaming;
using ActualChat.UI.App.Services;
using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Extended UI hub with access to chat-specific services, audio, and playback functionality.
/// </summary>
public sealed class AppUIHub(IServiceProvider services) : UIHub(services)
{
    public IChats Chats => field ??= Services.GetRequiredService<IChats>();
    public IChatThreads ChatThreads => field ??= Services.GetRequiredService<IChatThreads>();
    public IConversations Conversations => field ??= Services.GetRequiredService<IConversations>();
    public IChatPositions ChatPositions => field ??= Services.GetRequiredService<IChatPositions>();
    public IPlaces Places => field ??= Services.GetRequiredService<IPlaces>();
    public IMentions Mentions => field ??= Services.GetRequiredService<IMentions>();
    public IAliases Aliases => field ??= Services.GetRequiredService<IAliases>();
    public IAuthors Authors => field ??= Services.GetRequiredService<IAuthors>();
    public IReactions Reactions => field ??= Services.GetRequiredService<IReactions>();
    public ISharedLocations SharedLocations => field ??= Services.GetRequiredService<ISharedLocations>();
    public IRoles Roles => field ??= Services.GetRequiredService<IRoles>();
    public IInvites Invites => field ??= Services.GetRequiredService<IInvites>();
    public IContacts Contacts => field ??= Services.GetRequiredService<IContacts>();
    public IChatUsages ChatUsages => field ??= Services.GetRequiredService<IChatUsages>();
    public INotifications Notifications => field ??= Services.GetRequiredService<INotifications>();
    public ITranslations Translations => field ??= Services.GetRequiredService<ITranslations>();
    public ILiveAudioStreams LiveAudioStreams => field ??= Services.GetRequiredService<ILiveAudioStreams>();
    public ILiveVideoStreams LiveVideoStreams => field ??= Services.GetRequiredService<ILiveVideoStreams>();
    public ILiveSessions LiveSessions => field ??= Services.GetRequiredService<ILiveSessions>();
    public IChatTypingActivities ChatTypingActivities => field ??= Services.GetRequiredService<IChatTypingActivities>();
    public IMedia Media => field ??= Services.GetRequiredService<IMedia>();
    public IGifs Gifs => field ??= Services.GetRequiredService<IGifs>();
    public VideoTranscoder VideoTranscoder => field ??= Services.GetRequiredService<VideoTranscoder>();

    public ChatUI ChatUI => field ??= Services.GetRequiredService<ChatUI>();
    public PeerBlockUI PeerBlockUI => field ??= Services.GetRequiredService<PeerBlockUI>();
    public AttachmentsState AttachmentsState => field ??= Services.GetRequiredService<AttachmentsState>();
    public SendingMessages SendingMessages => field ??= Services.GetRequiredService<SendingMessages>();
    public UploadSessions UploadSessions => field ??= Services.GetRequiredService<UploadSessions>();
    public UploadSessionsState UploadSessionsState => field ??= Services.GetRequiredService<UploadSessionsState>();
    public IUploads Uploads => field ??= Services.GetRequiredService<IUploads>();
    public ConversationUI ConversationUI => field ??= Services.GetRequiredService<ConversationUI>();
    public ActiveChatsUI ActiveChatsUI => field ??= Services.GetRequiredService<ActiveChatsUI>();
    public AuthorUI AuthorUI => field ??= Services.GetRequiredService<AuthorUI>();
    public SelectionUI SelectionUI => field ??= Services.GetRequiredService<SelectionUI>();
    public ChatPinsUI ChatPinsUI => field ??= Services.GetRequiredService<ChatPinsUI>();
    public ChatEditorUI ChatEditorUI => field ??= Services.GetRequiredService<ChatEditorUI>();
    public ChatListUI ChatListUI => field ??= Services.GetRequiredService<ChatListUI>();
    public NotificationsPanelUI NotificationsPanelUI => field ??= Services.GetRequiredService<NotificationsPanelUI>();
    public NotificationsUI NotificationsUI => field ??= Services.GetRequiredService<NotificationsUI>();
    public ChatAudioUI ChatAudioUI => field ??= Services.GetRequiredService<ChatAudioUI>();
    public ChatVideoUI ChatVideoUI => field ??= Services.GetRequiredService<ChatVideoUI>();
    public CameraUI CameraUI => field ??= Services.GetRequiredService<CameraUI>();
    public LocationUI LocationUI => field ??= Services.GetRequiredService<LocationUI>();
    public VideoQualityUI VideoQualityUI => field ??= Services.GetRequiredService<VideoQualityUI>();
    public AudioDiagnosticsUI AudioDiagnosticsUI => field ??= Services.GetRequiredService<AudioDiagnosticsUI>();
    public LiveStreamUI LiveStreamUI => field ??= Services.GetRequiredService<LiveStreamUI>();
    public TypingUI TypingUI => field ??= Services.GetRequiredService<TypingUI>();
    public IncomingVoiceActivityUI IncomingVoiceActivityUI
        => field ??= Services.GetRequiredService<IncomingVoiceActivityUI>();
    public PttReplyUI PttReplyUI => field ??= Services.GetRequiredService<PttReplyUI>();
    public GestureUI GestureUI => field ??= Services.GetRequiredService<GestureUI>();
    public LiveSessionUI LiveSessionUI => field ??= Services.GetRequiredService<LiveSessionUI>();
    public IncomingCallUI IncomingCallUI => field ??= Services.GetRequiredService<IncomingCallUI>();
    public LiveBlockUI LiveBlockUI => field ??= Services.GetRequiredService<LiveBlockUI>();
    public ChatActivityUI ChatActivityUI => field ??= Services.GetRequiredService<ChatActivityUI>();
    public new NotificationUI NotificationUI => field ??= Services.GetRequiredService<NotificationUI>();
    public LanguageUI LanguageUI => field ??= Services.GetRequiredService<LanguageUI>();
    public LocalizationUI LocalizationUI => field ??= Services.GetRequiredService<LocalizationUI>();
    public EditMembersUI EditMembersUI => field ??= Services.GetRequiredService<EditMembersUI>();
    public HighlightUI HighlightUI => field ??= Services.GetRequiredService<HighlightUI>();
    public new OnboardingUI OnboardingUI => (OnboardingUI)base.OnboardingUI;
    public SearchUI SearchUI => field ??= Services.GetRequiredService<SearchUI>();
    public LocalSearchUI LocalSearchUI => field ??= Services.GetRequiredService<LocalSearchUI>();
    public TranslationUI TranslationUI => field ??= Services.GetRequiredService<TranslationUI>();
    public TranscriptUI TranscriptUI => field ??= Services.GetRequiredService<TranscriptUI>();
    public LinkPreviewUI LinkPreviewUI => field ??= Services.GetRequiredService<LinkPreviewUI>();

    public AudioSettings AudioSettings => field ??= Services.GetRequiredService<AudioSettings>();
    public AudioRecorder AudioRecorder => field ??= Services.GetRequiredService<AudioRecorder>();
    public IAudioInitializer AudioInitializer => field ??= Services.GetRequiredService<IAudioInitializer>();
    public IPlaybackFactory PlaybackFactory => field ??= Services.GetRequiredService<IPlaybackFactory>();
    public ActivePlaybackInfo ActivePlaybackInfo => field ??= Services.GetRequiredService<ActivePlaybackInfo>();
    public ActivitiesBackend ActivitiesBackend => field ??= Services.GetRequiredService<ActivitiesBackend>();
    public AudioAttachmentPlayer AudioAttachmentPlayer
        => field ??= Services.GetRequiredService<AudioAttachmentPlayer>();
    public ReactionsUI ReactionsUI => field ??= Services.GetRequiredService<ReactionsUI>();
    public ClientCommandQueue ClientCommandQueue => field ??= Services.GetRequiredService<ClientCommandQueue>();
    public ClientCommandQueueTriggers ClientCommandQueueTriggers
        => field ??= Services.GetRequiredService<ClientCommandQueueTriggers>();

    public KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory
        => field ??= Services.GetRequiredService<KeyedFactory<IChatMarkupHub, ChatId>>();
    public MarkupHelpers MarkupHelpers => field ??= new MarkupHelpers(this);

    // Some handy helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChatEntryReader NewEntryReader(ChatId chatId)
        => new(Chats, Session, chatId);
}
