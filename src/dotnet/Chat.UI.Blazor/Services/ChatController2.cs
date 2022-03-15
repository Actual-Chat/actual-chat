using ActualChat.MediaPlayback;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatController2
{
    private readonly ChatPlayers _chatPlayers;
    private readonly ListeningChatsList _listeningChats;
    private readonly MomentClockSet _clocks;
    private readonly ILogger<ChatController2> _logger;

    public ChatController2(ChatPlayers chatPlayers,
        ListeningChatsList listeningChats, MomentClockSet clocks, ILogger<ChatController2> logger)
    {
        _chatPlayers = chatPlayers;
        _listeningChats = listeningChats;
        _clocks = clocks;
        _logger = logger;
    }

    public virtual async Task<bool> IsRealtimeListeningActivated(Symbol chatId, CancellationToken cancellationToken)
    {
        var playbackKind = await GetChatPlaybackKind(chatId, cancellationToken).ConfigureAwait(false);
        return playbackKind == PlaybackKind.Realtime;
    }

    public virtual async Task<PlaybackKind> GetChatPlaybackKind(Symbol chatId, CancellationToken cancellationToken)
    {
        var player = await _chatPlayers.GetPlayer(chatId).ConfigureAwait(false);
        if (player == null)
            return PlaybackKind.None;
        return await player.State.Use(cancellationToken).ConfigureAwait(false);
    }

    public virtual async Task<ChatPlayer?> GetChatPlayer(Symbol chatId, CancellationToken cancellationToken)
    {
        var player = await _chatPlayers.GetPlayer(chatId).ConfigureAwait(false);
        return player;
    }

    public virtual async Task<ChatPlayer?> GetHistoricalChatPlayer(Symbol chatId, CancellationToken cancellationToken)
    {
        var player = await _chatPlayers.GetPlayer(chatId).ConfigureAwait(false);
        if (player == null)
            return null;
        var playbackKind = await player.State.Use(cancellationToken).ConfigureAwait(false);
        if (playbackKind != PlaybackKind.Historical)
            return null;
        return player;
    }

    public virtual async Task StopPlaying(Symbol chatId, PlaybackKind playbackKind)
    {
        var player = await _chatPlayers.GetPlayer(chatId).ConfigureAwait(false);
        if (player == null)
            return;
        var playbackKindCurrent = await player.State.Use(default).ConfigureAwait(false);
        if (playbackKindCurrent != playbackKind)
            return;
        await player.Stop().ConfigureAwait(false);
    }

    public virtual async Task ActivateRealtimeListening(Symbol chatId)
    {
        _listeningChats.Add(chatId);
        var player = _chatPlayers.ActivatePlayer(chatId);
        await StopAndAwait(player).ConfigureAwait(false);
        await player.Play(_clocks.SystemClock.Now, isRealtime: true, default).ConfigureAwait(false);
    }

    public virtual async Task StopRealtimeListening(Symbol chatId)
    {
        _listeningChats.Remove(chatId);
        await StopPlaying(chatId, PlaybackKind.Realtime).ConfigureAwait(false);
    }

    private async Task StopAndAwait(ChatPlayer player)
    {
        await player.Stop().ConfigureAwait(false);
        while (true) {
            var state = await player.State.Use(default).ConfigureAwait(false);
            if (state == PlaybackKind.None)
                break;
        }
    }

    public virtual void StartHistoricalPlayer(Symbol chatId, Moment startAt)
    {
        var historyPlayTask = BackgroundTask.Run(
            () => InnerStartHistoricalPlayer(chatId, startAt),
            _logger, "Historical playback failed");

        // restore real-time playback
        if (_listeningChats.ListeningChats.Contains(chatId)) {
            _ = historyPlayTask.ContinueWith(
                _ => ActivateRealtimeListening(chatId));
        }
    }

    private async Task InnerStartHistoricalPlayer(Symbol chatId, Moment startAt)
    {
        var player = _chatPlayers.ActivatePlayer(chatId);
        await StopAndAwait(player).ConfigureAwait(false);
        await player.Play(startAt, isRealtime: false, default).ConfigureAwait(false);
    }

    public virtual Task StopHistoricalPlayer(Symbol chatId)
        => StopPlaying(chatId, PlaybackKind.Historical);

    public virtual async Task LeaveChat(Symbol chatId)
    {
        await StopHistoricalPlayer(chatId).ConfigureAwait(false);
        if (!_listeningChats.ListeningChats.Contains(chatId))
            _ = _chatPlayers.Close(chatId);
    }
}
