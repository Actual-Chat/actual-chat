namespace ActualChat.UI.Blazor.App.Services;

public enum AudioWidgetMode { Replaying, Listening, Recording }
public sealed record AudioWidgetChatInfo(ChatId Id, string Title, string PicUrl, int ExtraChatCount);
public sealed record AudioWidgetState(AudioWidgetMode Mode, AudioWidgetChatInfo Chat, bool IsPaused);

public class AudioWidget
{
    private readonly Lock _lock = new();
    private readonly MutableState<AudioWidgetState?> _state;
    private Task _lastComputeStateTask = Task.CompletedTask;
    private ReplayState? _replayState;
    private ImmutableHashSet<ChatId>? _listeningChatIds;
    private ChatId? _recordingChatId;

    private IServiceProvider Services { get; }
    private ScopedServicesAccessor ScopedServicesAccessor { get; }
    private ChatAudioUI? ChatAudioUI => ScopedServicesAccessor()?.AppUIHub().ChatAudioUI;
    private Session Session => field ??= Services.GetRequiredService<Session>();
    private IChats Chats => field ??= Services.GetRequiredService<IChats>();
    private IAccounts Accounts => field ??= Services.GetRequiredService<IAccounts>();
    private UrlMapper UrlMapper => field ??= Services.GetRequiredService<UrlMapper>();

    public IState<AudioWidgetState?> State => _state;

    public AudioWidget(IServiceProvider services)
    {
        Services = services;
        ScopedServicesAccessor = services.GetRequiredService<ScopedServicesAccessor>();
        _state = services.StateFactory().NewMutable(
            (AudioWidgetState?)null,
            StateCategories.Get(GetType(), nameof(State)));
    }

    // This method is called to trigger widget reset on UI restart in MAUI
    public void Reset()
    {
        lock (_lock) {
            _replayState = null;
            _listeningChatIds = null;
            _recordingChatId = null;
            SetState(null);
        }
    }

    public void UpdateState()
        => MutateState(null);

    public void OnReplayStateChanged(ReplayState? state)
        => MutateState(() => _replayState = state);

    public void OnListeningStateChanged(ImmutableHashSet<ChatId>? listeningChatIds)
        => MutateState(() => _listeningChatIds = listeningChatIds);

    public void OnRecodingStateChanged(ChatId? recordingChatId)
        => MutateState(() => _recordingChatId = recordingChatId);

    // Protected methods

    protected virtual void OnStateChanged(AudioWidgetState? state, AudioWidgetState? oldState)
    { }

    protected void InvokeAction(string actionName)
    {
        ReplayState? replayState;
        ImmutableHashSet<ChatId>? listeningChatIds;
        lock (_lock) {
            replayState = _replayState;
            listeningChatIds = _listeningChatIds;
        }

        if (replayState is not null)
            InvokeReplayAction(replayState, actionName);
        else if (listeningChatIds is { IsEmpty: false })
            InvokeListeningAction(listeningChatIds, actionName);
    }

    // Private methods

    private void SetState(AudioWidgetState? state)
    {
        lock (_lock) {
            var oldState = _state.Value;
            if (oldState == state)
                return;

            _state.Value = state;
            OnStateChanged(state, oldState);
        }
    }

