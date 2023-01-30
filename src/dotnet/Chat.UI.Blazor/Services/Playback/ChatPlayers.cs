using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPlayers : WorkerBase
{
    private static TimeSpan RestorePreviousPlaybackStateDelay { get; } = TimeSpan.FromMilliseconds(250);

    private volatile ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer> _players =
        ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer>.Empty;

    private readonly IMutableState<PlaybackState?> _playbackState;
    private AudioUI? _audioUI;

    private IServiceProvider Services { get; }
    private IAudioOutputController AudioOutputController { get;}
    private MomentClockSet Clocks { get; }
    // TODO: get rid of circular dependency between AudioUI and ChatPlayers
    private AudioUI AudioUI => _audioUI ??= Services.GetRequiredService<AudioUI>();
    private TuneUI TuneUI { get; }

    public IState<PlaybackState?> PlaybackState => _playbackState;

    public ChatPlayers(IServiceProvider services)
    {
        Services = services;
        AudioOutputController = services.GetRequiredService<IAudioOutputController>();
        Clocks = services.Clocks();
        TuneUI = services.GetRequiredService<TuneUI>();

        var stateFactory = services.StateFactory();
        _playbackState = stateFactory.NewMutable(
            (PlaybackState?)null,
            StateCategories.Get(GetType(), nameof(PlaybackState)));
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

    public void StartHistoricalPlayback(ChatId chatId, Moment startAt)
        => StartPlayback(new HistoricalPlaybackState(chatId, startAt));

    public void StartRealtimePlayback(RealtimePlaybackState playbackState)
        => StartPlayback(playbackState);

    public void StopPlayback()
        => _playbackState.Value = null;

    // Protected methods

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        // TODO(AY): Implement _players cleanup here
        var lastPlaybackState = (PlaybackState?)null;
        while (!cancellationToken.IsCancellationRequested) {
            var cPlaybackState = PlaybackState.Computed;
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
                    StopPlayback();
                }
            }
            lastPlaybackState = newPlaybackState;
            await cPlaybackState.WhenInvalidated(cancellationToken).ConfigureAwait(false);
        }
    }

    // Private methods

    private void StartPlayback(PlaybackState playbackState)
        => _playbackState.Value = playbackState;

    private void ResumeRealtimePlayback()
        => BackgroundTask.Run(async () => {
            var playbackState = await AudioUI.GetExpectedRealtimePlaybackState().ConfigureAwait(false);
            if (playbackState == null)
                StopPlayback();
            else
                StartRealtimePlayback(playbackState);
        }, CancellationToken.None);

    private async Task ProcessPlaybackStateChange(
        PlaybackState? lastPlaybackState,
        PlaybackState? playbackState,
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
        case HistoricalPlaybackState historical:
            await ExitState(lastPlaybackState, cancellationToken).ConfigureAwait(false);
            await EnterState(historical, cancellationToken).ConfigureAwait(false);
            break;
        case RealtimePlaybackState realtime:
            var lastRealtime = (RealtimePlaybackState)lastPlaybackState!;
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

        async Task EnterState(PlaybackState? state, CancellationToken ct)
        {
            await AudioOutputController.ToggleAudio(state != null).ConfigureAwait(false);
            if (state is HistoricalPlaybackState historical) {
                _ = TuneUI.Play("start-historical-playback", CancellationToken.None);
                var startTask = StartHistoricalPlayback(historical.ChatId, historical.StartAt, ct);
                _ = BackgroundTask.Run(async () => {
                    var endPlaybackTask = await startTask.ConfigureAwait(false);
                    await endPlaybackTask.ConfigureAwait(false);
                    await Clocks.CpuClock.Delay(RestorePreviousPlaybackStateDelay, ct).ConfigureAwait(false);
                    if (PlaybackState.Value == historical)
                        ResumeRealtimePlayback();
                }, ct);
                await startTask.ConfigureAwait(false);
            }
            if (state is RealtimePlaybackState realtime) {
                _ = TuneUI.Play("start-realtime-playback", CancellationToken.None);
                var resumeTask = ResumeRealtimePlayback(realtime.ChatIds, ct);
                await resumeTask.ConfigureAwait(false);
            }
        }

        async Task ExitState(PlaybackState? state, CancellationToken ct)
        {
            if (state is HistoricalPlaybackState historical) {
                _ = TuneUI.Play("stop-historical-playback", CancellationToken.None);
                await Stop(historical.ChatId, ChatPlayerKind.Historical, ct);
            }
            else if (state is RealtimePlaybackState realtime) {
                _ = TuneUI.Play("stop-realtime-playback", CancellationToken.None);
                await Stop(realtime.ChatIds, ChatPlayerKind.Realtime, ct);
            }
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
            .Collect()
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
            .Collect();

    private Task Stop(CancellationToken cancellationToken)
        // ReSharper disable once InconsistentlySynchronizedField
        => _players
            .Select(kv => Stop(kv.Key.ChatId, kv.Key.PlayerKind, cancellationToken))
            .Collect();
}
