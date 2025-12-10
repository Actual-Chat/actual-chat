using ActualChat.UI.Blazor.Services;
using ActualLab.Interception;

namespace ActualChat.UI.Blazor.App.Services;

public class ChatPlayers : UIWorkerBase<AppUIHub>, IComputeService, INotifyInitialized
{
    private static TimeSpan RestorePreviousPlaybackStateDelay { get; } = TimeSpan.FromMilliseconds(250);

    private volatile ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayerController> _players =
        ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayerController>.Empty;

    private readonly MutableState<PlaybackState?> _playbackState;
    private readonly AudioFocusConsumer _audioFocusConsumer;
    private IAudioFocusActivation? _audioFocusActivation;

    private ChatAudioUI ChatAudioUI => Hub.ChatAudioUI;

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

    public void StartHistoricalPlayback(ChatId chatId, Moment startAt)
        => StartPlayback(new HistoricalPlaybackState(chatId, startAt));

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

    public void ReleaseAudioFocusDueToPause(HistoricalChatPlayerController controller)
    {
        Log.LogInformation("Releasing audio focus for historical chat player '{ChatId}'", controller.ChatId);

        if (_playbackState.Value is HistoricalPlaybackState historicalPlaybackState
            && historicalPlaybackState.ChatId == controller.ChatId) {
            ReleaseAudioFocus();
        }
    }

    public async Task<bool> TryGainAudioFocusForResume(HistoricalChatPlayerController controller)
    {
        Log.LogInformation("Trying to gain audio focus for historical chat player '{ChatId}'", controller.ChatId);
        var audioFocusHandle = await TryGainAudioFocus($"Resuming historical chat player '{controller.ChatId}'").ConfigureAwait(false);
        return audioFocusHandle is not null;
    }

    public void UpdateMediaSessionState()
        => AudioWidgetSession.UpdateMediaSessionState();

    // Protected methods

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // TODO(AY): Implement _players cleanup here
        try {
            var lastPlaybackState = (PlaybackState?)null;
            var changes = PlaybackState.Computed.Changes(cancellationToken);
            await foreach (var cPlaybackState in changes.ConfigureAwait(false)) {
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
        finally {
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
        if (playbackState == null) {
            await ExitState(lastPlaybackState).ConfigureAwait(false);
            ReleaseAudioFocus();
            AudioWidgetSession.OnPlaybackStateChanged(null);
            return;
        }

        var audioFocusActivation = await TryGainAudioFocus("Playback state change").ConfigureAwait(false);
        if (audioFocusActivation is null) {
            // Failed to get audio focus, stop playback. Show toast?
            StopPlayback();
            return;
        }

        if (lastPlaybackState?.GetType() != playbackState?.GetType()) {
            // Mode type change
            await ExitState(lastPlaybackState).ConfigureAwait(false);
            await EnterState(playbackState, cancellationToken).ConfigureAwait(false);
            AudioWidgetSession.OnPlaybackStateChanged(playbackState);
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
        return;

        async Task EnterState(PlaybackState? state, CancellationToken ct)
        {
            if (state is HistoricalPlaybackState historical) {
                _ = TuneUI.Play(Tune.StartHistoricalPlayback);
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
            if (player != null)
                return player.ChatPlayer;
            newPlayer = playerKind switch {
                ChatPlayerKind.Realtime => new RealtimeChatPlayer(Hub, chatId),
                ChatPlayerKind.Historical => new HistoricalChatPlayer(Hub, chatId),
                _ => throw new ArgumentOutOfRangeException(nameof(playerKind), playerKind, null),
            };
            var controller = newPlayer is HistoricalChatPlayer historicalChatPlayer
                ? new HistoricalChatPlayerController(historicalChatPlayer, this, Log)
                : new ChatPlayerController(newPlayer, this);
            _players = _players.Add((chatId, playerKind), controller);
        }
        if (playerKind is ChatPlayerKind.Historical)
            using (Invalidation.Begin())
                _ = GetHistoricalChatPlayerController(chatId, default);
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
    }

    private Task<Task> ResumeRealtimePlayback(ChatId chatId, CancellationToken cancellationToken)
    {
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
            .Collect(ApiConstants.Concurrency.Unlimited, cancellationToken)
            .ConfigureAwait(false);
        return Task.WhenAll(resultPlayingTasks);
    }

    private Task<Task> StartHistoricalPlayback(ChatId chatId, Moment startAt, CancellationToken cancellationToken)
    {
        var player = GetOrCreate(chatId, ChatPlayerKind.Historical);
        var startTask = player.Start(startAt, cancellationToken);
        player.Playback.IsPaused.Updated += IsPausedOnUpdated;
        return startTask;
    }

    private Task StopPlayer(ChatId chatId, ChatPlayerKind playerKind)
    {
        ChatPlayerController? controller;
        lock (Lock)
            controller = _players.GetValueOrDefault((chatId, playerKind));
        if (controller is null)
            return Task.CompletedTask;

        if (controller.ChatPlayer is HistoricalChatPlayer)
            controller.ChatPlayer.Playback.IsPaused.Updated -= IsPausedOnUpdated;
        return controller.ChatPlayer.Stop();
    }

    private void IsPausedOnUpdated(State state, StateEventKind kind)
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
