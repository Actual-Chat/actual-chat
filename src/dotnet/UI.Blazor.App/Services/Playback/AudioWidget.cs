namespace ActualChat.UI.Blazor.App.Services;

public enum AudioWidgetMode { Replaying, Listening, Recording }
public sealed record AudioWidgetChatInfo(ChatId Id, string Title, string PicUrl, int ExtraChatCount);
public sealed record AudioWidgetState(AudioWidgetMode Mode, AudioWidgetChatInfo Chat, bool IsPaused);

public class AudioWidget : IDisposable
{
    private readonly ComputedState<AudioWidgetState?> _state;
    private AudioWidgetState? _lastState;

    private AppUIHub Hub { get; }
    private Session Session => Hub.Session;
    private IChats Chats => Hub.Chats;
    private IAccounts Accounts => Hub.Accounts;
    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private UrlMapper UrlMapper => Hub.UrlMapper;

    public IState<AudioWidgetState?> State => _state;

    public AudioWidget(AppUIHub hub)
    {
        Hub = hub;
        _state = hub.StateFactory.NewComputed(
            new ComputedState<AudioWidgetState?>.Options() {
                InitialValue = null,
                UpdateDelayer = FixedDelayer.NextTick,
                Category = StateCategories.Get(GetType(), nameof(State)),
            },
            ComputeState);
        _state.Updated += OnStateUpdated;
    }

    public virtual void Dispose()
    {
        _state.Updated -= OnStateUpdated;
        _state.Dispose();
    }

    // Protected methods

    protected virtual void OnStateChanged(AudioWidgetState? state, AudioWidgetState? oldState)
    { }

    protected void InvokeAction(string actionName)
    {
        var replayState = ChatAudioUI.ReplayState.Value;
        if (replayState is not null)
            InvokeReplayAction(replayState, actionName);
        else {
            var state = _state.Value;
            if (state is { Mode: AudioWidgetMode.Listening })
                InvokeListeningAction(state.Chat.Id, actionName);
        }
    }

    // Private methods

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
        int extraChatCount = 0;
        bool isPaused = false;

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

        if (mode is not { } vMode)
            return null;

        var chatInfo = await GetChatInfo(chatId!).ConfigureAwait(false);
        if (extraChatCount > 0)
            chatInfo = chatInfo with { ExtraChatCount = extraChatCount };
        return new AudioWidgetState(vMode, chatInfo, isPaused);
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
