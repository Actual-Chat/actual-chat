using ActualChat.UI.Blazor.App.Services.Gestures;
using ActualChat.UI.Blazor.Services;
using ActivityKind = ActualChat.UI.Blazor.Services.ActivityKind;

namespace ActualChat.UI.Blazor.App.Services;

public class AudioActivitySource : IActivitySource, IDisposable, IHasDisposeStatus
{
    private static readonly TimeSpan AnswerWindowExpiryDelay = TimeSpan.FromMilliseconds(250);

    private readonly bool _isAndroidHost;
    private bool _isDisposed;

    private AppUIHub Hub { get; }
    private Session Session => Hub.Session;
    private IChats Chats => Hub.Chats;
    private IAccounts Accounts => Hub.Accounts;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private GestureUI GestureUI => Hub.GestureUI;
    private UrlMapper UrlMapper => Hub.UrlMapper;
    public bool IsDisposed => _isDisposed;

    public AudioActivitySource(AppUIHub hub)
    {
        Hub = hub;
        _isAndroidHost = hub.HostInfo.AppKind == AppKind.Android;
        if (_isAndroidHost) {
            IncomingVoiceActivityUI.IncomingVoiceStamped += OnIncomingVoiceStamped;
            GestureUI.StartGestureReadyChanged += OnStartGestureReadyChanged;
        }
    }

    public void Dispose()
    {
        _isDisposed = true;
        if (_isAndroidHost) {
            IncomingVoiceActivityUI.IncomingVoiceStamped -= OnIncomingVoiceStamped;
            GestureUI.StartGestureReadyChanged -= OnStartGestureReadyChanged;
        }
    }

    [ComputeMethod]
    public virtual async Task<ActivityInfo?> GetActivity(CancellationToken cancellationToken)
    {
        ChatId? chatId = null;
        ActivityKind? kind = null;
        var extraChatCount = 0;
        var isPaused = false;
        var pttChatIds = _isAndroidHost
            ? await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false)
            : [];

