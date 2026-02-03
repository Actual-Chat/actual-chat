using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public class ChatPlayers : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static bool DebugMode => Constants.DebugMode.ChatPlayers;
    private static TimeSpan RestorePreviousPlaybackStateDelay { get; } = TimeSpan.FromMilliseconds(250);

    private volatile ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayerController> _players =
        ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayerController>.Empty;

    private readonly MutableState<PlaybackState?> _playbackState;
    private readonly AudioFocusConsumer _audioFocusConsumer;
    private IAudioFocusActivation? _audioFocusActivation;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;
    private new ILogger? DebugLog => DebugMode ? Log : null;

    public IState<PlaybackState?> PlaybackState => _playbackState;

    protected AudioFocusService AudioFocusService => Hub.AudioFocusService;
    protected AudioWidgetSession AudioWidgetSession => Hub.AudioWidgetSession;

    public ChatPlayers(AppUIHub hub) : base(hub)
    {
        _playbackState = hub.StateFactory.NewMutable(
            (PlaybackState?)null,
            StateCategories.Get(GetType(), nameof(PlaybackState)));
        _audioFocusConsumer = new AudioFocusConsumer(AudioMode.Playback, OnLostFocus);
        AudioWidgetSession.Reset();
    }

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    public virtual Task<HistoricalChatPlayerController?> GetHistoricalChatPlayerController(ChatId chatId, CancellationToken cancellationToken)
    {
        var controller = GetHistoricalChatPlayerControllerNonComputed(chatId);
        return Task.FromResult(controller);
    }

    public HistoricalChatPlayerController? GetHistoricalChatPlayerControllerNonComputed(ChatId chatId)
    {
        lock (Lock)
            return _players.GetValueOrDefault((chatId, ChatPlayerKind.Historical)) as HistoricalChatPlayerController;
    }

    [ComputeMethod]
    public virtual Task<RealtimeChatPlayerController?> GetRealtimeChatPlayerController(ChatId chatId, CancellationToken cancellationToken)
    {
        var controller = GetRealtimeChatPlayerControllerNonComputed(chatId);
        return Task.FromResult(controller);
    }

    public RealtimeChatPlayerController? GetRealtimeChatPlayerControllerNonComputed(ChatId chatId)
    {
        lock (Lock)
            return _players.GetValueOrDefault((chatId, ChatPlayerKind.Realtime)) as RealtimeChatPlayerController;
    }

    public void StartHistoricalPlayback(ChatId chatId, Moment startAt)
    {
        var currentState = PlaybackState.Value;
        DebugLog?.LogInformation("StartHistoricalPlayback: chatId={ChatId}, startAt={StartAt}, currentState={CurrentState}",
            chatId, startAt, currentState?.GetType().Name ?? "null");

        var newState = new HistoricalPlaybackState(chatId, startAt);
        if (currentState == newState) {
            // Same state - force restart by stopping first
            DebugLog?.LogInformation("StartHistoricalPlayback: same state, forcing restart");
            StopPlayback();
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

    public void ReleaseAudioFocusDueToPause(ChatPlayerController controller)
    {
        Log.LogInformation("Releasing audio focus for chat player '{ChatId}'", controller.ChatId);

        var playbackState = _playbackState.Value;
        var shouldRelease = playbackState switch {
            HistoricalPlaybackState historical => historical.ChatId == controller.ChatId,
            RealtimePlaybackState realtime => realtime.ChatIds.Contains(controller.ChatId),
            _ => false
        };

        if (shouldRelease)
            ReleaseAudioFocus();
    }

    public async Task<bool> TryGainAudioFocusForResume(ChatPlayerController controller)
    {
        Log.LogInformation("Trying to gain audio focus for chat player '{ChatId}'", controller.ChatId);
        var audioFocusHandle = await TryGainAudioFocus($"Resuming chat player '{controller.ChatId}'").ConfigureAwait(false);
        return audioFocusHandle is not null;
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

        var audioFocusActivation = await TryGainAudioFocus("Playback state change").ConfigureAwait(false);
        if (audioFocusActivation is null) {
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
                return player.ChatPlayer;
            }
            DebugLog?.LogInformation("GetOrCreate: creating new {PlayerKind} player for {ChatId}", playerKind, chatId);
            newPlayer = playerKind switch {
                ChatPlayerKind.Realtime => new RealtimeChatPlayer(Hub, chatId),
                ChatPlayerKind.Historical => new HistoricalChatPlayer(Hub, chatId),
                _ => throw new ArgumentOutOfRangeException(nameof(playerKind), playerKind, null),
            };
            var controller = newPlayer is HistoricalChatPlayer historicalChatPlayer
                ? new HistoricalChatPlayerController(historicalChatPlayer, this, Log)
                : (ChatPlayerController)new RealtimeChatPlayerController((RealtimeChatPlayer)newPlayer, this, Log);
            _players = _players.Add((chatId, playerKind), controller);
        }
        if (playerKind is ChatPlayerKind.Historical)
            using (Invalidation.Begin())
                _ = GetHistoricalChatPlayerController(chatId, default);
        if (playerKind is ChatPlayerKind.Realtime)
            using (Invalidation.Begin())
                _ = GetRealtimeChatPlayerController(chatId, default);
        return newPlayer;
    }

    private async Task Close(ChatId chatId, ChatPlayerKind playerKind)
    {
        ChatPlayerController? controller;
        lock (Lock) {
            controller = _players.GetValueOrDefault((chatId, playerKind));
            if (controller == null)
                return;
            _players = _players.Remove((chatId, playerKind));
        }
        await controller.DisposeAsync().ConfigureAwait(false);
        if (playerKind is ChatPlayerKind.Historical)
            using (Invalidation.Begin())
                _ = GetHistoricalChatPlayerController(chatId, default);
        if (playerKind is ChatPlayerKind.Realtime)
            using (Invalidation.Begin())
                _ = GetRealtimeChatPlayerController(chatId, default);
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
        ChatPlayerController? controller;
        lock (Lock)
            controller = _players.GetValueOrDefault((chatId, playerKind));
        if (controller is null)
            return Task.CompletedTask;

        controller.ChatPlayer.Playback.IsPaused.Updated -= IsPausedOnUpdated;
        controller.ChatPlayer.Playback.IsPlaying.Updated -= IsPlayingOnUpdated;
        return controller.ChatPlayer.Stop();
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

    private async Task<IAudioFocusActivation?> TryGainAudioFocus(string? reason = "")
    {
        if (_audioFocusActivation is not null && !_audioFocusActivation.IsSuspended) {
            Log.LogInformation("Already have audio focus Id={Id}. Request reason: '{Reason}'", _audioFocusActivation.Id, reason);
            return _audioFocusActivation;
        }
        _audioFocusActivation = await AudioFocusService.TryGainAudioFocus(_audioFocusConsumer).ConfigureAwait(false);
        return _audioFocusActivation;
    }

    private void ReleaseAudioFocus()
    {
        _audioFocusActivation?.Release();
        _audioFocusActivation = null;
    }

    private RestoreFocusHandler? OnLostFocus(bool mayRecover, bool canDuck)
    {
        Log.LogInformation("Audio focus lost event. May recover: {MayRecover}, Can duck: {CanDuck}", mayRecover, canDuck);
        if (canDuck)
            return null; // Do not stop players. We don't support ducking so far, so just let it play, do nothing.

        if (!mayRecover)
            _audioFocusActivation = null;
        var restoreFocusHandler = HandleLostAudioFocus(PlaybackState.Value, mayRecover);
        return restoreFocusHandler;
    }

    private RestoreFocusHandler? HandleLostAudioFocus(PlaybackState? state, bool mayRecover)
    {
        Log.LogInformation("Lost audio focus. State: '{State}'", state);
        if (state is null)
            return null; // We should never get here.

        if (state is HistoricalPlaybackState historicalPlaybackState) {
            var paused = true;
            lock (Lock) {
                var activePlayers = _players.Values.Select(c => c.ChatPlayer).Where(c => c.Playback.IsPlaying.Value).ToList();
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
                            .Select(c => c.ChatPlayer)
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
