using ActualChat.MediaPlayback;
using System.Linq;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum RealtimeListeningMode { None, Active, Muted }

public class ChatController
{
    private readonly ChatPlayers _chatPlayers;
    private readonly ListeningChats _listeningChats;
    private readonly MomentClockSet _clocks;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        ChatPlayers chatPlayers,
        ListeningChats listeningChats,
        MomentClockSet clocks,
        ILogger<ChatController> logger)
    {
        _chatPlayers = chatPlayers;
        _listeningChats = listeningChats;
        _clocks = clocks;
        _logger = logger;
    }

    [ComputeMethod]
    public virtual async Task<RealtimeListeningMode> GetRealtimeListeningMode(Symbol chatId, CancellationToken cancellationToken)
    {
        var chatIds = await _listeningChats.GetChatIds().ConfigureAwait(false);
        if (!chatIds.Contains(chatId))
            return RealtimeListeningMode.None;
        var playbackKind = await GetChatPlaybackKind(chatId, cancellationToken).ConfigureAwait(false);
        switch (playbackKind) {
            case PlaybackKind.None:
                return RealtimeListeningMode.Muted;
            case PlaybackKind.Realtime:
                return RealtimeListeningMode.Active;
            case PlaybackKind.Historical:
                return RealtimeListeningMode.None;
            default:
                throw new NotSupportedException(playbackKind.ToString());
        }
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

    public virtual Task StartRealtimeListening(Symbol chatId)
        => StartRealtimeListening(chatId, ListenChatMode.Active);

    public virtual async Task StartRealtimeListening(Symbol chatId, ListenChatMode mode)
    {
        _listeningChats.Set(chatId, mode);
        if (mode == ListenChatMode.Active)
            await InnerStartRealtimeListening(chatId).ConfigureAwait(false);
    }

    public virtual async Task StopRealtimeListening(Symbol chatId)
    {
        _listeningChats.Remove(chatId);
        await InnerStopRealtimeListening(chatId).ConfigureAwait(false);
    }

    public virtual async Task MuteRealtimeListening(Symbol chatId)
    {
        if (!await IsListeningToChat(chatId).ConfigureAwait(false))
            return;
        _listeningChats.Set(chatId, ListenChatMode.Muted);
        await InnerMuteRealtimeListening(chatId).ConfigureAwait(false);
    }

    public virtual async Task UnmuteRealtimeListening(Symbol chatId)
    {
        if (!await IsListeningToChat(chatId).ConfigureAwait(false))
            return;
        _listeningChats.Set(chatId, ListenChatMode.Active);
        await InnerUnmuteRealtimeListening(chatId).ConfigureAwait(false);
    }

    public async Task FocusRealtimeListening(Symbol focusChatId)
    {
        var chatIds = await _listeningChats.GetChatIds().ConfigureAwait(false);
        if (!chatIds.Contains(focusChatId))
            return;

        var tasks = new List<Task>();
        foreach (var chatId in chatIds) {
            tasks.Add(
                chatId == focusChatId
                    ? InnerUnmuteRealtimeListening(chatId)
                    : InnerMuteRealtimeListening(chatId)
            );
        }
        await Task.WhenAll(tasks.ToArray()).ConfigureAwait(false);
    }

    public virtual async Task StartHistoricalPlayer(Symbol chatId, Moment startAt)
    {
        async Task InnerStartHistoricalPlayer(Symbol chatId, Moment startAt)
        {
            var player = _chatPlayers.ActivatePlayer(chatId);
            await EnsureStopped(player).ConfigureAwait(false);
            await player.Play(startAt, isRealtime: false, default).ConfigureAwait(false);
        }

        var historyPlayTask = BackgroundTask.Run(
            () => InnerStartHistoricalPlayer(chatId, startAt),
            _logger, "Historical playback failed");

        // restore real-time playback
        if (await IsListeningToChat(chatId).ConfigureAwait(false)) {
            _ = historyPlayTask.ContinueWith(
                _ => StartRealtimeListening(chatId), TaskScheduler.Current);
        }
    }

    public virtual Task StopHistoricalPlayer(Symbol chatId)
        => StopPlaying(chatId, PlaybackKind.Historical);

    public virtual async Task LeaveChat(Symbol chatId)
    {
        await StopHistoricalPlayer(chatId).ConfigureAwait(false);
        if (!await IsListeningToChat(chatId).ConfigureAwait(false))
            _ = _chatPlayers.Close(chatId);
    }

    private async Task StopPlaying(Symbol chatId, PlaybackKind playbackKind)
    {
        var player = await _chatPlayers.GetPlayer(chatId).ConfigureAwait(false);
        if (player == null)
            return;
        var playbackKindCurrent = await player.State.Use(default).ConfigureAwait(false);
        if (playbackKindCurrent != playbackKind)
            return;
        await player.Stop().ConfigureAwait(false);
    }

    private async Task EnsureStopped(ChatPlayer player)
    {
        // This method should be called before calling play method.
        // Play method can do stopping automatically but this can lead invalid
        // PlaybackKind of the chat player. Apparently this had to be fixed in ChatPlayer.
        // But for me it's easier to fix here for now.
        var computed = await player.State.Computed.Update().ConfigureAwait(false);
        if (computed.ValueOrDefault == PlaybackKind.None)
            return;
        await player.Stop().ConfigureAwait(false);
        while (computed.ValueOrDefault != PlaybackKind.None) {
            await computed.WhenInvalidated().ConfigureAwait(false);
            computed = await computed.Update().ConfigureAwait(false);
        }
    }

    private Task InnerStartRealtimeListening(Symbol chatId)
    {
        async Task RealtimeListening(Symbol chatId)
        {
            var player = _chatPlayers.ActivatePlayer(chatId);
            await EnsureStopped(player).ConfigureAwait(false);
            await player.Play(_clocks.SystemClock.Now, isRealtime: true, default).ConfigureAwait(false);
        }

        var realtimePlayTask = BackgroundTask.Run(
            () => RealtimeListening(chatId),
            _logger, "Realtime playback failed");

        return Task.CompletedTask;
    }

    [ComputeMethod]
    protected virtual async Task<PlaybackKind> GetChatPlaybackKind(Symbol chatId, CancellationToken cancellationToken)
    {
        var player = await _chatPlayers.GetPlayer(chatId).ConfigureAwait(false);
        if (player == null)
            return PlaybackKind.None;
        return await player.State.Use(cancellationToken).ConfigureAwait(false);
    }

    private async Task InnerMuteRealtimeListening(Symbol chatId)
        => await InnerStopRealtimeListening(chatId).ConfigureAwait(false);

    private async Task InnerUnmuteRealtimeListening(Symbol chatId)
    {
        var playbackKind = await GetChatPlaybackKind(chatId, default).ConfigureAwait(false);
        if (playbackKind == PlaybackKind.Realtime)
            return;
        await InnerStartRealtimeListening(chatId).ConfigureAwait(false);
    }

    private async Task InnerStopRealtimeListening(Symbol chatId)
        => await StopPlaying(chatId, PlaybackKind.Realtime).ConfigureAwait(false);

    private async Task<bool> IsListeningToChat(Symbol chatId)
    {
        var chatIds = await _listeningChats.GetChatIds().ConfigureAwait(false);
        return chatIds.Contains(chatId);
    }
}