    private void MutateState(Action? action)
    {
        ChatId? recordingChatId;
        ReplayState? replayState;
        ImmutableHashSet<ChatId>? listeningChatIds;
        Task lastComputeStateTask;
        lock(_lock) {
            action?.Invoke();
            recordingChatId = _recordingChatId;
            replayState = _replayState;
            listeningChatIds = _listeningChatIds;
            lastComputeStateTask = _lastComputeStateTask;
            _lastComputeStateTask = CompleteAsync();
        }
        return;

        async Task CompleteAsync() {
            await lastComputeStateTask.SilentAwait();

            ChatId? chatId = null;
            AudioWidgetMode? mode = null;
            int extraChatCount = 0;
            bool isPaused = false;

            // Priority: Recording > Replaying > Listening
            if (recordingChatId is not null) {
                mode = AudioWidgetMode.Recording;
                chatId = recordingChatId;
            }
            else if (ChatAudioUI is { } chatAudioUI) {
                if (replayState is not null) {
                    chatId = replayState.ChatId;
                    var player = chatAudioUI.GetReplayerNonComputed(chatId);
                    if (player?.Playback.IsPlaying.Value ?? false) {
                        mode = AudioWidgetMode.Replaying;
                        isPaused = player.Playback.IsPaused.Value;
                    }
                }
                else if (listeningChatIds is { IsEmpty: false }) {
                    chatId = listeningChatIds.First();
                    var player = chatAudioUI.GetListenerNonComputed(chatId);
                    if (player?.Playback.IsPlaying.Value ?? false) {
                        mode = AudioWidgetMode.Listening;
                        extraChatCount = listeningChatIds.Count - 1;
                        isPaused = player.Playback.IsPaused.Value;
                    }
                }
            }

            var state = (AudioWidgetState?)null;
            if (mode is { } vMode) {
                var chatInfo = await GetChatInfo(chatId!).ConfigureAwait(false);
                if (extraChatCount > 0)
                    chatInfo = chatInfo with {
                        ExtraChatCount = extraChatCount
                    };
                state = new AudioWidgetState(vMode, chatInfo, isPaused);
            }
            SetState(state);
        }
    }

    private async Task<AudioWidgetChatInfo> GetChatInfo(ChatId chatId)
    {
        var chat = await Chats.Get(Session, chatId, CancellationToken.None).ConfigureAwait(false);
        if (chat is null)
            return new AudioWidgetChatInfo(chatId, "unknown chat", "", 0);

        var picUrl = chat.Picture is not null ? UrlMapper.ContentUrl(chat.Picture.ContentId) : "";

        if (!picUrl.IsNullOrEmpty() || chatId is not PeerChatId peerChatId)
            return new AudioWidgetChatInfo(chatId, chat.Title, picUrl, 0);

        // For peer chats without a picture, use the peer's avatar
        var ownAccount = await Accounts.GetOwn(Session, CancellationToken.None).ConfigureAwait(false);
        var peerUserId = peerChatId.AnotherUserId(ownAccount.Id);
        var peerAccount = await Accounts.Get(Session, peerUserId, CancellationToken.None).ConfigureAwait(false);
        if (peerAccount?.Avatar.Picture?.MediaContent is { } mediaContent)
            picUrl = UrlMapper.ContentUrl(mediaContent.ContentId);

        return new AudioWidgetChatInfo(chatId, chat.Title, picUrl, 0);
    }

    private void InvokeReplayAction(ReplayState state, string actionName)
    {
        var chatAudioUI = ChatAudioUI;
        if (chatAudioUI is null)
            return;

        switch (actionName) {
        case ActionNames.Stop:
            chatAudioUI.StopReplay();
            break;
        case ActionNames.Pause:
            chatAudioUI
                .GetReplayerNonComputed(state.ChatId)
                ?.Pause();
            break;
        case ActionNames.Resume:
            _ = chatAudioUI
                .GetReplayerNonComputed(state.ChatId)
                ?.Resume();
            break;
        }
    }

    private void InvokeListeningAction(ImmutableHashSet<ChatId> listeningChatIds, string actionName)
    {
        var chatAudioUI = ChatAudioUI;
        if (chatAudioUI is null)
            return;

        var chatId = listeningChatIds.First();
        switch (actionName) {
        case ActionNames.Stop:
            _ = chatAudioUI.SetListeningState(chatId, false);
            break;
        case ActionNames.Pause:
            chatAudioUI
                .GetListenerNonComputed(chatId)
                ?.Pause();
            break;
        case ActionNames.Resume:
            _ = chatAudioUI
                .GetListenerNonComputed(chatId)
                ?.Resume();
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
