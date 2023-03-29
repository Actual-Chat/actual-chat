using ActualChat.UI.Blazor.Services;
using Stl.Interception;

namespace ActualChat.Chat.UI.Blazor.Services;

public class ChatPlayers : WorkerBase, IComputeService, INotifyInitialized
{
    private static TimeSpan RestorePreviousPlaybackStateDelay { get; } = TimeSpan.FromMilliseconds(250);

    private volatile ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer> _players =
        ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer>.Empty;

    private readonly IMutableState<PlaybackState?> _playbackState;
    private ChatAudioUI? _audioUI;

    private IServiceProvider Services { get; }
    private IAudioOutputController AudioOutputController { get;}
    private ChatAudioUI ChatAudioUI => _audioUI ??= Services.GetRequiredService<ChatAudioUI>();
    private TuneUI TuneUI { get; }
    private MomentClockSet Clocks { get; }

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
    }

    void INotifyInitialized.Initialized()
        => this.Start();

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

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // TODO(AY): Implement _players cleanup here
        var lastPlaybackState = (PlaybackState?)null;
        await foreach (var cPlaybackState in PlaybackState.Changes(cancellationToken).ConfigureAwait(false)) {
            var newPlaybackState = cPlaybackState.Value;
            try {
                await ProcessPlaybackStateChange(lastPlaybackState, newPlaybackState, cancellationToken)
                    .ConfigureAwait(false);
                lastPlaybackState = newPlaybackState;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                // Let's stop everything in this case
                StopPlayback();
                lastPlaybackState = null;
                _ = StopPlayers();
            }
        }
    }

    // Private methods

    private void StartPlayback(PlaybackState playbackState)
        => _playbackState.Value = playbackState;

    private void ResumeRealtimePlayback()
        => BackgroundTask.Run(async () => {
            var playbackState = await ChatAudioUI.GetExpectedRealtimePlaybackState().ConfigureAwait(false);
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
            await ExitState(lastPlaybackState).ConfigureAwait(false);
            await EnterState(playbackState, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Same mode, but new settings
        switch (playbackState) {
        case HistoricalPlaybackState historical:
            await ExitState(lastPlaybackState).ConfigureAwait(false);
            await EnterState(historical, cancellationToken).ConfigureAwait(false);
            break;
        case RealtimePlaybackState realtime:
            var lastRealtime = (RealtimePlaybackState)lastPlaybackState!;
            var removedChatIds = lastRealtime.ChatIds.Except(realtime.ChatIds);
            var addedChatIds = realtime.ChatIds.Except(lastRealtime.ChatIds);
            await StopPlayers(removedChatIds, ChatPlayerKind.Realtime).ConfigureAwait(false);
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

        async Task ExitState(PlaybackState? state)
        {
            if (state is HistoricalPlaybackState historical) {
                _ = TuneUI.Play("stop-historical-playback", CancellationToken.None);
                await StopPlayer(historical.ChatId, ChatPlayerKind.Historical);
            }
            else if (state is RealtimePlaybackState realtime) {
                _ = TuneUI.Play("stop-realtime-playback", CancellationToken.None);
                await StopPlayers(realtime.ChatIds, ChatPlayerKind.Realtime);
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

    private Task StopPlayer(ChatId chatId, ChatPlayerKind playerKind)
    {
        ChatPlayer? player;
        lock (Lock) player = _players.GetValueOrDefault((chatId, playerKind));
        return player?.Stop() ?? Task.CompletedTask;
    }

    private Task StopPlayers(IEnumerable<ChatId> chatIds, ChatPlayerKind playerKind)
        => chatIds
            .Select(chatId => StopPlayer(chatId, playerKind))
            .Collect();

    private Task StopPlayers()
        // ReSharper disable once InconsistentlySynchronizedField
        => _players
            .Select(kv => StopPlayer(kv.Key.ChatId, kv.Key.PlayerKind))
            .Collect();
}
