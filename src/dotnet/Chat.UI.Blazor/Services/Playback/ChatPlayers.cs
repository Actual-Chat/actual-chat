namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPlayers : WorkerBase
{
    private static TimeSpan RestorePreviousPlaybackStateDelay { get; } = TimeSpan.FromMilliseconds(250);

    private readonly ILogger _log;
    private readonly IServiceProvider _services;
    private readonly MomentClockSet _clocks;
    private ChatPageState? _chatPageState;

    private volatile ImmutableDictionary<(Symbol ChatId, ChatPlayerKind PlayerKind), ChatPlayer> _players =
        ImmutableDictionary<(Symbol ChatId, ChatPlayerKind PlayerKind), ChatPlayer>.Empty;

    private ChatPageState ChatPageState => _chatPageState ??= _services.GetRequiredService<ChatPageState>();
    public IMutableState<ChatPlaybackState?> ChatPlaybackState { get; }

    public ChatPlayers(IServiceProvider services)
    {
        _services = services;
        _log = services.LogFor(GetType());
        _clocks = services.Clocks();
        ChatPlaybackState = services.StateFactory().NewMutable<ChatPlaybackState?>();
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

    public void StartRealtimePlayback(bool mustPlayPinned)
        => BackgroundTask.Run(async () => {
            var playbackState = await ChatPageState.GetRealtimeChatPlaybackState(mustPlayPinned, default).ConfigureAwait(false);
            ChatPlaybackState.Value = playbackState;
        }, CancellationToken.None);

    public void StartHistoricalPlayback(Symbol chatId, Moment startAt, RealtimeChatPlaybackState? previousState = null)
    {
        previousState ??= ChatPlaybackState.Value switch {
            RealtimeChatPlaybackState realtime => realtime,
            HistoricalChatPlaybackState historical => historical.PreviousState,
            _ => null,
        };
        ChatPlaybackState.Value = new HistoricalChatPlaybackState(chatId, startAt, previousState);
    }

    public void StopPlayback(bool restorePreviousState)
        => ChatPlaybackState.Value =
            restorePreviousState
                ? ChatPlaybackState.Value is HistoricalChatPlaybackState historical ? historical.PreviousState : null
                : null;

    // Protected methods

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        // TODO(AY): Implement _players cleanup here
        var lastPlaybackState = (ChatPlaybackState?) null;
        var cPlaybackState = ChatPlaybackState.Computed;
        while (!cancellationToken.IsCancellationRequested) {
            if (!cPlaybackState.IsConsistent())
                cPlaybackState = await cPlaybackState.Update(cancellationToken).ConfigureAwait(false);
            var newPlaybackState = cPlaybackState.Value;
            if (newPlaybackState != lastPlaybackState) {
                try {
                    await ProcessPlaybackStateChange(lastPlaybackState, newPlaybackState, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    // Let's stop everything in this case
                    await Stop(cancellationToken).SuppressExceptions().ConfigureAwait(false);
                    newPlaybackState = null;
                    ChatPlaybackState.Value = null;
                }
            }
            lastPlaybackState = newPlaybackState;
            await cPlaybackState.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private async Task ProcessPlaybackStateChange(
        ChatPlaybackState? lastPlaybackState,
        ChatPlaybackState? playbackState,
        CancellationToken cancellationToken)
    {
        if (lastPlaybackState?.GetType() != playbackState?.GetType()) {
            // Mode type change
            await ExitState(lastPlaybackState, cancellationToken).ConfigureAwait(false);
            await EnterState(playbackState, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Same mode, but new settings
        switch (playbackState) {
        case HistoricalChatPlaybackState historical:
            await ExitState(lastPlaybackState, cancellationToken).ConfigureAwait(false);
            await EnterState(historical, cancellationToken).ConfigureAwait(false);
            break;
        case RealtimeChatPlaybackState realtime:
            var lastRealtime = (RealtimeChatPlaybackState) lastPlaybackState!;
            var removedChatIds = lastRealtime.ChatIds.Except(realtime.ChatIds);
            var addedChatIds = realtime.ChatIds.Except(lastRealtime.ChatIds);
            await Stop(removedChatIds, ChatPlayerKind.Realtime, cancellationToken).ConfigureAwait(false);
            await StartRealtimePlayback(addedChatIds, cancellationToken).ConfigureAwait(false);
            break;
        case null:
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(playbackState));
        }

        Task EnterState(ChatPlaybackState? state, CancellationToken ct)
        {
            if (state is HistoricalChatPlaybackState historical) {
                var result = StartHistoricalPlayback(historical.ChatId, historical.StartAt, ct);
                _ = BackgroundTask.Run(async () => {
                    var endPlaybackTask = await result.ConfigureAwait(false);
                    await endPlaybackTask.ConfigureAwait(false);
                    await _clocks.CpuClock.Delay(RestorePreviousPlaybackStateDelay, ct).ConfigureAwait(false);
                    if (ChatPlaybackState.Value == historical) {
                        // TODO(AY): We should beep if we restore non-null state, I guess...
                        ChatPlaybackState.Value = historical.PreviousState;
                    }
                }, ct);
                return result;
            }
            if (state is RealtimeChatPlaybackState realtime)
                return StartRealtimePlayback(realtime.ChatIds, ct);
            return Task.CompletedTask;
        }

        Task ExitState(ChatPlaybackState? state, CancellationToken ct)
        {
            if (state is HistoricalChatPlaybackState historical)
                return Stop(historical.ChatId, ChatPlayerKind.Historical, ct);
            if (state is RealtimeChatPlaybackState realtime)
                return Stop(realtime.ChatIds, ChatPlayerKind.Realtime, ct);
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

    private Task<Task> StartRealtimePlayback(Symbol chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsEmpty)
            return Task.FromResult(Task.CompletedTask);
        var player = GetOrCreate(chatId, ChatPlayerKind.Realtime);
        var whenPlaying = player.WhenPlaying;
        return whenPlaying is { IsCompleted: false }
            ? Task.FromResult(whenPlaying)
            : player.Start(_clocks.SystemClock.Now, cancellationToken);
    }

    private async Task<Task> StartRealtimePlayback(IEnumerable<Symbol> chatIds, CancellationToken cancellationToken)
    {
        var tasks = chatIds.Select(chatId => StartRealtimePlayback(chatId, cancellationToken));
        var playTasks = await Task.WhenAll(tasks).ConfigureAwait(false);
        return Task.WhenAll(playTasks);
    }

    private Task<Task> StartHistoricalPlayback(Symbol chatId, Moment startAt, CancellationToken cancellationToken)
    {
        if (chatId.IsEmpty)
            return Task.FromResult(Task.CompletedTask);
        var player = GetOrCreate(chatId, ChatPlayerKind.Historical);
        return player.Start(startAt, cancellationToken);
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
