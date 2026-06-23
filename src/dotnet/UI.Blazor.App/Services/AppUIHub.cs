using ActualChat.Audio;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.MediaPlayback;
using ActualChat.Notifications;
using ActualChat.Streaming;
using ActualChat.UI.App.Services;

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
    public IRoles Roles => field ??= Services.GetRequiredService<IRoles>();
    public IInvites Invites => field ??= Services.GetRequiredService<IInvites>();
    public IContacts Contacts => field ??= Services.GetRequiredService<IContacts>();
    public IChatUsages ChatUsages => field ??= Services.GetRequiredService<IChatUsages>();
    public INotifications Notifications => field ??= Services.GetRequiredService<INotifications>();
    public ITranslations Translations => field ??= Services.GetRequiredService<ITranslations>();
    public ILiveAudioStreams LiveAudioStreams => field ??= Services.GetRequiredService<ILiveAudioStreams>();
    public ILiveVideoStreams LiveVideoStreams => field ??= Services.GetRequiredService<ILiveVideoStreams>();
    public IMedia Media => field ??= Services.GetRequiredService<IMedia>();
    public IGifs Gifs => field ??= Services.GetRequiredService<IGifs>();
    public VideoTranscoder VideoTranscoder => field ??= Services.GetRequiredService<VideoTranscoder>();

    public ChatUI ChatUI => field ??= Services.GetRequiredService<ChatUI>();
    public AttachmentsState AttachmentsState => field ??= Services.GetRequiredService<AttachmentsState>();
    public SendingMessages SendingMessages => field ??= Services.GetRequiredService<SendingMessages>();
    public UploadSessions UploadSessions => field ??= Services.GetRequiredService<UploadSessions>();
    public UploadSessionsState UploadSessionsState => field ??= Services.GetRequiredService<UploadSessionsState>();
    public IUploads Uploads => field ??= Services.GetRequiredService<IUploads>();
    public ConversationUI ConversationUI => field ??= Services.GetRequiredService<ConversationUI>();
    public ActiveChatsUI ActiveChatsUI => field ??= Services.GetRequiredService<ActiveChatsUI>();
    public AuthorUI AuthorUI => field ??= Services.GetRequiredService<AuthorUI>();
    public SelectionUI SelectionUI => field ??= Services.GetRequiredService<SelectionUI>();
    public ChatEditorUI ChatEditorUI => field ??= Services.GetRequiredService<ChatEditorUI>();
    public ChatListUI ChatListUI => field ??= Services.GetRequiredService<ChatListUI>();
    public ChatAudioUI ChatAudioUI => field ??= Services.GetRequiredService<ChatAudioUI>();
    public ChatVideoUI ChatVideoUI => field ??= Services.GetRequiredService<ChatVideoUI>();
    public CameraUI CameraUI => field ??= Services.GetRequiredService<CameraUI>();
    public VideoQualityUI VideoQualityUI => field ??= Services.GetRequiredService<VideoQualityUI>();
    public LiveStreamUI LiveStreamUI => field ??= Services.GetRequiredService<LiveStreamUI>();
    public new NotificationUI NotificationUI => field ??= Services.GetRequiredService<NotificationUI>();
    public LanguageUI LanguageUI => field ??= Services.GetRequiredService<LanguageUI>();
    public EditMembersUI EditMembersUI => field ??= Services.GetRequiredService<EditMembersUI>();
    public HighlightUI HighlightUI => field ??= Services.GetRequiredService<HighlightUI>();
    public new OnboardingUI OnboardingUI => (OnboardingUI)base.OnboardingUI;
    public SearchUI SearchUI => field ??= Services.GetRequiredService<SearchUI>();
    public TranslationUI TranslationUI => field ??= Services.GetRequiredService<TranslationUI>();
    public TranscriptUI TranscriptUI => field ??= Services.GetRequiredService<TranscriptUI>();
    public LinkPreviewUI LinkPreviewUI => field ??= Services.GetRequiredService<LinkPreviewUI>();

    public AudioSettings AudioSettings => field ??= Services.GetRequiredService<AudioSettings>();
    public AudioRecorder AudioRecorder => field ??= Services.GetRequiredService<AudioRecorder>();
    public IAudioInitializer AudioInitializer => field ??= Services.GetRequiredService<IAudioInitializer>();
    public IPlaybackFactory PlaybackFactory => field ??= Services.GetRequiredService<IPlaybackFactory>();
    public ActivePlaybackInfo ActivePlaybackInfo => field ??= Services.GetRequiredService<ActivePlaybackInfo>();
    public AudioWidget AudioWidget => field ??= Services.GetRequiredService<AudioWidget>();
    public AudioAttachmentPlayer AudioAttachmentPlayer => field ??= Services.GetRequiredService<AudioAttachmentPlayer>();
    public OptimisticReactions OptimisticReactions => field ??= Services.GetRequiredService<OptimisticReactions>();

    public KeyedFactory<IChatMarkupHub, ChatId> ChatMarkupHubFactory
        => field ??= Services.GetRequiredService<KeyedFactory<IChatMarkupHub, ChatId>>();
    public MarkupHelpers MarkupHelpers => field ??= new MarkupHelpers(this);

    // Some handy helpers
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChatEntryReader NewEntryReader(ChatId chatId)
        => new(Chats, Session, chatId);
}
