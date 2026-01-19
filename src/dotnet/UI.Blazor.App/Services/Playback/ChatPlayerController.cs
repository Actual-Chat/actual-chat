namespace ActualChat.UI.Blazor.App.Services;

public class ChatPlayerController(ChatPlayer chatPlayer, ChatPlayers chatPlayers) : IAsyncDisposable
{
    protected ChatPlayers ChatPlayers { get; } = chatPlayers;
    public ChatPlayer ChatPlayer { get; } = chatPlayer;
    public ChatId ChatId => ChatPlayer.ChatId;
    public IState<bool> IsPaused => ChatPlayer.Playback.IsPaused;

    public ValueTask DisposeAsync()
        => ChatPlayer.DisposeAsync();
}

public class HistoricalChatPlayerController(HistoricalChatPlayer chatPlayer, ChatPlayers chatPlayers, ILogger log)
    : ChatPlayerController(chatPlayer, chatPlayers)
{
    private ILogger Log { get; } = log;

    public new HistoricalChatPlayer ChatPlayer { get; } = chatPlayer;

    public void Pause()
    {
        _ = ChatPlayer.Playback.Pause(default);
        ChatPlayers.ReleaseAudioFocusDueToPause(this);
        ChatPlayers.UpdateMediaSessionState();
    }

    public async Task Resume()
    {
        var playbackState = ChatPlayers.PlaybackState.Value;
        if (playbackState is not HistoricalPlaybackState historicalPlaybackState
            || historicalPlaybackState.ChatId != ChatId) {
            Log.LogInformation("Can't resume historical playback. State: '{State}', ChatId: '{ChatId}'", playbackState, ChatId);
            return;
        }

        if (!await ChatPlayers.TryGainAudioFocusForResume(this).ConfigureAwait(false))
            return;

        _ = ChatPlayer.Playback.Resume(default);
        ChatPlayers.UpdateMediaSessionState();
    }
}

public class RealtimeChatPlayerController(RealtimeChatPlayer chatPlayer, ChatPlayers chatPlayers, ILogger log)
    : ChatPlayerController(chatPlayer, chatPlayers)
{
    private ILogger Log { get; } = log;

    public new RealtimeChatPlayer ChatPlayer { get; } = chatPlayer;

    public void Pause()
    {
        _ = ChatPlayer.Playback.Pause(default);
        ChatPlayers.ReleaseAudioFocusDueToPause(this);
        ChatPlayers.UpdateMediaSessionState();
    }

    public async Task Resume()
    {
        var playbackState = ChatPlayers.PlaybackState.Value;
        if (playbackState is not RealtimePlaybackState realtimePlaybackState
            || !realtimePlaybackState.ChatIds.Contains(ChatId)) {
            Log.LogInformation("Can't resume realtime playback. State: '{State}', ChatId: '{ChatId}'", playbackState, ChatId);
            return;
        }

        if (!await ChatPlayers.TryGainAudioFocusForResume(this).ConfigureAwait(false))
            return;

        _ = ChatPlayer.Playback.Resume(default);
        ChatPlayers.UpdateMediaSessionState();
    }
}
