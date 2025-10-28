namespace ActualChat.UI.Blazor.App.Services;

public class ChatPlayerController(ChatPlayer chatPlayer, ChatPlayers chatPlayers) : IAsyncDisposable
{
    protected ChatPlayers ChatPlayers { get; } = chatPlayers;
    public ChatPlayer ChatPlayer { get; } = chatPlayer;

    public ValueTask DisposeAsync()
        => ChatPlayer.DisposeAsync();
}

public class HistoricalChatPlayerController(HistoricalChatPlayer chatPlayer, ChatPlayers chatPlayers, ILogger log)
    : ChatPlayerController(chatPlayer, chatPlayers)
{
    private ILogger Log { get; } = log;

    public new HistoricalChatPlayer ChatPlayer { get; } = chatPlayer;
    public ChatId ChatId => ChatPlayer.ChatId;

    public void Pause()
    {
        _ = ChatPlayer.Playback.Pause(default);
        ChatPlayers.ReleaseAudioFocusDueToPause(this);
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
    }
}
