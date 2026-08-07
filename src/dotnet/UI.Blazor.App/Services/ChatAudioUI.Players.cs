using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatAudioUI
{
    private static TimeSpan RestorePreviousPlaybackStateDelay { get; } = TimeSpan.FromMilliseconds(250);

    private volatile ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer> _players =
        ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer>.Empty;
    private readonly ConcurrentDictionary<ChatId, Moment> _listeningCatchUpAnchors = new();
    private ImmutableHashSet<ChatId> _listeningChatsBeforeReplay = ImmutableHashSet<ChatId>.Empty;
    private readonly MutableState<ReplayState?> _replayState;
    private readonly AudioFocusRequester _audioFocusRequester;
    private AudioFocusScope? _audioFocusScope;
    private int _audioFocusDenialCount;

    // Advances on every refused acquisition - the state sync answers those by dropping the state.
    public int AudioFocusDenialCount => Volatile.Read(ref _audioFocusDenialCount);

    // Compute methods

    [ComputeMethod]
    public virtual Task<ChatListeningPlayer?> GetListeningPlayer(ChatId chatId, CancellationToken cancellationToken)
    {
        var player = GetListeningPlayerNonComputed(chatId);
        return Task.FromResult(player);
    }

    [ComputeMethod]
    public virtual Task<ChatReplayPlayer?> GetReplayPlayer(ChatId chatId, CancellationToken cancellationToken)
    {
        var player = GetReplayPlayerNonComputed(chatId);
        return Task.FromResult(player);
    }

    [ComputeMethod]
    public virtual async Task<TimeSpan> GetPlaybackTargetBufferSize(ChatId chatId, CancellationToken cancellationToken)
    {
        var hasVideo = await ChatVideoUI.HasRemoteStreams(chatId, cancellationToken).ConfigureAwait(false);
        return hasVideo
            ? Constants.Audio.PlaybackTargetBufferSizeWithVideo
            : Constants.Audio.PlaybackTargetBufferSize;
    }

    // GetXxxNonComputed

    public ChatListeningPlayer? GetListeningPlayerNonComputed(ChatId chatId)
    {
        lock (Lock)
            return _players.GetValueOrDefault((chatId, ChatPlayerKind.Listening)) as ChatListeningPlayer;
    }

    public ChatReplayPlayer? GetReplayPlayerNonComputed(ChatId chatId)
    {
        lock (Lock)
            return _players.GetValueOrDefault((chatId, ChatPlayerKind.Replaying)) as ChatReplayPlayer;
    }

    // Actions

    public async Task StartReplay(ChatId chatId, Moment startAt, TimeSpan rewindOffset = default)
    {
        // If listening is active, ask user to confirm stopping it
        var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
        if (!listeningChatIds.IsEmpty) {
            var confirmed = false;
            var model = new ConfirmModal.Model(false,
                "Replay will pause listening, then resume it after you stop replay. Continue?",
                () => { confirmed = true; }) {
                Title = "Start replay?",
                ConfirmButtonText = "Yes",
            };
            var modalRef = await ModalUI.Show(model).ConfigureAwait(false);
            await modalRef.WhenClosed.ConfigureAwait(false);
            if (!confirmed)
                return;

            // One-shot per replay session: preserve the snapshot across replay switches
            // (when a stop transition hasn't been processed yet by StartStopReplayingPlayers).
            lock (Lock) {
                if (_listeningChatsBeforeReplay.IsEmpty)
                    _listeningChatsBeforeReplay = listeningChatIds;
            }
            await ClearListeningChats().ConfigureAwait(false);
        }

        var speed = ReplaySettings.Value.Speed;
        DebugLog?.LogInformation("StartReplay: chatId={ChatId}, startAt={StartAt}, rewindOffset={RewindOffset}, speed={Speed}",
            chatId, startAt, rewindOffset, speed);

        StopReplay();
        _replayState.Value = new ReplayState(chatId, startAt, rewindOffset, speed);

        // Replay and audio-attachment playback are mutually exclusive.
        _ = Hub.AudioAttachmentPlayer.Stop();
    }

    public void SetListeningCatchUp(ChatId chatId, Moment anchor)
        // Wake-driven from-start join: the next listening player run for this chat asks the
        // server to serve streams beginning at/after the anchor from t=0.
        => _listeningCatchUpAnchors[chatId] = anchor;

    public Moment GetListeningCatchUp(ChatId chatId)
    {
        if (!_listeningCatchUpAnchors.TryGetValue(chatId, out var anchor))
            return default;
        if (!WalkieTalkie.IsStaleWake(anchor, ServerNow))
            return anchor;

        _listeningCatchUpAnchors.TryRemove(chatId, out _);
        return default;
    }

    public void PauseReplay(Moment pausedAt)
    {
        var state = _replayState.Value;
        if (state is null)
            return;

        DebugLog?.LogInformation("PauseReplay: chatId={ChatId}, pausedAt={PausedAt}", state.ChatId, pausedAt);
        _replayState.Value = state with { PausedAt = pausedAt };
    }

    public void StopReplay()
        => _replayState.Value = null;

    public async Task<bool> TryAcquireAudioFocusForResume(ChatPlayer player)
    {
        Log.LogInformation("Trying to gain audio focus for chat player '{ChatId}'", player.ChatId);
        var scope = await TryAcquireAudioFocus($"Resuming chat player '{player.ChatId}'").ConfigureAwait(false);
        return scope is not null;
    }

    // Audio focus management

    public async Task<AudioFocusScope?> TryAcquireAudioFocus(string? reason = "")
    {
        if (_audioFocusScope is not null && !_audioFocusScope.IsSuspended) {
            Log.LogInformation("Already have audio focus {Scope}. Request reason: '{Reason}'", _audioFocusScope, reason);
            return _audioFocusScope;
        }

        _audioFocusScope = await AudioFocusUI.TryAcquire(_audioFocusRequester).ConfigureAwait(false);
        if (_audioFocusScope is null)
            Interlocked.Increment(ref _audioFocusDenialCount);
        return _audioFocusScope;
    }

    public void TryReleaseAudioFocus()
    {
        _audioFocusScope?.Dispose();
        _audioFocusScope = null;
    }

    // Private player lifecycle methods

    private ChatPlayer GetOrCreatePlayer(ChatId chatId, ChatPlayerKind playerKind)
    {
        StopToken.ThrowIfCancellationRequested();
        ChatPlayer newPlayer;
        lock (Lock) {
            var player = _players.GetValueOrDefault((chatId, playerKind));
            if (player != null) {
                DebugLog?.LogInformation("GetOrCreatePlayer: returning existing {PlayerKind} player for {ChatId}", playerKind, chatId);
                return player;
            }
            DebugLog?.LogInformation("GetOrCreatePlayer: creating new {PlayerKind} player for {ChatId}", playerKind, chatId);
            newPlayer = playerKind switch {
                ChatPlayerKind.Listening => new ChatListeningPlayer(Hub, chatId),
                ChatPlayerKind.Replaying => new ChatReplayPlayer(Hub, chatId),
                _ => throw new ArgumentOutOfRangeException(nameof(playerKind), playerKind, null),
            };
            _players = _players.Add((chatId, playerKind), newPlayer);
        }
        if (playerKind is ChatPlayerKind.Replaying)
            using (Invalidation.Begin())
                _ = GetReplayPlayer(chatId, default);
        if (playerKind is ChatPlayerKind.Listening)
            using (Invalidation.Begin())
                _ = GetListeningPlayer(chatId, default);
        return newPlayer;
    }

    private Task StopPlayer(ChatId chatId, ChatPlayerKind playerKind)
    {
        ChatPlayer? player;
        lock (Lock)
            player = _players.GetValueOrDefault((chatId, playerKind));
        if (player is null)
            return Task.CompletedTask;

        return player.Stop();
    }

    private Task StopPlayers(IEnumerable<ChatId> chatIds, ChatPlayerKind playerKind)
        => chatIds
            .Select(chatId => StopPlayer(chatId, playerKind))
            .Collect(ApiConstants.Concurrency.Unlimited);

    private Task StopAllPlayers()
        // ReSharper disable once InconsistentlySynchronizedField
        => _players
            .Select(kv => StopPlayer(kv.Key.ChatId, kv.Key.PlayerKind))
            .Collect(ApiConstants.Concurrency.Unlimited);

    private async Task<Task> StartListeningPlayer(ChatId chatId, CancellationToken cancellationToken)
    {
        var player = GetOrCreatePlayer(chatId, ChatPlayerKind.Listening);
        var whenPlaying = player.WhenPlaying;
        if (whenPlaying is { IsCompleted: false })
            return whenPlaying;

        var serverClock = Clocks.ServerClock;
        // A wake catch-up carries its own server-time anchor and is served from the stream's start,
        // so it needs no clock - and on a cold headless boot the first sync is seconds away, which
        // is the very delay the catch-up exists to erase.
        var catchUpFrom = GetListeningCatchUp(chatId);
        if (catchUpFrom == default) {
            // Bounded: the clock is synced by a background worker, so anything that keeps that worker
            // from running (a wake-driven headless scope used to) would otherwise wedge listening
            // here for the life of the scope - silently, since a hang throws nothing.
            try {
                await serverClock.WhenReady
                    .WaitAsync(Constants.Audio.ServerClockWaitTimeout, cancellationToken)
                    .ConfigureAwait(true);
            }
            catch (TimeoutException) {
                Log.LogWarning(nameof(StartListeningPlayer)
                    + ": server clock isn't synced yet, starting the listener for chat #{ChatId} anyway", chatId);
            }
        }

        await Chats.Get(Hub.Session, chatId, cancellationToken).ConfigureAwait(true); // Just to cache it
        // The anchor is server time straight from the wake, so it beats an unsynced clock's Now.
        var startAt = catchUpFrom != default ? catchUpFrom : serverClock.Now;
        return await player.Start(startAt, cancellationToken).ConfigureAwait(false);
    }

    private Task<Task> StartReplayPlayer(ChatId chatId, Moment startAt, CancellationToken cancellationToken)
    {
        var player = GetOrCreatePlayer(chatId, ChatPlayerKind.Replaying);
        return player.Start(startAt, cancellationToken);
    }

    private AudioFocusRestoreHandler? OnAudioFocusLost(bool mayRecover, bool canDuck)
    {
        Log.LogInformation("OnAudioFocusLost: mayRecover={MayRecover}, canDuck={CanDuck}", mayRecover, canDuck);
        if (canDuck)
            return null; // Do not stop players. We don't support ducking so far, so just let it play, do nothing.

        if (!mayRecover)
            _audioFocusScope = null;

        _ = ClearListeningChats();
        var replayState = _replayState.Value;
        if (replayState is null || replayState.PausedAt.HasValue)
            return null;

        // Pause replay by stopping the stream and remembering position
        // Use StartAt as fallback if we can't determine the current position
        var pausedAt = replayState.StartAt;
        lock (Lock) {
            var chatReplayer = _players.Values
                .OfType<ChatReplayPlayer>()
                .FirstOrDefault(c => c.ChatId == replayState.ChatId);
            if (chatReplayer is null)
                return null;
        }
        PauseReplay(pausedAt);
        Log.LogInformation("OnAudioFocusLost: paused replayer for #{ChatId}", replayState.ChatId);

        if (!mayRecover)
            return null;

        // Return the handler that restores replay on audio focus restore
        return () => {
            Log.LogInformation("OnAudioFocusRestore: restored audio focus");
            var currentState = _replayState.Value;
            if (currentState is { PausedAt: not null }) {
                var resumeAt = currentState.PausedAt.Value;
                _ = StartReplay(currentState.ChatId, resumeAt);
                Log.LogInformation("OnAudioFocusRestore: resumed replayer for #{ChatId}", currentState.ChatId);
            }
        };
    }
}
