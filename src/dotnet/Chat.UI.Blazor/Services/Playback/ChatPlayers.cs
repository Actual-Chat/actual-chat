namespace ActualChat.Chat.UI.Blazor.Services;

// ReSharper disable once ClassNeverInstantiated.Global
public class ChatPlayers : WorkerBase
{
    private readonly ILogger _log;
    private readonly IServiceProvider _services;
    private readonly MomentClockSet _clocks;
    private ChatPageState? _chatPageState;

    private volatile ImmutableDictionary<(Symbol ChatId, ChatPlayerKind PlayerKind), ChatPlayer> _players =
        ImmutableDictionary<(Symbol ChatId, ChatPlayerKind PlayerKind), ChatPlayer>.Empty;

    private ChatPageState ChatPageState => _chatPageState ??= _services.GetRequiredService<ChatPageState>();
    public IMutableState<ChatPlaybackMode?> PlaybackMode { get; }

    public ChatPlayers(IServiceProvider services)
    {
        _services = services;
        _log = services.LogFor(GetType());
        _clocks = services.Clocks();
        PlaybackMode = services.StateFactory().NewMutable<ChatPlaybackMode?>();
        Start();
    }

    protected override Task DisposeAsyncCore()
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var playerCloseTasks = _players.Select(kv => Close(kv.Key.ChatId, kv.Key.PlayerKind));
        return Task.WhenAll(playerCloseTasks);
    }

    [ComputeMethod]
    public virtual Task<ChatPlayer?> Get(Symbol chatId, ChatPlayerKind playerKind, CancellationToken cancellationToken)
    {
        lock (Lock) return Task.FromResult(_players.GetValueOrDefault((chatId, playerKind)));
    }

    public void StartRealtimePlayback()
        => BackgroundTask.Run(async () => {
            var playbackMode = await ChatPageState.GetRealtimeChatPlaybackMode(default).ConfigureAwait(false);
            PlaybackMode.Value = playbackMode;
        }, CancellationToken.None);

    public void StartHistoricalPlayback(Symbol chatId, Moment startAt)
        => PlaybackMode.Value = new HistoricalChatPlaybackMode(chatId, startAt);

    public void StopPlayback()
        => PlaybackMode.Value = null;

    // Protected methods

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        // TODO(AY): Implement _players cleanup here
        var lastPlaybackMode = (ChatPlaybackMode?) null;
        var cPlaybackMode = PlaybackMode.Computed;
        while (!cancellationToken.IsCancellationRequested) {
            if (!cPlaybackMode.IsConsistent())
                cPlaybackMode = await cPlaybackMode.Update(cancellationToken).ConfigureAwait(false);
            var newPlaybackMode = cPlaybackMode.ValueOrDefault;
            if (newPlaybackMode != lastPlaybackMode) {
                try {
                    await ProcessPlaybackModeChange(lastPlaybackMode, newPlaybackMode, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    // Let's stop everything in this case
                    await Stop(cancellationToken).SuppressExceptions().ConfigureAwait(false);
                    newPlaybackMode = null;
                    PlaybackMode.Value = null;
                }
            }
            lastPlaybackMode = newPlaybackMode;
            await cPlaybackMode.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private async Task ProcessPlaybackModeChange(
        ChatPlaybackMode? lastPlaybackMode,
        ChatPlaybackMode? playbackMode,
        CancellationToken cancellationToken)
    {
        if (lastPlaybackMode?.GetType() != playbackMode?.GetType()) {
            // Mode type change
            await StopMode(lastPlaybackMode, cancellationToken).ConfigureAwait(false);
            await StartMode(playbackMode, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Same mode, but new settings
        switch (playbackMode) {
        case HistoricalChatPlaybackMode hm:
            await StopMode(lastPlaybackMode, cancellationToken).ConfigureAwait(false);
            await StartMode(hm, cancellationToken).ConfigureAwait(false);
            break;
        case RealtimeChatPlaybackMode rm:
            var lrm = (RealtimeChatPlaybackMode) lastPlaybackMode!;
            var removedChatIds = lrm.ChatIds.Except(rm.ChatIds);
            var addedChatIds = rm.ChatIds.Except(lrm.ChatIds);
            await Stop(removedChatIds, ChatPlayerKind.Realtime, cancellationToken).ConfigureAwait(false);
            await PlayRealtime(addedChatIds, cancellationToken).ConfigureAwait(false);
            break;
        case null:
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(playbackMode));
        }

        Task StartMode(ChatPlaybackMode? mode, CancellationToken ct)
        {
            if (mode is HistoricalChatPlaybackMode hm)
                return PlayHistorical(hm.ChatId, hm.StartAt, ct);
            if (mode is RealtimeChatPlaybackMode rm)
                return PlayRealtime(rm.ChatIds, ct);
            return Task.CompletedTask;
        }

        Task StopMode(ChatPlaybackMode? mode, CancellationToken ct)
        {
            if (mode is HistoricalChatPlaybackMode hm)
                return Stop(hm.ChatId, ChatPlayerKind.Historical, ct);
            if (mode is RealtimeChatPlaybackMode rm)
                return Stop(rm.ChatIds, ChatPlayerKind.Realtime, ct);
            return Task.CompletedTask;
        }
    }

    private ChatPlayer GetOrCreate(Symbol chatId, ChatPlayerKind playerKind)
    {
        this.ThrowIfDisposedOrDisposing();
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        ChatPlayer newPlayer;
        lock (Lock) {
            var player = _players.GetValueOrDefault((chatId, playerKind));
            if (player != null)
                return player;
            newPlayer = playerKind switch {
                ChatPlayerKind.Realtime => _services.Activate<RealtimeChatPlayer>(chatId),
                ChatPlayerKind.Historical => _services.Activate<HistoricalChatPlayer>(chatId),
                _ => throw new ArgumentOutOfRangeException(nameof(playerKind), playerKind, null),
            };
            _players = _players.Add((chatId, playerKind), newPlayer);
        }
        using (Computed.Invalidate())
            _ = Get(chatId, playerKind, default);
        return newPlayer;
    }

    private async Task Close(Symbol chatId, ChatPlayerKind playerKind)
    {
        if (chatId.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        ChatPlayer? player;
        lock (Lock) {
            player = _players.GetValueOrDefault((chatId, playerKind));
            if (player == null)
                return;
            _players = _players.Remove((chatId, playerKind));
        }
        await player.DisposeAsync().ConfigureAwait(true);
        using (Computed.Invalidate())
            _ = Get(chatId, playerKind, default);
    }

    private Task<Task> PlayRealtime(Symbol chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsEmpty)
            return Task.FromResult(Task.CompletedTask);
        var player = GetOrCreate(chatId, ChatPlayerKind.Realtime);
        var whenPlaying = player.WhenPlaying;
        return whenPlaying is { IsCompleted: false }
            ? Task.FromResult(whenPlaying)
            : player.Play(_clocks.SystemClock.Now, cancellationToken);
    }

    private async Task<Task> PlayRealtime(IEnumerable<Symbol> chatIds, CancellationToken cancellationToken)
    {
        var tasks = chatIds.Select(chatId => PlayRealtime(chatId, cancellationToken));
        var playTasks = await Task.WhenAll(tasks).ConfigureAwait(false);
        return Task.WhenAll(playTasks);
    }

    private Task<Task> PlayHistorical(Symbol chatId, Moment startAt, CancellationToken cancellationToken)
    {
        if (chatId.IsEmpty)
            return Task.FromResult(Task.CompletedTask);
        var player = GetOrCreate(chatId, ChatPlayerKind.Historical);
        return player.Play(startAt, cancellationToken);
    }

    private Task Stop(Symbol chatId, ChatPlayerKind playerKind, CancellationToken cancellationToken)
    {
        ChatPlayer? player;
        lock (Lock) player = _players.GetValueOrDefault((chatId, playerKind));
        return player?.Stop() ?? Task.CompletedTask;
    }

    private Task Stop(IEnumerable<Symbol> chatIds, ChatPlayerKind playerKind, CancellationToken cancellationToken)
    {
        var tasks = chatIds.Select(chatId => Stop(chatId, playerKind, cancellationToken));
        return Task.WhenAll(tasks);
    }

    private Task Stop(CancellationToken cancellationToken)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var playerStopTasks = _players.Select(kv => Stop(kv.Key.ChatId, kv.Key.PlayerKind, cancellationToken));
        return Task.WhenAll(playerStopTasks);
    }
}
