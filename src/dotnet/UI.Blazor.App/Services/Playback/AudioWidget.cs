using ActualChat.UI.Blazor.App.Services.Gestures;

namespace ActualChat.UI.Blazor.App.Services;

// Armed is the idle walkie state: nothing plays, but the foreground service must stay up - see
// AudioWidget.GetArmedChat. Append only: the value crosses to Android as an intent extra.
public enum AudioWidgetMode { Replaying, Listening, Recording, Armed }
public sealed record AudioWidgetChatInfo(ChatId Id, string Title, string PicUrl, int ExtraChatCount);
public sealed record AudioWidgetState(
    AudioWidgetMode Mode, AudioWidgetChatInfo Chat, bool IsPaused, bool CanPause = true);

public class AudioWidget : IDisposable
{
    private static readonly TimeSpan AnswerWindowExpiryDelay = TimeSpan.FromMilliseconds(250);

    private readonly ComputedState<AudioWidgetState?> _state;
    private readonly bool _isAndroidHost;
    private AudioWidgetState? _lastState;

    private AppUIHub Hub { get; }
    private Session Session => Hub.Session;
    private IChats Chats => Hub.Chats;
    private IAccounts Accounts => Hub.Accounts;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private IncomingVoiceActivityUI IncomingVoiceActivityUI => Hub.IncomingVoiceActivityUI;
    private UrlMapper UrlMapper => Hub.UrlMapper;

    public IState<AudioWidgetState?> State => _state;