        // Priority: Recording > Replaying > Listening
        var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
        if (recordingChatId is not null) {
            kind = ActivityKind.Recording;
            chatId = recordingChatId;
        }
        else {
            var replayState = await ChatAudioUI.ReplayState.Use(cancellationToken).ConfigureAwait(false);
            if (replayState is not null) {
                chatId = replayState.ChatId;
                var player = await ChatAudioUI.GetReplayPlayer(chatId, cancellationToken).ConfigureAwait(false);
                if (player is not null) {
                    var isPlaying = await player.Playback.IsPlaying.Use(cancellationToken).ConfigureAwait(false);
                    if (isPlaying) {
                        kind = ActivityKind.Replaying;
                        isPaused = await player.Playback.IsPaused.Use(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else {
                // Arming keeps a chat listening for as long as it stays armed, so such a listen is
                // ambient state - counting it as a Listening activity outranks Armed and hides the
                // PTT state (ready / flip-to-reply / countdown) this notification exists to report.
                var listeningChatIds = (await ChatAudioUI.GetListeningChatIds().ConfigureAwait(false))
                    .Where(x => !pttChatIds.Contains(x))
                    .ToList();
                if (listeningChatIds.Count != 0) {
                    chatId = listeningChatIds.First();
                    var player = await ChatAudioUI.GetListeningPlayer(chatId, cancellationToken).ConfigureAwait(false);
                    if (player is not null) {
                        var isPlaying = await player.Playback.IsPlaying.Use(cancellationToken).ConfigureAwait(false);
                        if (isPlaying) {
                            kind = ActivityKind.Listening;
                            extraChatCount = listeningChatIds.Count - 1;
                            isPaused = await player.Playback.IsPaused.Use(cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        var canPause = true;
        Moment? answerWindowEndsAt = null;
        var isStartGestureReady = false;
        if (kind is not { } vKind) {
            var answerWindow = _isAndroidHost
                ? await Hub.UserSettingsUI.UserPttSettings()
                    .Get(x => x.AnswerWindow, cancellationToken)
                    .ConfigureAwait(false)
                : Constants.Audio.PttAnswerWindowDefault;
            if (GetArmedChat(pttChatIds, answerWindow) is not { } armed)
                return null;

            // Nothing plays while merely armed, so there is no player a Pause could reach.
            vKind = ActivityKind.Armed;
            chatId = armed.ChatId;
            extraChatCount = armed.ExtraChatCount;
            answerWindowEndsAt = armed.AnswerWindowEndsAt;
            isStartGestureReady = GestureUI.IsStartGestureReady;
            canPause = false;
        }

        var chatInfo = await GetChatInfo(chatId!).ConfigureAwait(false);
        if (extraChatCount > 0)
            chatInfo = chatInfo with { ExtraChatCount = extraChatCount };

        return new AudioActivity(
            vKind, chatInfo, isPaused, canPause, answerWindowEndsAt, isStartGestureReady);
    }

    public static (ChatId ChatId, int ExtraChatCount, Moment? AnswerWindowEndsAt)? ResolveArmedChat(
        IReadOnlyList<ChatId> pttChatIds,
        IReadOnlyDictionary<ChatId, Moment> lastIncomingVoiceAt,
        Moment now,
        TimeSpan answerWindow)
    {
        if (pttChatIds.Count == 0)
            return null;

        var extraChatCount = pttChatIds.Count - 1;
        var answer = GestureActivationPolicy.GetAnswerWindowChat(
            pttChatIds, lastIncomingVoiceAt, now, answerWindow);
        return answer is { } vAnswer
            ? (vAnswer.ChatId, extraChatCount, vAnswer.At + answerWindow)
            : (pttChatIds[0], extraChatCount, null);
    }

    // Private methods

    private void OnStartGestureReadyChanged()
    {
        // Same reason as the stamps below: readiness lives in GestureUI's loop, so nothing here
        // invalidates when a flip stops (or starts) being able to open the mic.
        using (Invalidation.Begin())
            _ = GetActivity(default);
    }

    private void OnIncomingVoiceStamped()
    {
        // The stamps are a plain dictionary, so a stamp landing or being cleared invalidates
        // nothing on its own - and the answer-window state depends on both.
        using (Invalidation.Begin())
            _ = GetActivity(default);
    }

    private (ChatId ChatId, int ExtraChatCount, Moment? AnswerWindowEndsAt)? GetArmedChat(
        List<ChatId> pttChatIds, TimeSpan answerWindow)
    {
        // Android only, and the reason is the foreground service the backend drives: Android grants
        // microphone access on the serviceType of the last startForeground call, and only if the app
        // wasn't in the background when it ran. A service first started by a wake therefore can never
        // record - so while any chat is armed the service stays up, started while the app is visible.
        // It also keeps the media session (the headset button) alive across the answer window.
        var now = Hub.Clocks.ServerClock.Now;
        var lastIncomingVoiceAt = IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
        var armed = ResolveArmedChat(pttChatIds, lastIncomingVoiceAt, now, answerWindow);
        if (armed is not { AnswerWindowEndsAt: { } endsAt })
            return armed;

        // Nothing invalidates this state when the window merely lapses, and without the
        // auto-invalidation the source would keep naming the answering chat forever.
        var expiresIn = endsAt - now + AnswerWindowExpiryDelay;
        Computed.GetCurrent().Invalidate(expiresIn, false);
        return armed;
    }

    private async Task<ActivityChatInfo> GetChatInfo(ChatId chatId)
    {
        var chat = await Chats.Get(Session, chatId, CancellationToken.None).ConfigureAwait(false);
        if (chat is null)
            return new ActivityChatInfo(chatId, "unknown chat", "", 0);

        var picUrl = chat.Picture is not null ? UrlMapper.ContentUrl(chat.Picture.BlobId) : "";
        if (!picUrl.IsNullOrEmpty() || chatId is not PeerChatId peerChatId)
            return new ActivityChatInfo(chatId, chat.Title, picUrl, 0);

        // For peer chats without a picture, use the peer's avatar
        var ownAccount = await Accounts.GetOwn(Session, CancellationToken.None).ConfigureAwait(false);
        var peerUserId = peerChatId.AnotherUserId(ownAccount.Id);
        var peerAccount = await Accounts.Get(Session, peerUserId, CancellationToken.None).ConfigureAwait(false);
        if (peerAccount?.Avatar.Picture?.MediaRef is { } mediaRef)
            picUrl = UrlMapper.ContentUrl(mediaRef.BlobId);

        return new ActivityChatInfo(chatId, chat.Title, picUrl, 0);
    }
}
