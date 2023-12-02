using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Rpc;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatAudioUI
{
    private static readonly TimeSpan Epsilon = TimeSpan.FromMilliseconds(50);
    private static readonly int MaxStopRecordingTryCount = 3;

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChainExt.From(InvalidateActiveChatDependencies),
            AsyncChainExt.From(InvalidateHistoricalPlaybackDependencies),
            AsyncChainExt.From(PushRecordingState),
            AsyncChainExt.From(PushRealtimePlaybackState),
            AsyncChainExt.From(StopHistoricalPlaybackWhenRecordingStarts),
            AsyncChainExt.From(StopListeningWhenIdle),
            AsyncChainExt.From(StopRecordingOnAwake),
            AsyncChainExt.From(ReconnectOnRpcReconnect),
            AsyncChainExt.From(UpdateNextBeepAt),
            AsyncChainExt.From(PlayBeep),
            AsyncChainExt.From(RecordingTroubleshooter),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    // Private methods

    private async Task InvalidateActiveChatDependencies(CancellationToken cancellationToken)
    {
        var oldRecordingChat = default(ActiveChat);
        var oldListeningChats = new HashSet<ActiveChat>();
        var changes = ActiveChatsUI.ActiveChats.Changes(cancellationToken);
        await foreach (var cActiveContacts in changes.ConfigureAwait(false)) {
            var activeChats = cActiveContacts.Value;
            var newRecordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
            var newListeningChats = activeChats.Where(c => c.IsListening).ToHashSet();

            DebugLog?.LogDebug("InvalidateActiveChatDependencies: *");
            var added = newListeningChats.Except(oldListeningChats);
            var removed = oldListeningChats.Except(newListeningChats);
            var changed = added.Concat(removed).ToList();

            var oldAudioOn = oldRecordingChat != default || oldListeningChats.Count != 0;
            var newAudioOn = newRecordingChat != default || newListeningChats.Count != 0;

            using (Computed.Invalidate()) {
                if (newRecordingChat != oldRecordingChat) {
                    _ = GetRecordingChatId();
                    _ = GetState(oldRecordingChat.ChatId);
                    _ = GetState(newRecordingChat.ChatId);
                }
                if (changed.Count > 0) {
                    _ = GetListeningChatIds();
                    foreach (var c in changed)
                        _ = GetState(c.ChatId);
                }
                if (oldAudioOn != newAudioOn)
                    _ = IsAudioOn();
            }
            if ((oldRecordingChat != default && newRecordingChat == default) || (newListeningChats.Count == 0 && oldListeningChats.Count > 0))
                _audioStoppedAt.Value = CpuNow;

            oldRecordingChat = newRecordingChat;
            oldListeningChats = newListeningChats;
        }
    }

    private async Task InvalidateHistoricalPlaybackDependencies(CancellationToken cancellationToken)
    {
        var oldChatId = ChatId.None;
        var changes = ChatPlayers.PlaybackState.Changes(cancellationToken);
        await foreach (var cPlaybackState in changes.ConfigureAwait(false)) {
            var newChatId = (cPlaybackState.Value as HistoricalPlaybackState)?.ChatId ?? default;
            if (newChatId == oldChatId)
                continue;

            DebugLog?.LogDebug("InvalidateHistoricalPlaybackDependencies: *");
            using (Computed.Invalidate()) {
                _ = GetState(oldChatId);
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
        while (!cancellationToken.IsCancellationRequested) {
            var cRecordingState = await cRecordingStateBase
                .When(x => !x.ChatId.IsNone, FixedDelayer.ZeroUnsafe, cancellationToken)
                .ConfigureAwait(false);
            await BackgroundTask.Run(
                () => RecordChat(cRecordingState, cancellationToken),
                Log, $"{nameof(RecordChat)} failed",
                cancellationToken
                ).SilentAwait(false);
        }
    }

    // TODO: get rid of this workaround when playback state is refactored and put this logic to PushPlaybackState
    private async Task StopHistoricalPlaybackWhenRecordingStarts(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var cRecordingState = await Computed
            .Capture(() => GetRecordingState(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            cRecordingState = await cRecordingState
                .When(x => !x.ChatId.IsNone, FixedDelayer.ZeroUnsafe, cancellationToken)
                .ConfigureAwait(false);
            var chatId = cRecordingState.Value.ChatId;
            if (ChatPlayers.PlaybackState.Value is HistoricalPlaybackState historicalPlaybackState && historicalPlaybackState.ChatId != chatId)
                ChatPlayers.StopPlayback();
            cRecordingState = await cRecordingState.When(x => x.ChatId.IsNone || x.ChatId != chatId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RecordChat(Computed<RecordingState> cRecordingState, CancellationToken cancellationToken)
    {
        AudioInitializer.Start();
        var serverClock = Clocks.ServerClock;
        var cpuClock = Clocks.CpuClock;
        var (chatId, language) = cRecordingState.Value;
        if (!InteractiveUI.IsInteractive.Value) {
            var isConfirmed = false;
            var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
            if (chat != null) {
                var operation = $"recording in \"{chat.Title}\"";
                isConfirmed = await InteractiveUI.Demand(operation, cancellationToken).ConfigureAwait(false);
            }
            if (!isConfirmed) {
                await SetRecordingChatId(ChatId.None).ConfigureAwait(false);
                return;
            }
        }

        if (!cRecordingState.IsConsistent())
            return;

        Task? whenIdle = null;
        var cts = cancellationToken.CreateLinkedTokenSource();
        var ctsToken = cts.Token;
        try {
            var relatedChatEntry = await ChatEditorUI.RelatedChatEntry.Use(cancellationToken).ConfigureAwait(false);
            var repliedChatEntryId = relatedChatEntry is { Kind: RelatedEntryKind.Reply }
                ? relatedChatEntry.Value.Id
                : ChatEntryId.None;
            await ChatEditorUI.HideRelatedEntry().ConfigureAwait(false);

            await AudioRecorder.StartRecording(chatId, repliedChatEntryId, cancellationToken).ConfigureAwait(false);
            var whenStopped = ForegroundTask.Run(
                async () => await cRecordingState
                    .When(x => x.ChatId != chatId || x.Language != language, ctsToken)
                    .ConfigureAwait(false),
                cancellationToken);
            whenIdle = ForegroundTask.Run(async () => {
                var options = new RecordingIdleOptions(
                    AudioSettings.IdleRecordingTimeout,
                    AudioSettings.IdleRecordingPreCountdownTimeout,
                    AudioSettings.IdleRecordingCheckPeriod);
                var streamingIdleBoundaries = ObserveStreamingIdleBoundaries(chatId, options, ctsToken);
                await foreach (var serverStopAt in streamingIdleBoundaries.ConfigureAwait(false))
                    _stopRecordingAt.Value = serverStopAt.Convert(serverClock, cpuClock);
            }, ctsToken);
            await Task.WhenAny(whenStopped, whenIdle).ConfigureAwait(false);
            // No need to await for the result of WhenAny: we're stopping anyway
        }
        finally {
            cts.CancelAndDisposeSilently();
            _stopRecordingAt.Value = null;
            if (whenIdle is { IsCompleted: true })
                await SetRecordingChatId(ChatId.None).ConfigureAwait(false);

            // Stopping the recording
            for (var tryIndex = 0;; tryIndex++) {
                if (await AudioRecorder.StopRecording(CancellationToken.None).ConfigureAwait(false))
                    break;
                if (tryIndex >= MaxStopRecordingTryCount) {
                    Log.LogError(nameof(RecordChat) + ": couldn't stop recording in {TryCount} tries", MaxStopRecordingTryCount);
                    break;
                }

                await Clocks.CpuClock.Delay(1000, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task PushRealtimePlaybackState(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var dCancellationTask = cancellationToken.ToTask();
        var cancellationTask = dCancellationTask.Resource;

        var playbackState = ChatPlayers.PlaybackState;
        var cExpectedPlaybackState = await Computed
            .Capture(GetExpectedRealtimePlaybackState, cancellationToken)
            .ConfigureAwait(false);
        var cActualPlaybackState = playbackState.Computed;

        var syncedPlaybackState = (RealtimePlaybackState?)null;
        while (!cancellationToken.IsCancellationRequested) {
            var expectedPlaybackState = cExpectedPlaybackState.Value;
            var actualPlaybackState = cActualPlaybackState.Value;
            if (actualPlaybackState is HistoricalPlaybackState) {
                // Historical playback "overrides" realtime playback
                syncedPlaybackState = null; // But we must re-sync once it completes
                goto skip;
            }

            if (ReferenceEquals(expectedPlaybackState, syncedPlaybackState))
                goto skip; // Already in sync
            if (ReferenceEquals(expectedPlaybackState, actualPlaybackState)) {
                // It's _somehow_ in sync - normally we shouldn't land here
                syncedPlaybackState = expectedPlaybackState;
                goto skip;
            }

            syncedPlaybackState = expectedPlaybackState;
            if (expectedPlaybackState != null) {
                DebugLog?.LogDebug(nameof(PushRealtimePlaybackState) + ": starting playback");
                ChatPlayers.StartRealtimePlayback(expectedPlaybackState);
            }
            else {
                DebugLog?.LogDebug(nameof(PushRealtimePlaybackState) + ": stopping playback");
                ChatPlayers.StopPlayback();
            }

            skip:
            DebugLog?.LogDebug("PushRealtimePlaybackState: waiting for changes");
            await Task.WhenAny(
                cActualPlaybackState.WhenInvalidated(cancellationToken),
                cExpectedPlaybackState.WhenInvalidated(cancellationToken),
                cancellationTask
            ).ConfigureAwait(false);
            cExpectedPlaybackState = await cExpectedPlaybackState.Update(cancellationToken).ConfigureAwait(false);
            cActualPlaybackState = playbackState.Computed;
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private async Task StopListeningWhenIdle(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var options = new RecordingIdleOptions(
            AudioSettings.IdleListeningTimeout,
            AudioSettings.IdleListeningPreCountdownTimeout,
            AudioSettings.IdleListeningCheckPeriod);
        var cListeningChatIds = await Computed
            .Capture(GetListeningChatIds, cancellationToken)
            .ConfigureAwait(false);
        var monitors = new Dictionary<ChatId, FuncWorker>();
        await foreach (var change in cListeningChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            var listeningChatIds = change.Value;
            var toStop = monitors.Keys.Except(listeningChatIds).ToList();
            var toStart = listeningChatIds.Except(monitors.Keys).ToList();

            var stopTasks = new List<Task>();
            foreach (var chatId in toStop) {
                if (monitors.Remove(chatId, out var monitor))
                    stopTasks.Add(monitor.Stop());
            }
            await stopTasks.Collect().ConfigureAwait(false);

            foreach (var chatId in toStart) {
                var watcher = FuncWorker.Start(ct => StopListeningWhenIdle(chatId, options, ct), cancellationToken);
                monitors.Add(chatId, watcher);
            }
        }
    }

    private async Task StopListeningWhenIdle(
        ChatId chatId, RecordingIdleOptions options, CancellationToken cancellationToken)
    {
        var mustStop = true;
        try {
            while (!cancellationToken.IsCancellationRequested) {
                await WhenRecordingChatIdBecomes(x => x != chatId, cancellationToken).ConfigureAwait(false);
                var cts = cancellationToken.CreateLinkedTokenSource();
                try {
                    var whenRecording = WhenRecordingChatIdBecomes(x => x == chatId, cts.Token);
                    var whenIdle = WhenIdle(cts.Token);
                    await Task.WhenAny(whenRecording, whenIdle).ConfigureAwait(false);
                    if (whenIdle.IsCompletedSuccessfully)
                        break;
                }
                finally {
                    cts.CancelAndDisposeSilently();
                }
            }
        }
        catch (OperationCanceledException) {
            mustStop = false;
        }
        catch (Exception e) {
            Log.LogError(e, "StopListeningWhenIdle failed");
            throw;
        }
        finally {
            if (mustStop)
                await SetListeningState(chatId, false).ConfigureAwait(false);
        }

        async Task WhenRecordingChatIdBecomes(Func<ChatId, bool> predicate, CancellationToken ct) {
            var cRecordingChatId = await Computed
                .Capture(GetRecordingChatId, cancellationToken)
                .ConfigureAwait(false);
            await foreach (var (recordingChatId, _) in cRecordingChatId.Changes(ct).ConfigureAwait(false))
                if (predicate.Invoke(recordingChatId))
                    return;
        }

        async Task WhenIdle(CancellationToken ct) {
            var idleBoundaries = ObserveStreamingIdleBoundaries(chatId, options, ct);
            await foreach (var _ in idleBoundaries.ConfigureAwait(false)) { }
        }
    }

    private async Task StopRecordingOnAwake(CancellationToken cancellationToken)
    {
        var totalSleepDuration = DeviceAwakeUI.TotalSleepDuration.Value;
        await DeviceAwakeUI.TotalSleepDuration
            .When(x => x != totalSleepDuration, cancellationToken)
            .ConfigureAwait(false);

        await SetRecordingChatId(ChatId.None).ConfigureAwait(false);
    }

    private async Task ReconnectOnRpcReconnect(CancellationToken cancellationToken)
    {
        var rpcDependentReconnectDelayer = ChatHub.GetService<RpcDependentReconnectDelayer>();
        if (rpcDependentReconnectDelayer == null)
            return;

        while (true) {
            await rpcDependentReconnectDelayer.WhenDisconnected(cancellationToken).ConfigureAwait(false);
            await rpcDependentReconnectDelayer.WhenConnected(cancellationToken).ConfigureAwait(false);
            // AudioRecorder.Reconnect does nothing if the connection is already there
            await AudioRecorder.Reconnect(cancellationToken).ConfigureAwait(false);
        }
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
            var (isRecording, activeUntil, isCountingDown) = state;
            if (!isRecording)
                return null;

            var beepIn = isCountingDown
                ? AudioSettings.RecordingAggressiveBeepInterval
                : AudioSettings.RecordingBeepInterval;

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
            var cNextBeep = await _nextBeep.When(x => x != null && x.At > CpuNow, cancellationToken).ConfigureAwait(false);
            var nextBeepAt = cNextBeep.Value!.At;
            var nextBeepIn = nextBeepAt - CpuNow;
            await Task.Delay(TimeSpanExt.Max(nextBeepIn, TimeSpan.FromMilliseconds(50)), cancellationToken).ConfigureAwait(false);
            if (await IsNotCancelled(nextBeepAt).ConfigureAwait(false))
                await TuneUI.PlayAndWait(Tune.RemindOfRecording).ConfigureAwait(false);
        }

        async Task<bool> IsNotCancelled(Moment previous)
        {
            var nextBeep = await _nextBeep.Use(cancellationToken).ConfigureAwait(false);
            return nextBeep is { IsPreviousCancelled: false } || nextBeep?.At == previous;
        }
    }

    private async Task RecordingTroubleshooter(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var lastState = (ChatId: ChatId.None, RequiresTroubleshooter: false);
        var troubleshooterCts = (CancellationTokenSource?)null;
        try {
            var cRecordingTroubleshootState = await Computed
                .Capture(() => GetRecordingTroubleshootState(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            // cRecordingStateBase.Changes(cancellationToken).Join(AudioRecorder.State.Changes(cancellationToken), )
            var changes = cRecordingTroubleshootState.Changes(cancellationToken);
            await foreach (var (state, _) in changes.ConfigureAwait(false)) {
                if (state == lastState)
                    continue; // Nothing changed - this may happen, we don't want to take any actions in this case

                DebugLog?.LogDebug($"{nameof(RecordingTroubleshooter)}: {{State}}", state);
                if (state.ChatId.IsNone) {
                    // Recording is stopped
                    StopTroubleshooter();
                }
                else if (state.ChatId != lastState.ChatId) {
                    // Recording in new chat
                    if (state.IsTroubleshootRequired)
                        StartOrKeepTroubleshooter();
                }
                else if (!state.ChatId.IsNone) {
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
        var language = await LanguageUI.GetChatLanguage(chatId, cancellationToken).ConfigureAwait(false);
        return new(chatId, chatId.IsNone ? Language.None : language);
    }

    [ComputeMethod]
    protected virtual async Task<(ChatId ChatId, bool IsTroubleshootRequired)> GetRecordingTroubleshootState(CancellationToken cancellationToken)
    {
        var chatId = await GetRecordingChatId().ConfigureAwait(false);
        var state = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var isTroubleshootRequired = !chatId.IsNone && state is { IsRecording: false, IsConnected: true };
        // Good for debugging:
        // = !chatId.IsNone && state is { IsVoiceActive: false };
        return new ValueTuple<ChatId, bool>(chatId, isTroubleshootRequired);
    }

    [ComputeMethod]
    protected virtual async Task<RecordingBeepState> GetRecordingBeepState(CancellationToken cancellationToken)
    {
        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        if (recordingChatId.IsNone)
            // if recording is not started, other properties make no sense
            return new (false, Moment.MinValue, false);

        var activeUntil = await UserActivityUI.ActiveUntil.Use(cancellationToken).ConfigureAwait(false); // CPU time
        var recordingStopsAt = await StopRecordingAt.Use(cancellationToken).ConfigureAwait(false); // CPU time
        var isRecording = !recordingChatId.IsNone;
        return new(isRecording, activeUntil, recordingStopsAt != null);
    }

    // !!! This method returns server time sequence!
    private async IAsyncEnumerable<Moment?> ObserveStreamingIdleBoundaries(
        ChatId chatId,
        RecordingIdleOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        var lastTranscribedAt = Clocks.ServerClock.Now;
        yield return null;

        // We just started, so it's ok to await for the countdown interval first
        await Task.Delay(options.PreCountdownTimeout, cancellationToken).ConfigureAwait(false);

        using var streamingActivity = await ChatActivity.GetStreamingActivity(chatId, cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            lastTranscribedAt = Moment.Max(lastTranscribedAt, streamingActivity.LastTranscribedAt.Value ?? ServerNow);
            var idleAt = lastTranscribedAt + options.IdleTimeout;
            var idleDelay = (idleAt - ServerNow).Positive();
            if (idleDelay <= Epsilon) {
                // We must stop right now
                yield return null;
                yield break;
            }

            var countdownAt = lastTranscribedAt + options.PreCountdownTimeout;
            var countdownDelay = (countdownAt - ServerNow).Positive();
            if (countdownDelay <= Epsilon) {
                // Start the countdown
                yield return idleAt;
                await Task
                    .Delay(TimeSpanExt.Min(idleDelay, options.CheckPeriod), cancellationToken)
                    .ConfigureAwait(false);
            }
            else {
                // Too early to countdown
                yield return null;
                await Task.Delay(countdownDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task ShowRecordingTroubleshooter(TimeSpan delay, CancellationToken cancellationToken) {
        await Clocks.CpuClock.Delay(delay, cancellationToken).ConfigureAwait(false);
        await Dispatcher.InvokeAsync(async () => {
            var model = new RecordingTroubleshooterModal.Model(null, true);
            var modalRef = await ModalUI.Show(model, cancellationToken).ConfigureAwait(true);

            Log.LogWarning("Recording issue. Capturing diagnostics state...");
            var diagnostics = await AudioRecorder.RunDiagnostics(CancellationToken.None).ConfigureAwait(true);
            Log.LogWarning("Recording issue. Diagnostics State = {State}", diagnostics);

            try {
                await modalRef.WhenClosed.WaitAsync(cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                modalRef.Close();
            }
        }).ConfigureAwait(false);
    }

    // Nested types

    [StructLayout(LayoutKind.Auto)]
    public readonly record struct RecordingState(ChatId ChatId, Language Language)
    {
        public static readonly RecordingState None = default;
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly record struct RecordingBeepState(
        bool IsRecording,
        Moment ActiveUntil, // CPU time
        bool IsCountingDown);

    public sealed record NextBeepState(
        Moment At, // CPU time
        bool IsPreviousCancelled);

    [StructLayout(LayoutKind.Auto)]
    public readonly record struct RecordingIdleOptions
    {
        public TimeSpan IdleTimeout { get; init; }
        public TimeSpan PreCountdownTimeout { get; init; }
        public TimeSpan CheckPeriod { get; init; }

        public RecordingIdleOptions(
            TimeSpan idleTimeout,
            TimeSpan preCountdownTimeout,
            TimeSpan checkPeriod)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(idleTimeout, TimeSpan.Zero, nameof(idleTimeout));
            ArgumentOutOfRangeException.ThrowIfLessThan(checkPeriod, TimeSpan.Zero, nameof(checkPeriod));
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