    public AudioWidget(AppUIHub hub)
    {
        Hub = hub;
        _isAndroidHost = hub.HostInfo.AppKind == AppKind.Android;
        _state = hub.StateFactory.NewComputed(
            new ComputedState<AudioWidgetState?>.Options() {
                InitialValue = null,
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), nameof(State)),
            },
            ComputeState);
        _state.Updated += OnStateUpdated;
        if (_isAndroidHost)
            IncomingVoiceActivityUI.IncomingVoiceStamped += OnIncomingVoiceStamped;
    }

    public virtual void Dispose()
    {
        if (_isAndroidHost)
            IncomingVoiceActivityUI.IncomingVoiceStamped -= OnIncomingVoiceStamped;
        _state.Updated -= OnStateUpdated;
        _state.Dispose();
    }

    // Protected methods

    protected virtual void OnStateChanged(AudioWidgetState? state, AudioWidgetState? oldState)
    { }

    protected virtual void OnArmedChanged(bool isArmed)
    { }

    protected void InvokeAction(string actionName)
    {
        // Routes on what the notification is showing, not on ReplayState: a replay state whose
        // player isn't playing leaves ComputeState showing something else entirely.
        if (_state.Value is not { } state)
            return;

        switch (state.Mode) {
        case AudioWidgetMode.Replaying:
            if (ChatAudioUI.ReplayState.Value is { } replayState)
                InvokeReplayAction(replayState, actionName);
            break;
        case AudioWidgetMode.Listening:
            InvokeListeningAction(state.Chat.Id, actionName);
            break;
        }
    }

    // Private methods

    private void OnIncomingVoiceStamped()
    {
        // The stamps are a plain dictionary, so a stamp landing or being cleared invalidates
        // nothing on its own - and the answer-window state depends on both.
        _state.Invalidate();
    }

    private void OnStateUpdated(State state, StateEventKind eventKind)
    {
        if (eventKind != StateEventKind.Updated)
            return;

        var newState = _state.Value;
        var oldState = _lastState;
        if (newState == oldState)
            return;

        _lastState = newState;
        OnStateChanged(newState, oldState);
    }

    private async Task<AudioWidgetState?> ComputeState(CancellationToken cancellationToken)
    {
        ChatId? chatId = null;
        AudioWidgetMode? mode = null;
        var extraChatCount = 0;
        var isPaused = false;

        // Read on every recompute, not just in the Armed branch below: the host mirrors it so the
        // next launch can raise the foreground service before any of this state exists.
        var pttChatIds = _isAndroidHost
            ? await ChatAudioUI.GetPttChatIds(cancellationToken).ConfigureAwait(false)
            : [];
        OnArmedChanged(pttChatIds.Count > 0);

        // Priority: Recording > Replaying > Listening
        var recordingChatId = await ChatAudioUI.GetRecordingChatId().ConfigureAwait(false);
        if (recordingChatId is not null) {
            mode = AudioWidgetMode.Recording;
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
                        mode = AudioWidgetMode.Replaying;
                        isPaused = await player.Playback.IsPaused.Use(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            else {
                var listeningChatIds = await ChatAudioUI.GetListeningChatIds().ConfigureAwait(false);
                if (!listeningChatIds.IsEmpty) {
                    chatId = listeningChatIds.First();
                    var player = await ChatAudioUI.GetListeningPlayer(chatId, cancellationToken).ConfigureAwait(false);
                    if (player is not null) {
                        var isPlaying = await player.Playback.IsPlaying.Use(cancellationToken).ConfigureAwait(false);
                        if (isPlaying) {
                            mode = AudioWidgetMode.Listening;
                            extraChatCount = listeningChatIds.Count - 1;
                            isPaused = await player.Playback.IsPaused.Use(cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        }

        var canPause = true;
        if (mode is not { } vMode) {
            if (GetArmedChat(pttChatIds) is not { } armed)
                return null;

            // Nothing plays while merely armed, so there is no player a Pause could reach.
            vMode = AudioWidgetMode.Armed;
            chatId = armed.ChatId;
            extraChatCount = armed.ExtraChatCount;
            canPause = false;
        }

        var chatInfo = await GetChatInfo(chatId!).ConfigureAwait(false);
        if (extraChatCount > 0)
            chatInfo = chatInfo with { ExtraChatCount = extraChatCount };

        return new AudioWidgetState(vMode, chatInfo, isPaused, canPause);
    }

    private (ChatId ChatId, int ExtraChatCount)? GetArmedChat(List<ChatId> pttChatIds)
    {
        // Android only, and the reason is the foreground service this widget drives: Android grants
        // microphone access on the serviceType of the last startForeground call, and only if the app
        // wasn't in the background when it ran. A service first started by a wake therefore can never
        // record - so while any chat is armed the service stays up, started while the app is visible.
        // It also keeps the media session (the headset button) alive across the answer window.
        if (pttChatIds.Count == 0)
            return null;

        var extraChatCount = pttChatIds.Count - 1;
        var now = Hub.Clocks.ServerClock.Now;
        var recencyWindow = Constants.Audio.WalkieTalkieReplyRecencyWindow;
        var lastIncomingVoiceAt = IncomingVoiceActivityUI.SnapshotLastIncomingVoiceAt();
        var answer = GestureActivationPolicy.GetAnswerWindowChat(
            pttChatIds, lastIncomingVoiceAt, now, recencyWindow);
        if (answer is not { } vAnswer)
            return (pttChatIds[0], extraChatCount);

        // Nothing invalidates this state when the window merely lapses, and without the
        // auto-invalidation the widget would keep naming the answering chat forever.
        var expiresIn = vAnswer.At + recencyWindow - now + AnswerWindowExpiryDelay;
        Computed.GetCurrent().Invalidate(expiresIn, false);
        return (vAnswer.ChatId, extraChatCount);
    }

    private async Task<AudioWidgetChatInfo> GetChatInfo(ChatId chatId)
    {
        var chat = await Chats.Get(Session, chatId, CancellationToken.None).ConfigureAwait(false);
        if (chat is null)
            return new AudioWidgetChatInfo(chatId, "unknown chat", "", 0);

        var picUrl = chat.Picture is not null ? UrlMapper.ContentUrl(chat.Picture.BlobId) : "";
        if (!picUrl.IsNullOrEmpty() || chatId is not PeerChatId peerChatId)
            return new AudioWidgetChatInfo(chatId, chat.Title, picUrl, 0);

        // For peer chats without a picture, use the peer's avatar
        var ownAccount = await Accounts.GetOwn(Session, CancellationToken.None).ConfigureAwait(false);
        var peerUserId = peerChatId.AnotherUserId(ownAccount.Id);
        var peerAccount = await Accounts.Get(Session, peerUserId, CancellationToken.None).ConfigureAwait(false);
        if (peerAccount?.Avatar.Picture?.MediaRef is { } mediaRef)
            picUrl = UrlMapper.ContentUrl(mediaRef.BlobId);

        return new AudioWidgetChatInfo(chatId, chat.Title, picUrl, 0);
    }

    private void InvokeReplayAction(ReplayState state, string actionName)
    {
        switch (actionName) {
        case ActionNames.Stop:
            ChatAudioUI.StopReplay();
            break;
        case ActionNames.Pause:
            ChatAudioUI.GetReplayPlayerNonComputed(state.ChatId)?.Pause();
            break;
        case ActionNames.Resume:
            _ = ChatAudioUI.GetReplayPlayerNonComputed(state.ChatId)?.Resume();
            break;
        }
    }

    private void InvokeListeningAction(ChatId chatId, string actionName)
    {
        switch (actionName) {
        case ActionNames.Stop:
            IncomingVoiceActivityUI.ClearIncomingVoice(chatId);
            _ = ChatAudioUI.SetListeningState(chatId, false);
            break;
        case ActionNames.Pause:
            ChatAudioUI.GetListeningPlayerNonComputed(chatId)?.Pause();
            break;
        case ActionNames.Resume:
            _ = ChatAudioUI.GetListeningPlayerNonComputed(chatId)?.Resume();
            break;
        }
    }

    // Nested types

    protected static class ActionNames
    {
        public const string Stop = nameof(Stop);
        public const string Resume = nameof(Resume);
        public const string Pause = nameof(Pause);
    }
}
