using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Services;
using ActualLab.Resilience;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatAudioUI
{
    private static readonly TimeSpan Epsilon = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MinListeningPlayerPlayDurationToConsiderHealthy = TimeSpan.FromSeconds(10);
    // Long enough to bridge the gap between consecutive utterances of one conversation, so music
    // isn't bounced on every pause in speech; short enough that it comes back in a real lull.
    private static readonly TimeSpan ListeningFocusLinger = TimeSpan.FromSeconds(4);
    private static readonly int MaxStopRecordingTryCount = 3;
    private static readonly TimeSpan DiagnosticsTimeout = TimeSpan.FromSeconds(5);

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // All logic here can be delayed to let other code run
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true); // Intended
        var baseChains = new[] {
            AsyncChain.From(InitializeListening),
            AsyncChain.From(StopListeningWhenPttDisarmed),
            AsyncChain.From(InvalidateActiveChatDependencies),
            AsyncChain.From(InvalidateReplayDependencies),
            AsyncChain.From(PushRecordingState),
            AsyncChain.From(StartStopListeningPlayers),
            AsyncChain.From(ManageListeningFocusBursts),
            AsyncChain.From(StartStopReplayingPlayers),
            AsyncChain.From(StopReplayWhenRecordingStarts),
            AsyncChain.From(StopListeningWhenIdle),
            AsyncChain.From(StopListeningWhenIdleInBackground),
            AsyncChain.From(StopRecordingAndReplayOnDeviceAwake),
            AsyncChain.From(UpdateNextBeepAt),
            AsyncChain.From(PlayBeep),
            AsyncChain.From(WarnBeforeRecordingStop),
            AsyncChain.From(RecordingTroubleshooter),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        await (
            from chain in baseChains
            select chain
                .WithTransiencyResolver(TransiencyResolvers.PreferTransient)
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken)
            .ConfigureAwait(false);
    }

    // Private methods

    private async Task InitializeListening(CancellationToken cancellationToken)
    {
        // A SetListeningState landing before the stored active chats are read would make
        // StoredState discard them.
        await ActiveChatsUI.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        await RestoreKeepListeningChats(cancellationToken).ConfigureAwait(false);
    }

    private async Task StopListeningWhenPttDisarmed(CancellationToken cancellationToken)
    {
        var cKeepListeningChatIds = await Computed
            .Capture(() => GetChatsYouNeedToKeepListeningTo(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var oldChatIds = (HashSet<ChatId>?)null;
        await foreach (var c in cKeepListeningChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            var chatIds = c.Value.ToHashSet();
            if (oldChatIds is not null) {
                // Arming is the only thing that keeps such a chat listening, and StopListeningWhenIdle
                // deliberately runs no watcher for it - so leaving PTT is what must end that listening,
                // ongoing conversation or not.
                foreach (var chatId in oldChatIds.Except(chatIds))
                    await SetListeningState(chatId, false).ConfigureAwait(false);
            }
            oldChatIds = chatIds;
        }
    }

    private async Task InvalidateActiveChatDependencies(CancellationToken cancellationToken)
    {
        var oldRecordingChat = default(ActiveChat);
        var oldListeningChats = new HashSet<ActiveChat>();
        var changes = ActiveChatsUI.ActiveChats.Computed.ChangesUntyped(FixedDelayer.NoneUnsafe, cancellationToken);
        await foreach (var c in changes.ConfigureAwait(false)) {
            var cActiveContacts = (Computed<ActiveChat[]>)c;
            var activeChats = cActiveContacts.Value;
            var newRecordingChat = activeChats.FirstOrDefault(x => x.IsRecording);
            var newListeningChats = activeChats.Where(x => x.IsListening).ToHashSet();

            DebugLog?.LogDebug("InvalidateActiveChatDependencies: *");
            var added = newListeningChats.Except(oldListeningChats);
            var removed = oldListeningChats.Except(newListeningChats);
            var changed = added.Concat(removed).ToList();

            using (Invalidation.Begin()) {
                if (newRecordingChat != oldRecordingChat) {
                    _ = GetRecordingChatId();
                    if (oldRecordingChat != null)
                        _ = GetState(oldRecordingChat.ChatId);
                    if (newRecordingChat != null)
                        _ = GetState(newRecordingChat.ChatId);
                }
                if (changed.Count > 0) {
                    _ = GetListeningChatIds();
                    foreach (var activeChat in changed)
                        _ = GetState(activeChat.ChatId);
                }
            }

            oldRecordingChat = newRecordingChat;
            oldListeningChats = newListeningChats;
        }
    }

    private async Task InvalidateReplayDependencies(CancellationToken cancellationToken)
    {
        var oldChatId = (ChatId?)null;
        var changes = _replayState.Computed.Changes(cancellationToken);
        await foreach (var cPlaybackState in changes.ConfigureAwait(false)) {
            var newChatId = cPlaybackState.Value?.ChatId;
            if (newChatId == oldChatId)
                continue;

            DebugLog?.LogDebug("InvalidateReplayDependencies: *");
            using (Invalidation.Begin()) {
                if (oldChatId is not null)
                    _ = GetState(oldChatId);
                if (newChatId is not null)
                    _ = GetState(newChatId);
            }

            oldChatId = newChatId;
        }
    }

    private async Task PushRecordingState(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var cRecordingStateBase = await Computed
            .Capture(() => GetRecordingState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var restartDelays = RetryDelaySeq.Exp(0.5, 8);
        var restartAttempt = 0;
        var restartChatId = (ChatId?)null;
        while (!cancellationToken.IsCancellationRequested) {
            var cRecordingState = await cRecordingStateBase
                .When(x => x.ChatId is not null, FixedDelayer.MinDelay, cancellationToken)
                .ConfigureAwait(false);
            var intendedChatId = cRecordingState.Value.ChatId;
            // A chat switched to during the backoff delay is a fresh user start, not attempt N+1 -
            // it gets its own begin tune and its own retry budget.
            if (intendedChatId != restartChatId) {
                restartAttempt = 0;
                restartChatId = intendedChatId;
            }
            if (restartAttempt > 0)
                Log.LogInformation(
                    nameof(PushRecordingState) + ": retrying recorder for chat #{ChatId} (attempt {Attempt})",
                    intendedChatId, restartAttempt);
            await BackgroundTask.Run(
                () => RecordChat(cRecordingState, restartAttempt > 0, cancellationToken),
                Log, $"{nameof(RecordChat)} failed",
                cancellationToken
                ).SilentAwait(false);

            // If user intent for the same chat persists, RecordChat exited unexpectedly
            // (recorder died, mic permission failure, etc.). Back off before re-entering.
            var latest = await cRecordingStateBase.Update(cancellationToken).ConfigureAwait(false);
            if (latest.Value.ChatId == intendedChatId) {
                restartAttempt++;
                var delay = restartDelays[restartAttempt];
                Log.LogWarning(
                    nameof(PushRecordingState)
                    + ": recorder for chat #{ChatId} exited with user intent intact"
                    + " (attempt {Attempt}); restarting in {Delay}",
                    intendedChatId, restartAttempt, delay);
                await Clocks.CpuClock.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            else {
                if (restartAttempt > 0)
                    Log.LogInformation(
                        nameof(PushRecordingState)
                        + ": recorder for chat #{ChatId} stopped after {Attempt} restart attempt(s)",
                        intendedChatId, restartAttempt);
                restartAttempt = 0;
            }
        }
    }

    private async Task StopReplayWhenRecordingStarts(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var cRecordingState = await Computed
            .Capture(() => GetRecordingState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            cRecordingState = await cRecordingState
                .When(x => x.ChatId is not null, FixedDelayer.MinDelay, cancellationToken)
                .ConfigureAwait(false);
            var chatId = cRecordingState.Value.ChatId;
            if (_replayState.Value is { } replayState && replayState.ChatId != chatId)
                StopReplay();
            cRecordingState = await cRecordingState
                .When(x => x.ChatId is null || x.ChatId != chatId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RecordChat(
        Computed<RecordingState> cRecordingState,
        bool isRestart,
        CancellationToken cancellationToken)
    {
        AudioInitializer.StartInitialization();
        var serverClock = Clocks.ServerClock;
        var cpuClock = Clocks.CpuClock;
        var (chatId, language) = cRecordingState.Value;
        if (chatId is null)
            throw new ArgumentOutOfRangeException(nameof(cRecordingState));

        if (!InteractiveUI.IsInteractive.Value) {
            var isConfirmed = false;
            var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat != null) {
                var operation = $"recording in \"{chat.Title}\"";
                isConfirmed = await InteractiveUI.Demand(operation, cancellationToken).ConfigureAwait(false);
            }

            if (!isConfirmed) {
                await SetRecordingChatId(null).ConfigureAwait(false);
                return;
            }
        }

        if (!cRecordingState.IsConsistent())
            return;

        Task? whenStopped = null;
        Task? whenRecorderStopped = null;
        Task? whenIdle = null;
        Task? whenWinner = null;
        var abortTokenSource = cancellationToken.CreateLinkedTokenSource();
        var abortToken = abortTokenSource.Token;
        try {
            var relatedEntryRef = await ChatEditorUI.RelatedEntryRef.Use(abortToken).ConfigureAwait(false);
            var repliedEntryId = relatedEntryRef is { Kind: RelatedEntryKind.Reply }
                ? relatedEntryRef.EntryId
                : null;
            await ChatEditorUI.HideRelatedEntry().ConfigureAwait(false);

            // Play tune BEFORE acquiring mic — on iOS Safari, the mic's
            // audioSession is set to 'play-and-record' after getUserMedia,
            // and WebKit AEC does not register the DestinationFallbackTrait
            // playback path, so a tune playing into a live mic feeds back.
            // A restart is the recorder recovering, not the user starting: chiming on each one
            // turns a run of capture failures into a burst of tones.
            var mustPlayBeginTune = !isRestart && !Volatile.Read(ref _isBeginTuneSuppressed);
            await TuneUI.PlayAndWait(Tune.BeginRecording, mustPlay: mustPlayBeginTune).ConfigureAwait(false);
            // Install before StartRecording so we don't miss a fast false→true→false
            // transition (e.g., pipeline dies during JS init).
            whenRecorderStopped = ForegroundTask.Run(async () => {
                var sawRecording = false;
                await foreach (var c in AudioRecorder.State.Computed.Changes(abortToken).ConfigureAwait(false)) {
                    var s = c.Value;
                    if (s.ChatId != chatId)
                        continue;

                    if (s.IsRecording)
                        sawRecording = true;
                    else if (sawRecording)
                        return;
                }
            }, abortToken);
            await AudioRecorder.StartRecording(chatId, repliedEntryId, abortToken).ConfigureAwait(false);
            _ = IncomingShareSuggestions?.Push(chatId);
            whenStopped = ForegroundTask.Run(
                async () => await cRecordingState
                    .When(x => x.ChatId != chatId || x.Language != language, abortToken)
                    .ConfigureAwait(false),
                abortToken);
            whenIdle = ForegroundTask.Run(async () => {
                // Paired with the publication in SetRecordingChatId.
                var idleDuration = (TimeSpan?)Volatile.Read(ref _recordingIdleDurationBox);
                var options = GetRecordingIdleOptions(idleDuration, AudioSettings);
                var streamingIdleBoundaries = ObserveStreamingIdleBoundaries(chatId, options, abortToken);
                await foreach (var serverStopAt in streamingIdleBoundaries.ConfigureAwait(false)) {
                    // MutableState.Set invalidates unconditionally, and the boundaries repeat the
                    // same stopAt on every countdown tick - each redundant set re-renders the header.
                    var stopAt = serverStopAt.Convert(serverClock, cpuClock);
                    if (_stopRecordingAt.Value != stopAt)
                        _stopRecordingAt.Value = stopAt;
                }
            }, abortToken);
            whenWinner = await Task.WhenAny(whenStopped, whenIdle, whenRecorderStopped).ConfigureAwait(false);
        }
        finally {
            abortTokenSource.CancelAndDisposeSilently();
            _stopRecordingAt.Value = null;
            // Task.WhenAny never throws, so a faulted watcher is indistinguishable from a clean one
            // here - and until this, a faulted whenIdle read as a genuine idle timeout below.
            LogWatcherFault(whenStopped, chatId);
            LogWatcherFault(whenRecorderStopped, chatId);
            LogWatcherFault(whenIdle, chatId);
            // Only a whenIdle that ran to completion means the mic went idle. Anything else -
            // a fault, or a throw before whenIdle was even assigned, which is every failure to
            // open the microphone - used to land here as ReferenceEquals(null, null) and clear
            // the user's recording intent with nothing but an "idle threshold reached" log.
            if (whenIdle is { IsCompletedSuccessfully: true } && ReferenceEquals(whenWinner, whenIdle)) {
                Log.LogInformation(
                    nameof(RecordChat) + ": idle threshold reached for chat #{ChatId}, stopping recording",
                    chatId);
                await SetRecordingChatId(null).ConfigureAwait(false);
            }

            // Stopping the recording
            for (var tryIndex = 0;; tryIndex++) {
                if (await AudioRecorder.StopRecording(CancellationToken.None).ConfigureAwait(false)) {
                    _ = TuneUI.Play(Tune.EndRecording);
                    break;
                }

                if (tryIndex >= MaxStopRecordingTryCount) {
                    Log.LogError(nameof(RecordChat) + ": couldn't stop recording in {TryCount} tries",
                        MaxStopRecordingTryCount);
                    break;
                }

                await Clocks.CpuClock.Delay(1000, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private void LogWatcherFault(Task? watcherTask, ChatId chatId)
        // Attached rather than awaited: the losers are still running when RecordChat exits, and an
        // unobserved fault would otherwise surface as an UnobservedTaskException and nothing else.
        => _ = watcherTask?.ContinueWith(
            (t, state) => Log.LogError(t.Exception,
                nameof(RecordChat) + ": a recording watcher failed for chat #{ChatId}",
                (ChatId)state!),
            chatId,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task StartStopListeningPlayers(CancellationToken cancellationToken)
    {
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var cListeningChatIds = await Computed
            .Capture(GetListeningChatIds, cancellationToken)
            .ConfigureAwait(false);
        var lastChatIds = ImmutableHashSet<ChatId>.Empty;
        var playerWorkers = new Dictionary<ChatId, FuncWorker>();

        await foreach (var change in cListeningChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            var newChatIds = change.Value;
            try {
                var removedChatIds = lastChatIds.Except(newChatIds);
                var addedChatIds = newChatIds.Except(lastChatIds);

                if (!removedChatIds.IsEmpty) {
                    await StopPlayers(removedChatIds, ChatPlayerKind.Listening).ConfigureAwait(false);
                    foreach (var chatId in removedChatIds)
                        if (playerWorkers.Remove(chatId, out var worker))
                            await worker.Stop().ConfigureAwait(false);
                }

                if (!addedChatIds.IsEmpty) {
                    // No focus here: listening no longer owns one while idle. ManageListeningFocusBursts
                    // takes a transient focus per incoming stream, so other apps' audio keeps playing
                    // in the silence between utterances.
                    if (lastChatIds.IsEmpty)
                        _ = TuneUI.Play(Tune.StartListening);
                    foreach (var chatId in addedChatIds)
                        playerWorkers[chatId] = FuncWorker.Start(
                            ct => KeepListeningPlayerAlive(chatId, ct),
                            cancellationToken);
                }

                if (newChatIds.IsEmpty && !lastChatIds.IsEmpty)
                    _ = TuneUI.Play(Tune.StopListening);

                lastChatIds = newChatIds;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                Log.LogError(ex, "ManageListeningPlayers failed");
                await StopAllPlayers().ConfigureAwait(false);
                foreach (var worker in playerWorkers.Values)
                    await worker.Stop().ConfigureAwait(false);
                playerWorkers.Clear();
                lastChatIds = ImmutableHashSet<ChatId>.Empty;
            }
        }
    }

    private async Task KeepListeningPlayerAlive(ChatId chatId, CancellationToken cancellationToken)
    {
        var restartDelays = RetryDelaySeq.Exp(0.5, 8);
        var restartAttempt = 0;
        while (!cancellationToken.IsCancellationRequested) {
            var startedAt = CpuTimestamp.Now;
            try {
                var whenPlaying = await StartListeningPlayer(chatId, cancellationToken).ConfigureAwait(false);
                await whenPlaying.SilentAwait(false);
            }
            catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                // A throw here used to kill this worker silently, leaving the chat in the listening
                // set with no player behind it - nothing retries, and the PTT wake path hits
                // exactly that: it starts the listener on an RPC connection that reconnected a
                // moment ago, so a transient failure is the norm rather than the exception.
                Log.LogWarning(e,
                    nameof(KeepListeningPlayerAlive) + ": couldn't start the listener for chat #{ChatId}",
                    chatId);
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
            if (!listeningChatIds.Contains(chatId))
                return;

            // A session that ran long enough is healthy, so the backoff starts fresh on its exit
            if (startedAt.Elapsed >= MinListeningPlayerPlayDurationToConsiderHealthy)
                restartAttempt = 0;

            restartAttempt++;
            if (restartAttempt >= 2) {
                // Repeated fast failures while the user still wants to listen usually mean the
                // platform audio graph died under us (e.g. iOS AVAudioSession/engine went inactive
                // and never recovered - the "audio's gone but headphones stay on" case). Rebuild it;
                // this is a no-op on platforms without a recovery implementation.
                Log.LogWarning(nameof(KeepListeningPlayerAlive)
                    + ": repeated unhealthy exits for chat #{ChatId}, recovering audio focus", chatId);
                await AudioFocusUI.TryRecover(cancellationToken).SilentAwait(false);
            }
            var delay = restartDelays[restartAttempt];
            Log.LogWarning(
                nameof(KeepListeningPlayerAlive)
                + ": listener for chat #{ChatId} exited with user intent intact"
                + " (attempt {Attempt}); restarting in {Delay}",
                chatId, restartAttempt, delay);
            await Clocks.CpuClock.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ManageListeningFocusBursts(CancellationToken cancellationToken)
    {
        // Playback itself doesn't wait for this focus: the acquire rides on the same server-side
        // stream flag that starts the track, so it lands around the first frames. A denial is
        // logged and retried on the next stream - it never disarms listening.
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);
        // A restart after a mid-hold crash would otherwise park in the wait below with the scope
        // still held, keeping other apps' audio paused until the next burst cycle releases it.
        ReleaseListeningFocus();
        var cMustHold = await Computed
            .Capture(() => ShouldHoldListeningFocus(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (true) {
            cMustHold = await cMustHold.When(x => x, cancellationToken).ConfigureAwait(false);
            await AcquireListeningFocus().ConfigureAwait(false);
            while (true) {
                cMustHold = await cMustHold.When(x => !x, cancellationToken).ConfigureAwait(false);
                using var lingerCts = cancellationToken.CreateLinkedTokenSource();
                lingerCts.CancelAfter(ListeningFocusLinger);
                try {
                    cMustHold = await cMustHold.When(x => x, lingerCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
                    break; // The linger elapsed with no new stream - time to hand the focus back
                }

                // A new stream arrived within the linger; the scope may be gone if focus was lost
                // mid-burst, and re-acquiring is exactly how the burst model recovers from that.
                await AcquireListeningFocus().ConfigureAwait(false);
            }
            ReleaseListeningFocus();
        }
    }

    private async Task StartStopReplayingPlayers(CancellationToken cancellationToken)
    {
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        ReplayState? lastState = null;
        var changes = _replayState.Computed.Changes(cancellationToken);
        await foreach (var cState in changes.ConfigureAwait(false)) {
            var newState = cState.Value;
            try {
                if (newState == null) {
                    // Stop replay
                    if (lastState is not null) {
                        _ = TuneUI.Play(Tune.StopReplay);
                        await StopPlayer(lastState.ChatId, ChatPlayerKind.Replaying).ConfigureAwait(false);

                        // Restore listening for chats that had it before replay started
                        ImmutableHashSet<ChatId> toRestore;
                        lock (Lock) {
                            toRestore = _listeningChatsBeforeReplay;
                            _listeningChatsBeforeReplay = ImmutableHashSet<ChatId>.Empty;
                        }
                        foreach (var chatId in toRestore)
                            await SetListeningState(chatId, true).ConfigureAwait(false);

                        // Listening runs on its own transient focus now, so replay's ends with replay
                        TryReleaseAudioFocus();
                    }
                    lastState = null;
                    continue;
                }

                if (lastState is not null) // Stop previous replay player
                    await StopPlayer(lastState.ChatId, ChatPlayerKind.Replaying).ConfigureAwait(false);

                // Paused state: stop the player/stream but keep the state (banner stays visible)
                if (newState.PausedAt.HasValue) {
                    lastState = newState;
                    continue;
                }

                // Start or switch replay
                var audioFocusScope = await TryAcquireAudioFocus("Replay").ConfigureAwait(false);
                if (audioFocusScope is null) {
                    Log.LogWarning("ManageReplay: failed to gain audio focus, stopping");
                    _replayState.Value = null;
                    continue;
                }

                _ = TuneUI.Play(Tune.StartReplay);
                var startTask = StartReplayPlayer(newState.ChatId, newState.StartAt, cancellationToken);
                // Set up "resume listening after done" background task
                _ = BackgroundTask.Run(async () => {
                    var endPlaybackTask = await startTask.ConfigureAwait(false);
                    await endPlaybackTask.ConfigureAwait(false);
                    await Clocks.CpuClock
                        .Delay(RestorePreviousPlaybackStateDelay, cancellationToken)
                        .ConfigureAwait(false);
                    // Don't clear state if it was paused or changed since we started
                    var currentState = _replayState.Value;
                    if (ReferenceEquals(currentState, newState))
                        _replayState.Value = null;
                }, cancellationToken);
                await startTask.ConfigureAwait(false);

                lastState = newState;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                Log.LogError(ex, "ManageReplay failed");
                _replayState.Value = null;
                lastState = null;
                lock (Lock)
                    _listeningChatsBeforeReplay = ImmutableHashSet<ChatId>.Empty;
            }
        }
    }

    private async Task StopListeningWhenIdle(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var listenerTimeout = Constants.Audio.ListeningDuration;
        var cListeningChatIds = await Computed
            .Capture(GetListeningChatIds, cancellationToken)
            .ConfigureAwait(false);
        var monitors = new Dictionary<ChatId, FuncWorker>();
        await foreach (var change in cListeningChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            var listeningChatIds = change.Value;
            var toStop = monitors.Keys.Except(listeningChatIds).ToList();
            var toStart = listeningChatIds.Except(monitors.Keys).ToList();

            var stopTasks = new List<Task>();
            foreach (var chatId in toStop)
                if (monitors.Remove(chatId, out var monitor))
                    stopTasks.Add(monitor.Stop());
            await stopTasks.Collect(ApiConstants.Concurrency.Unlimited, cancellationToken).ConfigureAwait(false);

            if (toStart.Count == 0)
                continue;

            var keepListeningChatIds = await GetChatsYouNeedToKeepListeningTo(cancellationToken)
                .ConfigureAwait(false);
            foreach (var chatId in toStart) {
                if (keepListeningChatIds.Contains(chatId))
                    continue; // armed PTT chats keep listening — no idle watcher

                var watcher = FuncWorker.Start(
                    ct => StopListeningWhenIdle(chatId, listenerTimeout, ct),
                    cancellationToken);
                monitors.Add(chatId, watcher);
            }
        }
    }

    private async Task StopListeningWhenIdle(
        ChatId chatId,
        TimeSpan listenerTimeout,
        CancellationToken cancellationToken)
    {
        var serverClock = Clocks.ServerClock;
        var cpuClock = Clocks.CpuClock;
        var mustStop = false;
        // Speaker session = the user recorded during this listening session, so their
        // listening-linger setting applies once the chat goes quiet; a pure listener
        // session always holds for listenerTimeout - see ComputeStopListeningAt.
        var hasRecorded = await GetRecordingChatId().ConfigureAwait(false) == chatId;
        var lastActivityAt = serverClock.Now;
        var retryDelays = RetryDelaySeq.Exp(0.5, 8);
        var retryAttempt = 0;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    await WhenRecordingChatIdBecomes(x => x != chatId, cancellationToken).ConfigureAwait(false);
                    lastActivityAt = serverClock.Now;
                    var cts = cancellationToken.CreateLinkedTokenSource();
                    try {
                        var whenRecording = WhenRecordingChatIdBecomes(x => x == chatId, cts.Token);
                        var whenIdle = WhenIdle(cts.Token);
                        var whenWinner = await Task.WhenAny(whenRecording, whenIdle).ConfigureAwait(false);
                        // Task.WhenAny itself never throws, so the winner must be awaited -
                        // an unobserved fault here would otherwise spin this loop at full speed.
                        await whenWinner.ConfigureAwait(false);
                        if (ReferenceEquals(whenWinner, whenIdle)) {
                            mustStop = true;
                            break;
                        }

                        hasRecorded = true;
                        retryAttempt = 0;
                    }
                    finally {
                        SetStopListeningAt(chatId, null);
                        cts.CancelAndDisposeSilently();
                    }
                }
                catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
                    // A fault pauses and re-enters the watch; ending the watcher here used to
                    // read as "idle" downstream and instantly stopped perfectly live sessions.
                    retryAttempt++;
                    var delay = retryDelays[retryAttempt];
                    Log.LogError(e,
                        nameof(StopListeningWhenIdle) + " failed for chat #{ChatId}; retrying in {Delay}",
                        chatId, delay);
                    await cpuClock.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally {
            SetStopListeningAt(chatId, null);
            if (mustStop) {
                var keepListeningChatIds = await GetChatsYouNeedToKeepListeningTo(cancellationToken)
                    .ConfigureAwait(false);
                if (!keepListeningChatIds.Contains(chatId))
                    await SetListeningState(chatId, false).ConfigureAwait(false);
            }
        }

        async Task WhenRecordingChatIdBecomes(Func<ChatId?, bool> predicate, CancellationToken ct) {
            var cRecordingChatId = await Computed
                .Capture(GetRecordingChatId, ct)
                .ConfigureAwait(false);
            await foreach (var (recordingChatId, _) in cRecordingChatId.Changes(ct).ConfigureAwait(false))
                if (predicate.Invoke(recordingChatId))
                    return;
        }

        async Task WhenIdle(CancellationToken ct) {
            var cHasActivity = await Computed
                .Capture(() => LiveStreamUI.HasActivity(chatId, ct), ct)
                .ConfigureAwait(false);
            var cHasRecorder = await Computed
                .Capture(() => LiveStreamUI.HasRecorder(chatId, ct), ct)
                .ConfigureAwait(false);
            var cIsWatching = await Computed
                .Capture(() => ChatVideoUI.IsWatching(chatId, ct), ct)
                .ConfigureAwait(false);
            var cHasRemoteStreams = await Computed
                .Capture(() => ChatVideoUI.HasRemoteStreams(chatId, ct), ct)
                .ConfigureAwait(false);
            var cOwnSourceKind = await Computed
                .Capture(() => ChatVideoUI.GetOwnSourceKind(chatId, ct), ct)
                .ConfigureAwait(false);
            var cSetting = await Computed
                .Capture(() => GetListeningLinger(ct), ct)
                .ConfigureAwait(false);

            while (!ct.IsCancellationRequested) {
                // The conversation holds: anyone's speech, anyone's open mic (a hot mic is a
                // conversation even mid-pause), peers' video/screencast, own video/screencast,
                // or watching the chat's video. The timer runs only when all of them clear.
                var isHeld = cHasActivity.Value
                    || cHasRecorder.Value
                    || cIsWatching.Value
                    || cHasRemoteStreams.Value
                    || cOwnSourceKind.Value is not null;
                if (isHeld) {
                    lastActivityAt = serverClock.Now;
                    SetStopListeningAt(chatId, null);
                    using var waitCts = ct.CreateLinkedTokenSource();
                    await Task.WhenAny(
                        cHasActivity.WhenInvalidated(waitCts.Token),
                        cHasRecorder.WhenInvalidated(waitCts.Token),
                        cIsWatching.WhenInvalidated(waitCts.Token),
                        cHasRemoteStreams.WhenInvalidated(waitCts.Token),
                        cOwnSourceKind.WhenInvalidated(waitCts.Token)
                        ).ConfigureAwait(false);
                    waitCts.CancelAndDisposeSilently();
                    // The hold spanned this whole wait, so the timer counts from its end -
                    // keeping the pre-wait timestamp would backdate stopAt by the length of
                    // the last activity and silently skip the countdown.
                    lastActivityAt = serverClock.Now;
                    cHasActivity = await cHasActivity.Update(ct).ConfigureAwait(false);
                    cHasRecorder = await cHasRecorder.Update(ct).ConfigureAwait(false);
                    cIsWatching = await cIsWatching.Update(ct).ConfigureAwait(false);
                    cHasRemoteStreams = await cHasRemoteStreams.Update(ct).ConfigureAwait(false);
                    cOwnSourceKind = await cOwnSourceKind.Update(ct).ConfigureAwait(false);
                    continue;
                }

                // Idle — compute stop time
                var speakerTimeout = cSetting.Value.ToTimeSpan();
                var stopAt = ComputeStopListeningAt(lastActivityAt, hasRecorded, listenerTimeout, speakerTimeout);
                var remaining = (stopAt - serverClock.Now).Positive();
                if (remaining <= Epsilon)
                    return; // Must stop listening

                SetStopListeningAt(chatId, stopAt.Convert(serverClock, cpuClock));

                // Wait for either: a hold returns, the setting changes, or the timeout expires
                using var delayCts = ct.CreateLinkedTokenSource();
                var whenTimeout = Task.Delay(remaining, delayCts.Token);
                await Task.WhenAny(
                    cHasActivity.WhenInvalidated(delayCts.Token),
                    cHasRecorder.WhenInvalidated(delayCts.Token),
                    cIsWatching.WhenInvalidated(delayCts.Token),
                    cHasRemoteStreams.WhenInvalidated(delayCts.Token),
                    cOwnSourceKind.WhenInvalidated(delayCts.Token),
                    cSetting.WhenInvalidated(delayCts.Token),
                    whenTimeout
                    ).ConfigureAwait(false);
                delayCts.CancelAndDisposeSilently();

                if (whenTimeout.IsCompletedSuccessfully)
                    return; // Idle timeout expired — stop listening

                cHasActivity = await cHasActivity.Update(ct).ConfigureAwait(false);
                cHasRecorder = await cHasRecorder.Update(ct).ConfigureAwait(false);
                cIsWatching = await cIsWatching.Update(ct).ConfigureAwait(false);
                cHasRemoteStreams = await cHasRemoteStreams.Update(ct).ConfigureAwait(false);
                cOwnSourceKind = await cOwnSourceKind.Update(ct).ConfigureAwait(false);
                cSetting = await cSetting.Update(ct).ConfigureAwait(false);
            }
        }
    }

    private void SetStopListeningAt(ChatId chatId, Moment? stopAt)
    {
        if (stopAt.HasValue)
            _stopListeningAtMap.Value = _stopListeningAtMap.Value.SetItem(chatId, stopAt.Value);
        else
            _stopListeningAtMap.Value = _stopListeningAtMap.Value.Remove(chatId);
    }

    private async Task StopListeningWhenIdleInBackground(CancellationToken cancellationToken)
    {
        // PTT hot->armed drop: in background (or a headless wake session), stop ALL
        // listening - including keep-listening (armed PTT) chats, which the watcher above
        // deliberately never stops - after PttIdleTimeout of silence. The FCM wake
        // push re-arms us.
        // Only platforms with a wake path that re-arms dropped listening:
        // FCM data pushes on Android, Apple Push to Talk on iOS.
        if (HostInfo.AppKind is not (AppKind.Android or AppKind.Ios))
            return;

        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var backgroundStateTracker = Hub.Services.GetRequiredService<BackgroundStateTracker>();
        var serverClock = Clocks.ServerClock;
        Moment? idleSince = null;
        Moment? lastActiveAt = null;
        while (!cancellationToken.IsCancellationRequested) {
            await Clocks.CpuClock.Delay(Constants.Audio.PttIdleCheckPeriod, cancellationToken)
                .ConfigureAwait(false);

            var isBackground = backgroundStateTracker.IsBackground.Value || IsPttHeadless;
            if (!isBackground) {
                (idleSince, lastActiveAt) = (null, null);
                continue;
            }

            var listeningChatIds = await GetListeningChatIds().ConfigureAwait(false);
            var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
            if (listeningChatIds.IsEmpty || _replayState.Value is not null || recordingChatId is not null) {
                (idleSince, lastActiveAt) = (null, null);
                continue;
            }

            var now = serverClock.Now;
            idleSince ??= now;
            var hasAnyActivity = false;
            foreach (var chatId in listeningChatIds)
                if (await LiveStreamUI.HasActivity(chatId, cancellationToken).ConfigureAwait(false)) {
                    hasAnyActivity = true;
                    break;
                }
            if (hasAnyActivity)
                lastActiveAt = now;

            var dropAt = Ptt.ComputeIdleDropAt(
                hasAnyActivity, lastActiveAt, idleSince.Value, Constants.Audio.PttIdleTimeout);
            if (dropAt is null || now < dropAt)
                continue;

            Log.LogInformation(
                "PTT: {Count} listening chat(s) idle in background, dropping to armed",
                listeningChatIds.Count);
            await ClearListeningChats().ConfigureAwait(false);
            (idleSince, lastActiveAt) = (null, null);
        }
    }

    private async Task StopRecordingAndReplayOnDeviceAwake(CancellationToken cancellationToken)
    {
        await DeviceAwakeUI.WhenSleepDetected(cancellationToken).ConfigureAwait(false);
        await SetRecordingChatId(null).ConfigureAwait(false);
        if (ReplayState.Value is not null)
            StopReplay();
        if (!HostInfo.AppKind.IsMaui())
            AudioRecorder.MicrophonePermission.ForgetCached();
    }

    private async Task UpdateNextBeepAt(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var cBeepState = await Computed
            .Capture(() => GetRecordingBeepState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var prevActiveUntil = Moment.MinValue;
        await foreach (var change in cBeepState.Changes(cancellationToken).ConfigureAwait(false)) {
            var nextBeep = GetNextBeep(change.Value);
            _nextBeep.Value = nextBeep;
            if (nextBeep == null)
                continue;

            prevActiveUntil = change.Value.ActiveUntil;
            change.Invalidate(nextBeep.At - CpuNow);
        }
        return;

        NextBeepState? GetNextBeep(RecordingBeepState state) {
            var (isRecording, activeUntil) = state;
            if (!isRecording)
                return null;

            var beepIn = AudioSettings.RecordingBeepInterval;
            if (activeUntil > prevActiveUntil)
                // UI interaction resets beep timer
                return new (activeUntil + beepIn, true);

            // Didn't beep yet
            if (_nextBeep.Value is not {} prevBeep || prevBeep.At < activeUntil)
                return new NextBeepState(activeUntil + beepIn, false);

            // Doesn't need recalculation
            if (prevBeep.At > CpuNow)
                return prevBeep;

            // Recalculate
            return new (prevBeep.At + beepIn, false);
        }
    }

    private async Task PlayBeep(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested) {
            var cNextBeep = await _nextBeep.Computed
                .When(x => x != null && x.At > CpuNow, cancellationToken)
                .ConfigureAwait(false);
            var nextBeepAt = cNextBeep.Value!.At;
            var nextBeepIn = nextBeepAt - CpuNow;
            await Task.Delay(TimeSpanExt.Max(nextBeepIn, TimeSpan.FromMilliseconds(50)), cancellationToken)
                .ConfigureAwait(false);
            if (!await IsNotCancelled(nextBeepAt).ConfigureAwait(false))
                continue;

            // Held rather than skipped, so the reminder lands as soon as the chat goes quiet.
            // ConversationStats re-measures on its own period, which is what paces this wait.
            using var holdCts = cancellationToken.CreateLinkedTokenSource();
            var whenQuiet = WhenNotActuallyConversing(holdCts.Token);
            // Cancellation only - UpdateNextBeepAt rolls the timer forward once per interval on
            // its own, and racing that would end every hold after an interval regardless.
            var whenCancelled = _nextBeep.Computed
                .When(x => x is null || (x.IsPreviousCancelled && x.At != nextBeepAt), holdCts.Token);
            await Task.WhenAny(whenQuiet, whenCancelled).ConfigureAwait(false);
            holdCts.CancelAndDisposeSilently();
            if (!whenQuiet.IsCompletedSuccessfully)
                continue;

            await TuneUI.PlayAndWait(Tune.RemindOfRecording).ConfigureAwait(false);
        }

        async Task<bool> IsNotCancelled(Moment previous)
        {
            var nextBeep = await _nextBeep.Use(cancellationToken).ConfigureAwait(false);
            return nextBeep is { IsPreviousCancelled: false } || nextBeep?.At == previous;
        }
    }

    private async Task WhenNotActuallyConversing(CancellationToken cancellationToken)
    {
        if (await GetRecordingChatId().ConfigureAwait(false) is not { } chatId)
            return;

        var cIsConversing = await Computed
            .Capture(() => IsActuallyConversing(chatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        await cIsConversing.When(x => !x, cancellationToken).ConfigureAwait(false);
    }

    private async Task WarnBeforeRecordingStop(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        // No IsActuallyConversing check here: the countdown this rides on only runs while
        // LiveStreamUI reports no activity at all, so by construction nobody is talking.
        var lastWarnedFor = (Moment?)null;
        await foreach (var change in _stopRecordingAt.Computed.Changes(cancellationToken).ConfigureAwait(false)) {
            if (change.Value is not { } stopAt) {
                lastWarnedFor = null;
                continue;
            }
            if (lastWarnedFor == stopAt)
                continue;

            var warnIn = stopAt - AudioSettings.RecordingStopWarningLeadTime - CpuNow;
            if (warnIn > TimeSpan.Zero)
                await Task.Delay(warnIn, cancellationToken).ConfigureAwait(false);
            // The countdown may have been cancelled or moved while we waited
            if (StopRecordingAt.Value != stopAt)
                continue;

            lastWarnedFor = stopAt;
            await TuneUI.PlayAndWait(Tune.RecordingWillStop).ConfigureAwait(false);
        }
    }

    private async Task RecordingTroubleshooter(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var lastState = (ChatId: (ChatId?)null, RequiresTroubleshooter: false);
        var troubleshooterCts = (CancellationTokenSource?)null;
        try {
            var cRecordingTroubleshootState = await Computed
                // ReSharper disable once PossiblyMistakenUseOfCancellationToken
                .Capture(() => GetRecordingTroubleshootState(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            // cRecordingStateBase.Changes(cancellationToken).Join(AudioRecorder.State.Changes(cancellationToken), )
            // ReSharper disable once PossiblyMistakenUseOfCancellationToken
            var changes = cRecordingTroubleshootState.Changes(cancellationToken);
            await foreach (var (state, _) in changes.ConfigureAwait(false)) {
                if (state == lastState)
                    continue; // Nothing changed - this may happen, we don't want to take any actions in this case

                DebugLog?.LogDebug($"{nameof(RecordingTroubleshooter)}: {{State}}", state);
                if (state.ChatId is null) {
                    // Recording is stopped
                    StopTroubleshooter();
                }
                else if (state.ChatId != lastState.ChatId) {
                    // Recording in new chat
                    if (state.IsTroubleshootRequired)
                        StartOrKeepTroubleshooter();
                }
                else if (state.ChatId is not null) {
                    // Recording in the same chat
                    if (!state.IsTroubleshootRequired)
                        StopTroubleshooter();
                    else if (!lastState.RequiresTroubleshooter) // And it's required now
                        StartOrKeepTroubleshooter();
                }
                lastState = state;
            }
        }
        finally {
            // Close the troubleshooter on exit no matter what
            troubleshooterCts.CancelAndDisposeSilently();
        }

        void StartOrKeepTroubleshooter() {
            if (troubleshooterCts != null)
                return;

            troubleshooterCts = new CancellationTokenSource();
            _ = ShowRecordingTroubleshooter(TimeSpan.FromSeconds(7.5), troubleshooterCts.Token);
        }

        void StopTroubleshooter() {
            if (troubleshooterCts == null)
                return;

            troubleshooterCts.CancelAndDisposeSilently();
            troubleshooterCts = null;
        }
    }

    // Helpers

    [ComputeMethod]
    protected virtual async Task<RecordingState> GetRecordingState(CancellationToken cancellationToken)
    {
        var chatId = await GetRecordingChatId().ConfigureAwait(false);
        if (chatId is null)
            return RecordingState.Idle;

        var language = await LanguageUI.GetChatLanguage(chatId, cancellationToken).ConfigureAwait(false);
        return new(chatId, language);
    }

    [ComputeMethod]
    protected virtual async Task<(ChatId? ChatId, bool IsTroubleshootRequired)> GetRecordingTroubleshootState(
        CancellationToken cancellationToken)
    {
        var chatId = await GetRecordingChatId().ConfigureAwait(false);
        var state = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var isTroubleshootRequired = chatId is not null && state is { IsRecording: false, IsConnected: true };
        // Good for debugging:
        // = !chatId.IsNone && state is { IsVoiceActive: false };
        return new ValueTuple<ChatId?, bool>(chatId, isTroubleshootRequired);
    }

    // A compute method so the wait in PlayBeep re-evaluates on any of its three inputs -
    // a fresh stats snapshot, a transcription toggle, or the own author resolving.
    [ComputeMethod]
    protected virtual async Task<bool> IsActuallyConversing(ChatId chatId, CancellationToken cancellationToken)
    {
        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var isTranscriptionOn = await LiveSessionUI.IsTranscriptionOn(chatId, cancellationToken).ConfigureAwait(false);
        var stats = await LiveStreamUI.GetConversationStats(chatId, cancellationToken).ConfigureAwait(false);
        return IsActuallyConversing(stats, ownAuthor?.Id, isTranscriptionOn, AudioSettings);
    }

    // Exists so Computed.Capture in WhenIdle gets a Computed<ListeningLinger>: capture binds
    // to the innermost compute-method call, which otherwise is IUserSettings.Get producing an
    // invariant Computed<StoredSettings?> - the cast to any settings type throws.
    [ComputeMethod]
    protected virtual Task<ListeningLinger> GetListeningLinger(CancellationToken cancellationToken)
        => UserSettingsUI.UserListeningSettings().Get(x => x.ListeningLinger, cancellationToken);

    [ComputeMethod]
    protected virtual async Task<RecordingBeepState> GetRecordingBeepState(CancellationToken cancellationToken)
    {
        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        if (recordingChatId is null)
            // if recording is not started, other properties make no sense
            return RecordingBeepState.Idle;

        var activeUntil = await UserActivityUI.ActiveUntil.Use(cancellationToken).ConfigureAwait(false); // CPU time
        return new(true, activeUntil);
    }

    private async IAsyncEnumerable<Moment?> ObserveStreamingIdleBoundaries(
        ChatId chatId,
        RecordingIdleOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // !!! This method returns a server time sequence!
        await Task.Yield();
        yield return null;

        var cHasActivity = await Computed
            .Capture(() => LiveStreamUI.HasActivity(chatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var cIsWatching = await Computed
            .Capture(() => ChatVideoUI.IsWatching(chatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var cOwnSourceKind = await Computed
            .Capture(() => ChatVideoUI.GetOwnSourceKind(chatId, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var lastActivityAt = ServerNow;
        while (!cancellationToken.IsCancellationRequested) {
            // The holds are observed by invalidation rather than polled: HasActivity is a level
            // that clears at the end of every VAD utterance, so a sampled reading anchors the
            // countdown to whichever pause the sample landed in, not to the end of the conversation.
            // HasError first: reading an errored dependency would end this method, which
            // RecordChat takes for an idle timeout. Unreadable counts as held.
            var isHeld = cHasActivity.HasError || cHasActivity.Value
                || cIsWatching.HasError || cIsWatching.Value
                || cOwnSourceKind.HasError || cOwnSourceKind.Value is not null;
            if (isHeld) {
                yield return null; // No countdown

                using var holdCts = cancellationToken.CreateLinkedTokenSource();
                await Task.WhenAny(
                    cHasActivity.WhenInvalidated(holdCts.Token),
                    cIsWatching.WhenInvalidated(holdCts.Token),
                    cOwnSourceKind.WhenInvalidated(holdCts.Token)
                    ).ConfigureAwait(false);
                holdCts.CancelAndDisposeSilently();
                // The hold spanned this whole wait, so the countdown counts from its end - keeping
                // the pre-wait timestamp would backdate the stop by the length of the last activity.
                lastActivityAt = ServerNow;
                cHasActivity = await cHasActivity.Update(cancellationToken).ConfigureAwait(false);
                cIsWatching = await cIsWatching.Update(cancellationToken).ConfigureAwait(false);
                cOwnSourceKind = await cOwnSourceKind.Update(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var (stopAt, wait, mustStop) = GetRecordingIdleStep(lastActivityAt, ServerNow, options);
            if (mustStop) {
                yield return null;
                yield break;
            }

            yield return stopAt;

            using var waitCts = cancellationToken.CreateLinkedTokenSource();
            await Task.WhenAny(
                cHasActivity.WhenInvalidated(waitCts.Token),
                cIsWatching.WhenInvalidated(waitCts.Token),
                cOwnSourceKind.WhenInvalidated(waitCts.Token),
                Task.Delay(wait, waitCts.Token)
                ).ConfigureAwait(false);
            waitCts.CancelAndDisposeSilently();
            cHasActivity = await cHasActivity.Update(cancellationToken).ConfigureAwait(false);
            cIsWatching = await cIsWatching.Update(cancellationToken).ConfigureAwait(false);
            cOwnSourceKind = await cOwnSourceKind.Update(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ShowRecordingTroubleshooter(TimeSpan delay, CancellationToken cancellationToken)
    {
        await Clocks.CpuClock.Delay(delay, cancellationToken).ConfigureAwait(false);
        if (!Hub.WhenInitialized.IsCompleted) {
            // A headless scope has no renderer, so Dispatcher would throw and WhenInitialized never completes.
            Log.LogWarning("Recording issue, but there is no renderer to show the troubleshooter in");
            return;
        }

        await Dispatcher.InvokeAsync(async () => {
            // Diagnostics run before the dialog because they decide what it may claim: the guide's
            // "grant the microphone permission" advice is misleading when permission is already granted
            Log.LogWarning("Recording issue. Capturing diagnostics state...");
            var diagnostics = await AudioRecorder.RunDiagnostics(CancellationToken.None)
                .WaitAsync(DiagnosticsTimeout, CancellationToken.None)
                .SuppressExceptions()
                .ConfigureAwait(true);
            Log.LogWarning("Recording issue. Diagnostics State = {State}", diagnostics);

            // A definitive "permission not granted" is the one case the shared permission guide fits;
            // anything else (granted, or diagnostics unavailable) stays on the recording-failure
            // troubleshooter, whose reload/reconnect advice covers a device problem that isn't a permission.
            var modalRef = diagnostics?.HasMicrophonePermission == false
                ? await ModalUI
                    .Show(new PermissionGuideModal.Model(PermissionKind.Microphone), cancellationToken)
                    .ConfigureAwait(true)
                : await ModalUI
                    .Show(new RecordingTroubleshooterModal.Model(null, true, diagnostics?.HasMicrophonePermission != true), cancellationToken)
                    .ConfigureAwait(true);

            try {
                await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                modalRef.Close();
            }
        }).ConfigureAwait(false);
    }

    // Nested types

    public sealed record RecordingState(ChatId? ChatId, Language? Language)
    {
        public static readonly RecordingState Idle = new (null, null);
    }

    public sealed record RecordingBeepState(
        bool IsRecording,
        Moment ActiveUntil) // CPU time
    {
        public static readonly RecordingBeepState Idle = new (false, Moment.MinValue);
    }

    public sealed record NextBeepState(
        Moment At, // CPU time
        bool IsPreviousCancelled);

    /// <summary>
    /// One step of the recording idle watcher: how long to wait before re-deciding,
    /// the countdown target to show meanwhile (null = no countdown yet), and whether
    /// the idle timeout has already expired.
    /// </summary>
    public sealed record RecordingIdleStep(Moment? StopAt, TimeSpan Wait, bool MustStop);

    public sealed record RecordingIdleOptions
    {
        public TimeSpan IdleTimeout { get; init; }
        public TimeSpan PreCountdownTimeout { get; init; }
        public TimeSpan CheckPeriod { get; init; }

        public RecordingIdleOptions(
            TimeSpan idleTimeout,
            TimeSpan preCountdownTimeout,
            TimeSpan checkPeriod)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(idleTimeout, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfLessThan(checkPeriod, TimeSpan.Zero);
            if (preCountdownTimeout > idleTimeout)
                throw new ArgumentOutOfRangeException(
                    nameof(preCountdownTimeout), preCountdownTimeout,
                    $"{nameof(preCountdownTimeout)} cannot be greater than {nameof(idleTimeout)}");

            IdleTimeout = idleTimeout;
            PreCountdownTimeout = preCountdownTimeout;
            CheckPeriod = checkPeriod;
        }
    }
}
