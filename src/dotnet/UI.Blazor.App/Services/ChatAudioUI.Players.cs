using ActualChat.Localization;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatAudioUI
{
    private static TimeSpan RestorePreviousPlaybackStateDelay { get; } = TimeSpan.FromMilliseconds(250);

    private ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer> _players =
        ImmutableDictionary<(ChatId ChatId, ChatPlayerKind PlayerKind), ChatPlayer>.Empty;
    private readonly ConcurrentDictionary<ChatId, Moment> _listeningCatchUpAnchors = new();
    private ImmutableHashSet<ChatId> _listeningChatsBeforeReplay = ImmutableHashSet<ChatId>.Empty;
    private readonly MutableState<ReplayState?> _replayState;
    private readonly AudioFocusRequester _audioFocusRequester;
    private readonly AudioFocusRequester _listeningFocusRequester;
    private AudioFocusScope? _audioFocusScope;
    private AudioFocusScope? _listeningFocusScope;
    private int _audioFocusDenialCount;
    // MutableState, not a plain flag: ShouldHoldListeningFocus reads it, and a focus-driven pause
    // must keep that compute's hold/retry signal alive - a non-reactive read would freeze it.
    private readonly MutableState<bool> _isListeningPausedByFocus;

    public int AudioFocusDenialCount
        // Advances on every refused acquisition, listening bursts included: an advance inside a
        // PTT wake window is what makes PttSessionCore fall back to a notification.
        => Volatile.Read(ref _audioFocusDenialCount);

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
                L.Replay_ConfirmText,
                () => { confirmed = true; }) {
                Title = L.Replay_ConfirmTitle,
                ConfirmButtonText = L.Common_Yes,
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
        DebugLog?.LogInformation(
            "StartReplay: chatId={ChatId}, startAt={StartAt}, rewindOffset={RewindOffset}, speed={Speed}",
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
        if (!Ptt.IsStaleWake(anchor, ServerNow))
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
            Log.LogInformation(
                "Already have audio focus {Scope}. Request reason: '{Reason}'", _audioFocusScope, reason);
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

    public async Task AcquireListeningFocus()
    {
        if (_listeningFocusScope is not null)
            return; // Live, or suspended with its restore handler pending - either way, no re-request

        if (AudioFocusUI.IsSuspended) {
            // Another requester's focus is lost right now (e.g. a call over a paused replay).
            // A renew from here would be denied, and a denied renew wipes that requester's
            // pending restore - so hold playback instead and let the next transition retry.
            PauseListeningPlayers();
            return;
        }

        _listeningFocusScope = await AudioFocusUI.TryAcquire(_listeningFocusRequester).ConfigureAwait(false);
        if (_listeningFocusScope is null) {
            // No SetListeningState(false) here: a denial - e.g. during a real phone call - is
            // transient, and the armed listening intent is exactly what retries on the next stream.
            // Playback pauses too: a denied transient focus doesn't mute an AudioTrack, and chat
            // speech must not play over whoever holds the focus.
            Interlocked.Increment(ref _audioFocusDenialCount);
            PauseListeningPlayers();
            Log.LogWarning("AcquireListeningFocus: failed to gain audio focus");
            return;
        }

        ResumeListeningPlayersPausedByFocus();
    }

    public void ReleaseListeningFocus()
    {
        _listeningFocusScope?.Dispose();
        _listeningFocusScope = null;
    }

    // Private player lifecycle methods

    private ChatPlayer GetOrCreatePlayer(ChatId chatId, ChatPlayerKind playerKind)
    {
        StopToken.ThrowIfCancellationRequested();
        ChatPlayer newPlayer;
        lock (Lock) {
            var player = _players.GetValueOrDefault((chatId, playerKind));
            if (player is not null) {
                DebugLog?.LogInformation(
                    "GetOrCreatePlayer: returning existing {PlayerKind} player for {ChatId}", playerKind, chatId);
                return player;
            }

            DebugLog?.LogInformation(
                "GetOrCreatePlayer: creating new {PlayerKind} player for {ChatId}", playerKind, chatId);
            newPlayer = playerKind switch {
                ChatPlayerKind.Listening => new ChatListeningPlayer(Hub, chatId),
                ChatPlayerKind.Replaying => new ChatReplayPlayer(Hub, chatId),
                _ => throw new ArgumentOutOfRangeException(nameof(playerKind), playerKind, null),
            };
            // Publication for StopAllPlayers' lock-free read.
            Volatile.Write(ref _players, _players.Add((chatId, playerKind), newPlayer));
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
        // Pairs with the Volatile.Write publication in GetOrCreatePlayer.
        => Volatile.Read(ref _players)
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
            // EnsureSynced beats waiting for that worker: it forces a fresh measurement right here,
            // so the startAt anchor below can't be captured off a clock that just woke from a sleep.
            try {
                var whenSynced = Hub.Services.GetService<ServerTimeSync>() is { } serverTimeSync
                    ? serverTimeSync.EnsureSynced(cancellationToken)
                    : serverClock.WhenReady;
                await whenSynced
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
        // Listening deliberately survives this: it holds no Playback focus anymore - incoming
        // speech runs on the transient Listening scope, which recovers on its own per utterance.
        Log.LogInformation("OnAudioFocusLost: mayRecover={MayRecover}, canDuck={CanDuck}", mayRecover, canDuck);
        if (canDuck)
            return null; // Do not stop players. We don't support ducking so far, so just let it play, do nothing.

        if (!mayRecover)
            _audioFocusScope = null;

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

    private AudioFocusRestoreHandler? OnListeningFocusLost(bool mayRecover, bool canDuck)
    {
        // Listening stays armed on any loss; only playback is silenced, since a lost focus doesn't
        // mute an AudioTrack and in-flight speech must not play over whoever took the focus.
        Log.LogInformation("OnListeningFocusLost: mayRecover={MayRecover}, canDuck={CanDuck}", mayRecover, canDuck);
        if (canDuck)
            return null;

        PauseListeningPlayers();
        if (mayRecover) {
            // The scope survives: the platform request stays registered, so the recover callback
            // unsuspends it and this handler brings the paused players back.
            return ResumeListeningPlayersPausedByFocus;
        }

        // A permanent loss never recovers - the scope would pin a dead platform request, so it
        // goes now; ManageListeningFocusBursts re-acquires from scratch on the next stream.
        ReleaseListeningFocus();
        return null;
    }

    private void PauseListeningPlayers()
    {
        _isListeningPausedByFocus.Value = true;
        foreach (var player in Volatile.Read(ref _players).Values.OfType<ChatListeningPlayer>())
            player.Pause();
    }

    private void ResumeListeningPlayersPausedByFocus()
    {
        if (!_isListeningPausedByFocus.Value)
            return;

        _isListeningPausedByFocus.Value = false;
        foreach (var player in Volatile.Read(ref _players).Values.OfType<ChatListeningPlayer>())
            _ = player.Resume();
    }
}
