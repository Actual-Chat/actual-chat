﻿using ActualChat.MediaPlayback;
using System.Linq;

namespace ActualChat.Chat.UI.Blazor.Services;

public enum RealtimeListeningMode { None, Active, Muted }

public class ChatController
{
    private readonly ChatPlayers _chatPlayers;
    private readonly ListeningChatsList _listeningChats;
    private readonly MomentClockSet _clocks;
    private readonly ILogger<ChatController> _logger;

    public ChatController(ChatPlayers chatPlayers,
        ListeningChatsList listeningChats, MomentClockSet clocks, ILogger<ChatController> logger)
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

    public virtual async Task<RealtimeListeningMode> GetRealtimeListeningMode(Symbol chatId, CancellationToken cancellationToken)
    {
        var chatIds = await _listeningChats.GetChatIds(cancellationToken).ConfigureAwait(false);
        if (!chatIds.Contains((string)chatId, StringComparer.Ordinal))
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

    public virtual async Task<PlaybackKind> GetChatPlaybackKind(Symbol chatId, CancellationToken cancellationToken)
    {
        var player = await _chatPlayers.GetPlayer(chatId).ConfigureAwait(false);
        if (player == null)
            return PlaybackKind.None;
        return await player.State.Use(cancellationToken).ConfigureAwait(false);
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

    public virtual async Task ActivateRealtimeListening(Symbol chatId)
    {
        _listeningChats.Add(chatId);
        await InnerActivateRealtimeListening(chatId).ConfigureAwait(false);
    }

    public virtual async Task StopRealtimeListening(Symbol chatId)
    {
        _listeningChats.Remove(chatId);
        await StopPlaying(chatId, PlaybackKind.Realtime).ConfigureAwait(false);
    }

    public virtual async Task MuteRealtimeListening(Symbol chatId)
    {
        if (!IsListeningToChat(chatId))
            return;
        await StopPlaying(chatId, PlaybackKind.Realtime).ConfigureAwait(false);
    }

    public virtual async Task UnmuteRealtimeListening(Symbol chatId)
    {
        if (!IsListeningToChat(chatId))
            return;
        await InnerActivateRealtimeListening(chatId).ConfigureAwait(false);
    }

    public virtual void StartHistoricalPlayer(Symbol chatId, Moment startAt)
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
        if (!IsListeningToChat(chatId)) {
            _ = historyPlayTask.ContinueWith(
                _ => ActivateRealtimeListening(chatId));
        }
    }

    public virtual Task StopHistoricalPlayer(Symbol chatId)
        => StopPlaying(chatId, PlaybackKind.Historical);

    public virtual async Task LeaveChat(Symbol chatId)
    {
        await StopHistoricalPlayer(chatId).ConfigureAwait(false);
        if (!IsListeningToChat(chatId))
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
        await player.Stop().ConfigureAwait(false);
        var computed = await player.State.Computed.Update().ConfigureAwait(false);
        while (computed.ValueOrDefault != PlaybackKind.None) {
            await computed.WhenInvalidated().ConfigureAwait(false);
            computed = await computed.Update().ConfigureAwait(false);
        }
    }

    private async Task InnerActivateRealtimeListening(Symbol chatId)
    {
        var player = _chatPlayers.ActivatePlayer(chatId);
        await EnsureStopped(player).ConfigureAwait(false);
        await player.Play(_clocks.SystemClock.Now, isRealtime: true, default).ConfigureAwait(false);
    }

    private bool IsListeningToChat(Symbol chatId)
        => _listeningChats.ListeningChats.Contains((string)chatId, StringComparer.Ordinal);
}
