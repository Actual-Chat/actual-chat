namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPlayers : WorkerBase
{
    private static TimeSpan RestorePreviousPlaybackStateDelay { get; } = TimeSpan.FromMilliseconds(250);

    private volatile ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer> _players =
        ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer>.Empty;

    private IServiceProvider Services { get; }
    private MomentClockSet Clocks { get; }
    private ChatUI ChatUI { get; }
    public IMutableState<ChatPlaybackState?> ChatPlaybackState { get; }
    public IMutableState<ChatId> HistoricalPlaybackChatId { get; }

    public ChatPlayers(IServiceProvider services)
    {
        Services = services;
        Clocks = services.Clocks();
        ChatUI = services.GetRequiredService<ChatUI>();

        var stateFactory = services.StateFactory();
        ChatPlaybackState = stateFactory.NewMutable<ChatPlaybackState?>();
        HistoricalPlaybackChatId = stateFactory.NewMutable<ChatId>();
        Start();
    }

    protected override Task DisposeAsyncCore()
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var playerCloseTasks = _players.Select(kv => Close(kv.Key.ChatId, kv.Key.PlayerKind));
        return Task.WhenAll(playerCloseTasks);
    }

    [ComputeMethod]
    public virtual Task<ChatPlayer?> Get(ChatId chatId, ChatPlayerKind playerKind, CancellationToken cancellationToken)
    {
        lock (Lock) return Task.FromResult(_players.GetValueOrDefault((chatId, playerKind)));
    }

    public void ResumeRealtimePlayback()
        => BackgroundTask.Run(async () => {
            var playbackState = await ChatUI.GetRealtimePlaybackState().ConfigureAwait(false);
            StartPlayback(playbackState);
        }, CancellationToken.None);

    public void StartHistoricalPlayback(ChatId chatId, Moment startAt)
        => StartPlayback(new HistoricalChatPlaybackState(chatId, startAt));

    public void StartPlayback(ChatPlaybackState? playbackState)
        => ChatPlaybackState.Value = playbackState;

    public void StopPlayback()
        => ChatPlaybackState.Value = null;

    // Protected methods

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        // TODO(AY): Implement _players cleanup here
        var lastPlaybackState = (ChatPlaybackState?)null;
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
            var lastRealtime = (RealtimeChatPlaybackState)lastPlaybackState!;
            var removedChatIds = lastRealtime.ChatIds.Except(realtime.ChatIds);
            var addedChatIds = realtime.ChatIds.Except(lastRealtime.ChatIds);
            await Stop(removedChatIds, ChatPlayerKind.Realtime, cancellationToken).ConfigureAwait(false);
            await ResumeRealtimePlayback(addedChatIds, cancellationToken).ConfigureAwait(false);
            break;
        case null:
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(playbackState));
        }

        Task EnterState(ChatPlaybackState? state, CancellationToken ct)
        {
            if (state is HistoricalChatPlaybackState historical) {
                HistoricalPlaybackChatId.Value = historical.ChatId;
                var result = StartHistoricalPlayback(historical.ChatId, historical.StartAt, ct);
                _ = BackgroundTask.Run(async () => {
                    var endPlaybackTask = await result.ConfigureAwait(false);
                    await endPlaybackTask.ConfigureAwait(false);
                    await Clocks.CpuClock.Delay(RestorePreviousPlaybackStateDelay, ct).ConfigureAwait(false);
                    if (ChatPlaybackState.Value == historical)
                        ResumeRealtimePlayback();
                }, ct);
                return result;
            }
            if (state is RealtimeChatPlaybackState realtime)
                return ResumeRealtimePlayback(realtime.ChatIds, ct);
            return Task.CompletedTask;
        }

        async Task ExitState(ChatPlaybackState? state, CancellationToken ct)
        {
            if (state is HistoricalChatPlaybackState historical) {
                await Stop(historical.ChatId, ChatPlayerKind.Historical, ct);
                HistoricalPlaybackChatId.Value = default;
            }
            else if (state is RealtimeChatPlaybackState realtime)
                await Stop(realtime.ChatIds, ChatPlayerKind.Realtime, ct);
        }
    }

    private ChatPlayer GetOrCreate(ChatId chatId, ChatPlayerKind playerKind)
    {
        this.ThrowIfDisposedOrDisposing();
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));

        ChatPlayer newPlayer;
        lock (Lock) {
            var player = _players.GetValueOrDefault((chatId, playerKind));
            if (player != null)
                return player;
            newPlayer = playerKind switch {
                ChatPlayerKind.Realtime => Services.Activate<RealtimeChatPlayer>(chatId),
                ChatPlayerKind.Historical => Services.Activate<HistoricalChatPlayer>(chatId),
                _ => throw new ArgumentOutOfRangeException(nameof(playerKind), playerKind, null),
            };
            _players = _players.Add((chatId, playerKind), newPlayer);
        }
        using (Computed.Invalidate())
            _ = Get(chatId, playerKind, default);
        return newPlayer;
    }

    private async Task Close(ChatId chatId, ChatPlayerKind playerKind)
    {
        if (chatId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(chatId));
        ChatPlayer? player;
        lock (Lock) {
            player = _players.GetValueOrDefault((chatId, playerKind));
            if (player == null)
                return;
            _players = _players.Remove((chatId, playerKind));
        }
        await player.DisposeAsync();
        using (Computed.Invalidate())
            _ = Get(chatId, playerKind, default);
    }

    private Task<Task> ResumeRealtimePlayback(ChatId chatId, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            return Task.FromResult(Task.CompletedTask);
        var player = GetOrCreate(chatId, ChatPlayerKind.Realtime);
        var whenPlaying = player.WhenPlaying;
        return whenPlaying is { IsCompleted: false }
            ? Task.FromResult(whenPlaying)
            : player.Start(Clocks.SystemClock.Now, cancellationToken);
    }

    private async Task<Task> ResumeRealtimePlayback(IEnumerable<ChatId> chatIds, CancellationToken cancellationToken)
    {
        var resultPlayingTasks = await chatIds
            .Select(chatId => ResumeRealtimePlayback(chatId, cancellationToken))
            .Collect(0)
            .ConfigureAwait(false);
        return Task.WhenAll(resultPlayingTasks);
    }

    private Task<Task> StartHistoricalPlayback(ChatId chatId, Moment startAt, CancellationToken cancellationToken)
    {
        if (chatId.IsNone)
            return Task.FromResult(Task.CompletedTask);
        var player = GetOrCreate(chatId, ChatPlayerKind.Historical);
        return player.Start(startAt, cancellationToken);
    }

    private Task Stop(ChatId chatId, ChatPlayerKind playerKind, CancellationToken cancellationToken)
    {
        ChatPlayer? player;
        lock (Lock) player = _players.GetValueOrDefault((chatId, playerKind));
        return player?.Stop() ?? Task.CompletedTask;
    }

    private Task Stop(IEnumerable<ChatId> chatIds, ChatPlayerKind playerKind, CancellationToken cancellationToken)
        => chatIds
            .Select(chatId => Stop(chatId, playerKind, cancellationToken))
            .Collect(0);

    private Task Stop(CancellationToken cancellationToken)
        // ReSharper disable once InconsistentlySynchronizedField
        => _players
            .Select(kv => Stop(kv.Key.ChatId, kv.Key.PlayerKind, cancellationToken))
            .Collect(0);
}
