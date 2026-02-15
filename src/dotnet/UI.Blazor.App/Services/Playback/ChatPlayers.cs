using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Orchestrates audio playback across multiple chats, handling real-time and historical modes.
/// </summary>
public class ChatPlayers : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static bool DebugMode => Constants.DebugMode.ChatPlayers;
    private static TimeSpan RestorePreviousPlaybackStateDelay { get; } = TimeSpan.FromMilliseconds(250);

    private volatile ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer> _players =
        ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer>.Empty;

    private readonly MutableState<PlaybackState?> _playbackState;
    private readonly AudioFocusRequester _audioFocusRequester;
    private AudioFocusScope? _audioFocusScope;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private new ILogger? DebugLog => DebugMode ? Log : null;

    public IState<PlaybackState?> PlaybackState => _playbackState;

    protected AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;
    protected AudioWidgetSession AudioWidgetSession => Hub.AudioWidgetSession;

    public ChatPlayers(AppUIHub hub) : base(hub)
    {
        _playbackState = hub.StateFactory.NewMutable(
            (PlaybackState?)null,
            StateCategories.Get(GetType(), nameof(PlaybackState)));
        _audioFocusRequester = new AudioFocusRequester(AudioFocusMode.Playback, OnAudioFocusLost);
        AudioWidgetSession.Reset();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual Task<HistoricalChatPlayer?> GetHistoricalChatPlayer(ChatId chatId, CancellationToken cancellationToken)
    {
        var player = GetHistoricalChatPlayerNonComputed(chatId);
        return Task.FromResult(player);
    }

    public HistoricalChatPlayer? GetHistoricalChatPlayerNonComputed(ChatId chatId)
    {
        lock (Lock)
            return _players.GetValueOrDefault((chatId, ChatPlayerKind.Historical)) as HistoricalChatPlayer;
    }

    [ComputeMethod]
    public virtual Task<RealtimeChatPlayer?> GetRealtimeChatPlayer(ChatId chatId, CancellationToken cancellationToken)
    {
        var player = GetRealtimeChatPlayerNonComputed(chatId);
        return Task.FromResult(player);
    }

    public RealtimeChatPlayer? GetRealtimeChatPlayerNonComputed(ChatId chatId)
    {
        lock (Lock)
            return _players.GetValueOrDefault((chatId, ChatPlayerKind.Realtime)) as RealtimeChatPlayer;
    }

    public async Task StartHistoricalPlayback(ChatId chatId, Moment startAt)
    {
        DebugLog?.LogInformation("StartHistoricalPlayback: chatId={ChatId}, startAt={StartAt}", chatId, startAt);

        var prevState = PlaybackState.Value;
        var newState = new HistoricalPlaybackState(chatId, startAt);
        if (prevState == newState) {
            DebugLog?.LogInformation("StartHistoricalPlayback: stopping active historical playback");
            StopPlayback();
            await StopPlayers([chatId], ChatPlayerKind.Historical).ConfigureAwait(true);
        }
        StartPlayback(newState);
    }

    public void StartRealtimePlayback(RealtimePlaybackState playbackState)
        => StartPlayback(playbackState);

    public void StopHistoricalPlayback()
    {
        if (PlaybackState.Value is HistoricalPlaybackState)
            StopPlayback();
    }

    public void StopRealtimePlayback()
    {
        if (PlaybackState.Value is RealtimePlaybackState)
            StopPlayback();
    }

    public void ReleaseAudioFocusDueToPause(ChatPlayer player)
    {
        Log.LogInformation("Releasing audio focus for chat player '{ChatId}'", player.ChatId);

        var playbackState = _playbackState.Value;
        var shouldRelease = playbackState switch {
            HistoricalPlaybackState historical => historical.ChatId == player.ChatId,
            RealtimePlaybackState realtime => realtime.ChatIds.Contains(player.ChatId),
            _ => false
        };

        if (shouldRelease)
            ReleaseAudioFocus();
    }

    public async Task<bool> TryGainAudioFocusForResume(ChatPlayer player)
    {
        Log.LogInformation("Trying to gain audio focus for chat player '{ChatId}'", player.ChatId);
        var scope = await TryAcquireAudioFocus($"Resuming chat player '{player.ChatId}'").ConfigureAwait(false);
        return scope is not null;
    }

    public void UpdateMediaSessionState()
        => AudioWidgetSession.UpdateMediaSessionState();

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("OnRun: ChatPlayers started");
        try {
            var lastPlaybackState = (PlaybackState?)null;
            var changes = PlaybackState.Computed.Changes(cancellationToken);
            DebugLog?.LogInformation("OnRun: Waiting for playback state changes");
            await foreach (var cPlaybackState in changes.ConfigureAwait(false)) {
                var newPlaybackState = cPlaybackState.Value;
                DebugLog?.LogInformation("OnRun: State change detected: {NewState}", newPlaybackState?.GetType().Name ?? "null");
                try {
                    await ProcessPlaybackStateChange(lastPlaybackState, newPlaybackState, cancellationToken)
                        .ConfigureAwait(false);
                    lastPlaybackState = newPlaybackState;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    // Let's stop everything in this case
                    Log.LogError(ex, "ProcessPlaybackStateChange failed, stopping playback");
                    StopPlayback();
                    lastPlaybackState = null;
                    _ = StopPlayers();
                }
            }
            DebugLog?.LogWarning("OnRun: Changes loop exited unexpectedly");
        }
        finally {
            DebugLog?.LogInformation("OnRun: ChatPlayers stopping");
            IEnumerable<Task> playerCloseTasks;
            lock (Lock)
                playerCloseTasks = _players.Select(kv => Close(kv.Key.ChatId, kv.Key.PlayerKind));
            await Task.WhenAll(playerCloseTasks).SilentAwait(false);
        }
    }

    // Private methods

    private void StartPlayback(PlaybackState playbackState)
        => _playbackState.Value = playbackState;

    private void StopPlayback()
        => _playbackState.Value = null;

    private void ResumeRealtimePlayback()
        => _ = BackgroundTask.Run(async () => {
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
        DebugLog?.LogInformation("ProcessPlaybackStateChange: {LastState} -> {NewState}",
            lastPlaybackState?.GetType().Name ?? "null",
            playbackState?.GetType().Name ?? "null");

        if (playbackState == null) {
            await ExitState(lastPlaybackState).ConfigureAwait(false);
            ReleaseAudioFocus();
            AudioWidgetSession.OnPlaybackStateChanged(null);
            return;
        }

        var audioFocusScope = await TryAcquireAudioFocus("Playback state change").ConfigureAwait(false);
        if (audioFocusScope is null) {
            // Failed to get audio focus, stop playback. Show toast?
            Log.LogWarning("ProcessPlaybackStateChange: failed to gain audio focus, stopping playback");
            StopPlayback();
            return;
        }

        if (lastPlaybackState?.GetType() != playbackState?.GetType()) {
            // Mode type change
            await ExitState(lastPlaybackState).ConfigureAwait(false);
            await EnterState(playbackState, cancellationToken).ConfigureAwait(false);
            AudioWidgetSession.OnPlaybackStateChanged(playbackState);
            UpdateMediaSessionState();
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
        AudioWidgetSession.OnPlaybackStateChanged(playbackState);
        UpdateMediaSessionState();
        return;

        async Task EnterState(PlaybackState? state, CancellationToken ct)
        {
            DebugLog?.LogInformation("EnterState: {State}", state?.GetType().Name ?? "null");
            if (state is HistoricalPlaybackState historical) {
                _ = TuneUI.Play(Tune.StartHistoricalPlayback);
                DebugLog?.LogInformation("EnterState: starting historical playback for {ChatId} at {StartAt}",
                    historical.ChatId, historical.StartAt);
                var startTask = StartHistoricalPlayback(historical.ChatId, historical.StartAt, ct);
                _ = BackgroundTask.Run(async () => {
                    var endPlaybackTask = await startTask.ConfigureAwait(false);
                    await endPlaybackTask.ConfigureAwait(false);
                    await Clocks.CpuClock.Delay(RestorePreviousPlaybackStateDelay, ct).ConfigureAwait(false);
                    if (PlaybackState.Value == historical)
                        ResumeRealtimePlayback();
                }, ct);
                await startTask.ConfigureAwait(false);
                DebugLog?.LogInformation("EnterState: historical playback started");
            }
            if (state is RealtimePlaybackState realtime) {
                _ = TuneUI.Play(Tune.StartRealtimePlayback);
                var resumeTask = ResumeRealtimePlayback(realtime.ChatIds, ct);
                await resumeTask.ConfigureAwait(false);
            }
        }

        async Task ExitState(PlaybackState? state)
        {
            if (state is HistoricalPlaybackState historical) {
                _ = TuneUI.Play(Tune.StopHistoricalPlayback);
                await StopPlayer(historical.ChatId, ChatPlayerKind.Historical).ConfigureAwait(false);
            }
            else if (state is RealtimePlaybackState realtime) {
                _ = TuneUI.Play(Tune.StopRealtimePlayback);
                await StopPlayers(realtime.ChatIds, ChatPlayerKind.Realtime).ConfigureAwait(false);
            }
        }
    }

    private ChatPlayer GetOrCreate(ChatId chatId, ChatPlayerKind playerKind)
    {
        StopToken.ThrowIfCancellationRequested();
        ChatPlayer newPlayer;
        lock (Lock) {
            var player = _players.GetValueOrDefault((chatId, playerKind));
            if (player != null) {
                DebugLog?.LogInformation("GetOrCreate: returning existing {PlayerKind} player for {ChatId}", playerKind, chatId);
                return player;
            }
            DebugLog?.LogInformation("GetOrCreate: creating new {PlayerKind} player for {ChatId}", playerKind, chatId);
            newPlayer = playerKind switch {
                ChatPlayerKind.Realtime => new RealtimeChatPlayer(Hub, chatId),
                ChatPlayerKind.Historical => new HistoricalChatPlayer(Hub, chatId),
                _ => throw new ArgumentOutOfRangeException(nameof(playerKind), playerKind, null),
            };
            newPlayer.ChatPlayers = this;
            _players = _players.Add((chatId, playerKind), newPlayer);
        }
        if (playerKind is ChatPlayerKind.Historical)
            using (Invalidation.Begin())
                _ = GetHistoricalChatPlayer(chatId, default);
        if (playerKind is ChatPlayerKind.Realtime)
            using (Invalidation.Begin())
                _ = GetRealtimeChatPlayer(chatId, default);
        return newPlayer;
    }

    private async Task Close(ChatId chatId, ChatPlayerKind playerKind)
    {
        ChatPlayer? player;
        lock (Lock) {
            player = _players.GetValueOrDefault((chatId, playerKind));
            if (player == null)
                return;
            _players = _players.Remove((chatId, playerKind));
        }
        await player.DisposeAsync().ConfigureAwait(false);
        if (playerKind is ChatPlayerKind.Historical)
            using (Invalidation.Begin())
                _ = GetHistoricalChatPlayer(chatId, default);
        if (playerKind is ChatPlayerKind.Realtime)
            using (Invalidation.Begin())
                _ = GetRealtimeChatPlayer(chatId, default);
    }

    private Task<Task> ResumeRealtimePlayback(ChatId chatId, CancellationToken cancellationToken)
    {
        var player = GetOrCreate(chatId, ChatPlayerKind.Realtime);
        player.Playback.IsPaused.Updated += IsPausedOnUpdated;
        player.Playback.IsPlaying.Updated += IsPlayingOnUpdated;
        var whenPlaying = player.WhenPlaying;
        return whenPlaying is { IsCompleted: false }
            ? Task.FromResult(whenPlaying)
            : player.Start(Clocks.SystemClock.Now, cancellationToken);
    }

    private async Task<Task> ResumeRealtimePlayback(IEnumerable<ChatId> chatIds, CancellationToken cancellationToken)
    {
        var resultPlayingTasks = await chatIds
            .Select(chatId => ResumeRealtimePlayback(chatId, cancellationToken))
            .Collect(ApiConstants.Concurrency.Unlimited, cancellationToken)
            .ConfigureAwait(false);
        return Task.WhenAll(resultPlayingTasks);
    }

    private Task<Task> StartHistoricalPlayback(ChatId chatId, Moment startAt, CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("StartHistoricalPlayback (private): getting or creating player for {ChatId}", chatId);
        var player = GetOrCreate(chatId, ChatPlayerKind.Historical);
        DebugLog?.LogInformation("StartHistoricalPlayback (private): calling player.Start for {ChatId}", chatId);
        var startTask = player.Start(startAt, cancellationToken);
        player.Playback.IsPaused.Updated += IsPausedOnUpdated;
        player.Playback.IsPlaying.Updated += IsPlayingOnUpdated;
        return startTask;
    }

    private Task StopPlayer(ChatId chatId, ChatPlayerKind playerKind)
    {
        ChatPlayer? player;
        lock (Lock)
            player = _players.GetValueOrDefault((chatId, playerKind));
        if (player is null)
            return Task.CompletedTask;

        player.Playback.IsPaused.Updated -= IsPausedOnUpdated;
        player.Playback.IsPlaying.Updated -= IsPlayingOnUpdated;
        return player.Stop();
    }

    private void IsPausedOnUpdated(State state, StateEventKind kind)
        => UpdateMediaSessionState();

    private void IsPlayingOnUpdated(State state, StateEventKind kind)
        => UpdateMediaSessionState();

    private Task StopPlayers(IEnumerable<ChatId> chatIds, ChatPlayerKind playerKind)
        => chatIds
            .Select(chatId => StopPlayer(chatId, playerKind))
            .Collect(ApiConstants.Concurrency.Unlimited);

    private Task StopPlayers()
        // ReSharper disable once InconsistentlySynchronizedField
        => _players
            .Select(kv => StopPlayer(kv.Key.ChatId, kv.Key.PlayerKind))
            .Collect(ApiConstants.Concurrency.Unlimited);

    private async Task<AudioFocusScope?> TryAcquireAudioFocus(string? reason = "")
    {
        if (_audioFocusScope is not null && !_audioFocusScope.IsSuspended) {
            Log.LogInformation("Already have audio focus {Scope}. Request reason: '{Reason}'", _audioFocusScope, reason);
            return _audioFocusScope;
        }
        _audioFocusScope = await AudioFocusUI.TryAcquire(_audioFocusRequester).ConfigureAwait(false);
        return _audioFocusScope;
    }

    private void ReleaseAudioFocus()
    {
        _audioFocusScope?.Dispose();
        _audioFocusScope = null;
    }

    private AudioFocusRestoreHandler? OnAudioFocusLost(bool mayRecover, bool canDuck)
    {
        Log.LogInformation("Audio focus lost event. May recover: {MayRecover}, Can duck: {CanDuck}", mayRecover, canDuck);
        if (canDuck)
            return null; // Do not stop players. We don't support ducking so far, so just let it play, do nothing.

        if (!mayRecover)
            _audioFocusScope = null;
        var restoreFocusHandler = HandleLostAudioFocus(PlaybackState.Value, mayRecover);
        return restoreFocusHandler;
    }

    private AudioFocusRestoreHandler? HandleLostAudioFocus(PlaybackState? state, bool mayRecover)
    {
        Log.LogInformation("Lost audio focus. State: '{State}'", state);
        if (state is null)
            return null; // We should never get here.

        if (state is HistoricalPlaybackState historicalPlaybackState) {
            var paused = true;
            lock (Lock) {
                var activePlayers = _players.Values.Where(c => c.Playback.IsPlaying.Value).ToList();
                if (activePlayers.Count is 1
                    && activePlayers[0] is HistoricalChatPlayer historicalChatPlayer
                    && historicalPlaybackState.ChatId == historicalChatPlayer.ChatId) {
                    historicalChatPlayer.Playback.Pause(CancellationToken.None);
                    paused = true;
                    Log.LogInformation("Lost audio focus, paused historical chat player");
                }
            }
            if (!mayRecover)
                return null;

            if (paused)
                return () => {
                    Log.LogInformation("Restored audio focus. Will try resume historical playback. Original state: '{State}'", state);
                    lock (Lock) {
                        var historicalChatPlayer = _players.Values
                            .OfType<HistoricalChatPlayer>()
                            .FirstOrDefault(c => c.ChatId == historicalPlaybackState.ChatId);
                        if (historicalChatPlayer is { Playback.IsPaused.Value: true }) {
                            historicalChatPlayer.Playback.Resume(CancellationToken.None);
                            Log.LogInformation("Resumed historical chat player");
                        }
                    }
                };
        }

        StopPlayback();
        Log.LogInformation("Lost audio focus, stopped playback");
        if (!mayRecover)
            return null;

        if (state is RealtimePlaybackState)
            return () => {
                Log.LogInformation("Restored audio focus. Resuming realtime playback. Original state: '{State}'", state);
                ResumeRealtimePlayback();
            };

        return null;
    }
}
