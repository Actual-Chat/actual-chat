using ActualLab.Locking;

namespace ActualChat.UI.Blazor.App.Services;

public enum AudioWidgetSessionStateMode { HistoricalPlayback, RealtimePlayback, Recording }

public record AudioWidgetSessionChatInfo(ChatId Id, string Title, string PicUri, int ExtraChatCount);
public record AudioWidgetSessionState(AudioWidgetSessionStateMode Mode, AudioWidgetSessionChatInfo Chat, bool IsPaused);

public sealed class AudioWidgetSession(
    AudioWidgetSessionChatResolver chatResolver,
    Func<ChatPlayers?> chatPlayersAccessor)
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
            StateChanged?.Invoke(this, EventArgs.Empty);
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
        _ = CompleteAsync();
        return;

        async Task CompleteAsync() {
            using var releaser = await _asyncLock.Lock(CancellationToken.None).ConfigureAwait(false);
            releaser.MarkLockedLocally();
            var state = await CalculateState(recordingChatId, playbackState).ConfigureAwait(false);
            if (Equals(State, state))
                return;

            State = state;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
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
            chatId = realtimePlaybackState.ChatIds.First();
            var player = ChatPlayers?.GetRealtimeChatPlayerNonComputed(chatId);
            if (player?.Playback.IsPlaying.Value ?? false) {
                mode = AudioWidgetSessionStateMode.RealtimePlayback;
                extraChatCount = realtimePlaybackState.ChatIds.Count - 1;
                isPaused = player.IsPaused.Value;
            }
        }
        else if (playbackState is HistoricalPlaybackState historicalPlaybackState) {
            chatId = historicalPlaybackState.ChatId;
            var player = ChatPlayers?.GetHistoricalChatPlayerNonComputed(chatId);
            if (player?.Playback.IsPlaying.Value ?? false) {
                mode = AudioWidgetSessionStateMode.HistoricalPlayback;
                isPaused = player.IsPaused.Value;
            }
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

    public void InvokeAction(string actionName)
    {
        PlaybackState? playbackState;
        lock (_lock)
            playbackState = _playbackState;

        if (playbackState is HistoricalPlaybackState historicalPlaybackState)
            InvokeHistoricalPlaybackAction(historicalPlaybackState, actionName);
        else if (playbackState is RealtimePlaybackState realtimePlaybackState)
            InvokeRealtimePlaybackAction(realtimePlaybackState, actionName);
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
                .GetHistoricalChatPlayerNonComputed(state.ChatId)
                ?.Pause();
            break;
        }
        case Actions.Resume: {
            _ = chatPlayers
                .GetHistoricalChatPlayerNonComputed(state.ChatId)
                ?.Resume();
            break;
        }
        }
    }

    private void InvokeRealtimePlaybackAction(RealtimePlaybackState state, string actionName)
    {
        var chatPlayers = ChatPlayers;
        if (chatPlayers is null)
            return;

        var chatId = state.ChatIds.First();
        switch (actionName) {
        case Actions.Stop: {
            chatPlayers.StopRealtimePlayback();
            break;
        }
        case Actions.Pause: {
            chatPlayers
                .GetRealtimeChatPlayerNonComputed(chatId)
                ?.Pause();
            break;
        }
        case Actions.Resume: {
            _ = chatPlayers
                .GetRealtimeChatPlayerNonComputed(chatId)
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
    private IAccounts Accounts => field ??= services.GetRequiredService<IAccounts>();
    private UrlMapper UrlMapper => field ??= services.GetRequiredService<UrlMapper>();

    public async Task<AudioWidgetSessionChatInfo> Get(ChatId chatId)
    {
        var chat = await Chats.Get(Session, chatId, CancellationToken.None).ConfigureAwait(false);
        if (chat is null)
            return new AudioWidgetSessionChatInfo(chatId, "unknown chat", "", 0);

        var picUrl = chat.Picture is not null ? UrlMapper.ContentUrl(chat.Picture.ContentId) : "";

        if (!picUrl.IsNullOrEmpty() || chatId is not PeerChatId peerChatId)
            return new AudioWidgetSessionChatInfo(chatId, chat.Title, picUrl, 0);

        // For peer chats without a picture, use the peer's avatar
        var ownAccount = await Accounts.GetOwn(Session, CancellationToken.None).ConfigureAwait(false);
        var peerUserId = peerChatId.AnotherUserId(ownAccount.Id);
        var peerAccount = await Accounts.Get(Session, peerUserId, CancellationToken.None).ConfigureAwait(false);
        if (peerAccount?.Avatar.Picture?.MediaContent is { } mediaContent)
            picUrl = UrlMapper.ContentUrl(mediaContent.ContentId);

        return new AudioWidgetSessionChatInfo(chatId, chat.Title, picUrl, 0);
    }
}
