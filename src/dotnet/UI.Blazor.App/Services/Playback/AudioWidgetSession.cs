using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Services;

public enum AudioWidgetSessionStateMode { HistoricalPlayback, RealtimePlayback, Recording }

public record AudioWidgetSessionChatInfo(ChatId Id, string Title, string PicUri, int ExtraChatCount);
public record AudioWidgetSessionState(AudioWidgetSessionStateMode Mode, AudioWidgetSessionChatInfo Chat, bool IsPaused);

public class AudioWidgetSession(AudioWidgetSessionChatResolver chatResolver, Func<ChatPlayers?> chatPlayersAccessor)
{
    public static class Actions
    {
        public const string Stop = nameof(Stop);
        public const string Resume = nameof(Resume);
        public const string Pause = nameof(Pause);
    }

    private readonly Lock _lock = new ();
    private readonly AsyncLock _asyncLock = new ();
    private PlaybackState? _playbackState;
    private ChatId? _recordingChatId;

    private ChatPlayers? ChatPlayers => chatPlayersAccessor.Invoke();

    public AudioWidgetSessionState? State { get; private set; }
    public event EventHandler? StateChanged;

    public void Reset()
    {
        bool changed;
        lock (_lock) {
            _playbackState = null;
            _recordingChatId = null;
            changed = State is not null;
            State = null;
        }
        if (changed)
            RaiseStateChanged();
    }

    public void OnPlaybackStateChanged(PlaybackState? playbackState)
    {
        lock (_lock)
            _playbackState = playbackState;
        UpdateState();
    }

    public void UpdateMediaSessionState()
        => UpdateState();

    public void OnRecodingStateChanged(ChatId? recordingChatId)
    {
        lock (_lock)
            _recordingChatId = recordingChatId;
        UpdateState();
    }

    private void UpdateState()
    {
        ChatId? recordingChatId;
        PlaybackState? playbackState;
        lock(_lock) {
            recordingChatId = _recordingChatId;
            playbackState = _playbackState;
        }
        _ = UpdateState2(recordingChatId, playbackState).SilentAwait();
    }

    private async Task UpdateState2(ChatId? recordingChatId, PlaybackState? playbackState)
    {
        using var releaser = await _asyncLock.Lock(CancellationToken.None).ConfigureAwait(false);
        releaser.MarkLockedLocally();
        var state = await CalculateState(recordingChatId, playbackState).ConfigureAwait(false);
        if (Equals(State, state))
            return;

        State = state;
        RaiseStateChanged();
    }

    private async Task<AudioWidgetSessionState?> CalculateState(ChatId? recordingChatId, PlaybackState? playbackState)
    {
        ChatId? chatId = null;
        AudioWidgetSessionStateMode? mode = null;
        int extraChatCount = 0;
        bool isPaused = false;
        if (recordingChatId is not null) {
            mode = AudioWidgetSessionStateMode.Recording;
            chatId = recordingChatId;
        }
        else if (playbackState is RealtimePlaybackState realtimePlaybackState) {
            mode = AudioWidgetSessionStateMode.RealtimePlayback;
            chatId = realtimePlaybackState.ChatIds.First();
            extraChatCount = realtimePlaybackState.ChatIds.Count - 1;
        }
        else if (playbackState is HistoricalPlaybackState historicalPlaybackState) {
            mode = AudioWidgetSessionStateMode.HistoricalPlayback;
            chatId = historicalPlaybackState.ChatId;
            var controller = ChatPlayers?.GetHistoricalChatPlayerControllerNonComputed(chatId);
            isPaused = controller?.IsPaused.Value ?? false;
        }
        if (mode is null)
            return null;

        var chatInfo = await chatResolver.Get(chatId!).ConfigureAwait(false);
        if (extraChatCount > 0)
            chatInfo = chatInfo with {
                ExtraChatCount = extraChatCount
            };
        return new AudioWidgetSessionState(mode.Value, chatInfo, isPaused);
    }

    protected virtual void RaiseStateChanged()
        => StateChanged?.Invoke(this, EventArgs.Empty);

    public void InvokeAction(string actionName)
    {
        PlaybackState? playbackState;
        lock (_lock)
            playbackState = _playbackState;

        if (playbackState is HistoricalPlaybackState historicalPlaybackState)
            InvokeHistoricalPlaybackAction(historicalPlaybackState, actionName);
    }

    private void InvokeHistoricalPlaybackAction(HistoricalPlaybackState state, string actionName)
    {
        var chatPlayers = ChatPlayers;
        if (chatPlayers is null)
            return;

        switch (actionName) {
        case Actions.Stop: {
            chatPlayers.StopHistoricalPlayback();
            break;
        }
        case Actions.Pause: {
            chatPlayers
                .GetHistoricalChatPlayerControllerNonComputed(state.ChatId)
                ?.Pause();
            break;
        }
        case Actions.Resume: {
            _ = chatPlayers
                .GetHistoricalChatPlayerControllerNonComputed(state.ChatId)
                ?.Resume();
            break;
        }
        }
    }
}

public class AudioWidgetSessionChatResolver(IServiceProvider services)
{
    private Session Session => field ??= services.GetRequiredService<Session>();
    private IChats Chats => field ??= services.GetRequiredService<IChats>();
    private UrlMapper UrlMapper => field ??= services.GetRequiredService<UrlMapper>();

    public async Task<AudioWidgetSessionChatInfo> Get(ChatId chatId)
    {
        var chat = await Chats.Get(Session, chatId, CancellationToken.None).ConfigureAwait(false);
        if (chat is null)
            return new AudioWidgetSessionChatInfo(chatId, "unknown chat", "", 0);

        var picUrl = chat.Picture is not null ? UrlMapper.ContentUrl(chat.Picture.ContentId) : "";
        return new AudioWidgetSessionChatInfo(chatId, chat.Title, picUrl, 0);
    }
}
