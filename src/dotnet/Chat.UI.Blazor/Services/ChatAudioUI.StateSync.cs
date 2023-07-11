using ActualChat.Audio.UI.Blazor.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatAudioUI
{
    private static readonly TimeSpan Epsilon = TimeSpan.FromMilliseconds(50);
    private static readonly int MaxStopRecordingTryCount = 3;

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        // Initialize audio
        var loadingUI = Services.GetRequiredService<LoadingUI>();
        _ = loadingUI.WhenRendered
            .ContinueWith(
                _ => {
                    if (!cancellationToken.IsCancellationRequested)
                        Services.GetRequiredService<AudioInitializer>();
                },
                TaskScheduler.Default);

        var baseChains = new AsyncChain[] {
            new(nameof(InvalidateActiveChatDependencies), InvalidateActiveChatDependencies),
            new(nameof(InvalidateHistoricalPlaybackDependencies), InvalidateHistoricalPlaybackDependencies),
            new(nameof(PushRecordingState), PushRecordingState),
            new(nameof(PushRealtimePlaybackState), PushRealtimePlaybackState),
            new(nameof(StopListeningWhenIdle), StopListeningWhenIdle),
            new(nameof(StopRecordingOnAwake), StopRecordingOnAwake),
            new(nameof(ReconnectAudioHubOnRpcConnect), ReconnectAudioHubOnRpcConnect),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .RetryForever(retryDelays, DebugLog)
                .Log(LogLevel.Debug, DebugLog)
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

            var oldAudioOn = oldRecordingChat != default || oldListeningChats.Any();
            var newAudioOn = newRecordingChat != default || newListeningChats.Any();

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
                _audioStoppedAt.Value = Now;

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
            .Capture(() => GetRecordingState(cancellationToken))
            .ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested) {
            var cRecordingState = await cRecordingStateBase.When(x => !x.ChatId.IsNone, cancellationToken).ConfigureAwait(false);
            await BackgroundTask.Run(
                () => RecordChat(cRecordingState, cancellationToken),
                Log, $"{nameof(RecordChat)} failed",
                cancellationToken
                ).SilentAwait(false);
        }
    }

    private async Task RecordChat(Computed<RecordingState> cRecordingState, CancellationToken cancellationToken)
    {
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
        try {
            var relatedChatEntry = await ChatEditorUI.RelatedChatEntry.Use(cancellationToken);
            var repliedChatEntryId = relatedChatEntry is { Kind: RelatedEntryKind.Reply }
                ? relatedChatEntry.Value.Id
                : ChatEntryId.None;
            await ChatEditorUI.HideRelatedEntry().ConfigureAwait(false);

            await AudioRecorder.StartRecording(chatId, repliedChatEntryId, cancellationToken).ConfigureAwait(false);

            var whenChanged = ForegroundTask.Run(async () => {
                return await cRecordingState
                    .When(x => x.ChatId != chatId || x.Language != language, cts.Token)
                    .ConfigureAwait(false);
            }, cancellationToken);
            whenIdle = ForegroundTask.Run(async () => {
                var options = new IdleAudioWatchOptions(AudioSettings.IdleRecordingTimeout,
                    AudioSettings.IdleRecordingTimeoutBeforeCountdown,
                    AudioSettings.IdleRecordingCheckInterval);
                await foreach (var willStopAt in WatchIdleAudioBoundaries(chatId, options, cts.Token).ConfigureAwait(false))
                    _stopRecordingAt.Value = willStopAt;
            }, cts.Token);
            await Task.WhenAny(whenChanged, whenIdle).ConfigureAwait(false);
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
            .Capture(GetExpectedRealtimePlaybackState)
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

        var options = new IdleAudioWatchOptions(AudioSettings.IdleListeningTimeout,
            AudioSettings.IdleListeningTimeout - AudioSettings.IdleListeningCheckInterval + TimeSpan.FromSeconds(1),
            AudioSettings.IdleListeningCheckInterval);
        var cListeningChatIds = await Computed.Capture(GetListeningChatIds).ConfigureAwait(false);
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
                var watcher = FuncWorker.Start(ct => StopChatListeningWhenIdle(chatId, options, ct), cancellationToken);
                monitors.Add(chatId, watcher);
            }
        }
    }

    private async Task StopChatListeningWhenIdle(
        ChatId chatId, IdleAudioWatchOptions options, CancellationToken cancellationToken)
    {
        var stopListening = true;
        try {
            await foreach (var _ in WatchIdleAudioBoundaries(chatId, options, cancellationToken).ConfigureAwait(false)) { }
        }
        catch (Exception e) {
            if (cancellationToken.IsCancellationRequested) {
                // Cancellation happens when user click on restart button in MAUI app.
                // We should avoid updating recent chats when this happens.
                stopListening = false;
            }
            else {
                Log.LogError(e, "StopIdleListening failed");
                throw;
            }
        }
        finally {
            if (stopListening)
                await SetListeningState(chatId, false).ConfigureAwait(false);
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

    private async Task ReconnectAudioHubOnRpcConnect(CancellationToken cancellationToken)
    {
        await foreach (var _ in RpcClientConnectionFactory.ReconnectCount.Changes(cancellationToken).ConfigureAwait(false))
            await AudioRecorder.Reconnect(cancellationToken).ConfigureAwait(false);
    }

    // Helpers

    [ComputeMethod]
    protected virtual async Task<RecordingState> GetRecordingState(CancellationToken cancellationToken)
    {
        var chatId = await GetRecordingChatId().ConfigureAwait(false);
        var language = await LanguageUI.GetChatLanguage(chatId, cancellationToken).ConfigureAwait(false);
        return new(chatId, chatId.IsNone ? Language.None : language);
    }

    private async IAsyncEnumerable<Moment?> WatchIdleAudioBoundaries(
        ChatId chatId,
        IdleAudioWatchOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        var clock = Clocks.SystemClock;
        var lastEntryAt = clock.Now; // Recording just started
        yield return null;

        // No need to check last entry since monitoring has just started
        await Task.Delay(options.CountdownInterval, cancellationToken).ConfigureAwait(false);

        ChatEntry? prevLastEntry = null;
        while (!cancellationToken.IsCancellationRequested) {
            var lastEntry = await GetLastTranscribedEntry(chatId,
                    prevLastEntry?.LocalId,
                    lastEntryAt,
                    cancellationToken)
                .ConfigureAwait(false);
            if (lastEntry != null) {
                // When entry is finalized and EndsAt is lower than we expect we keep previous lastEntryAt
                lastEntryAt = Moment.Max(lastEntryAt, GetEndsAt(lastEntry));
            }

            var willBeIdleAt = lastEntryAt + options.IdleInterval;
            var timeToStop = (willBeIdleAt - clock.Now).Positive();
            var timeToCountdown =
                (lastEntryAt + options.CountdownInterval - clock.Now).Positive();
            if (timeToStop <= Epsilon) {
                // Notify is idle and stop counting down
                yield return null;
                yield break;
            }
            if (timeToCountdown <= Epsilon) {
                // continue counting down
                yield return willBeIdleAt;
                await Task
                    .Delay(TimeSpanExt.Min(timeToStop, options.CheckInterval), cancellationToken)
                    .ConfigureAwait(false);
            }
            else {
                // reset countdown since there were new messages
                yield return null;
                await Task.Delay(timeToCountdown, cancellationToken).ConfigureAwait(false);
            }
            prevLastEntry = lastEntry;
        }
    }

    private async Task<ChatEntry?> GetLastTranscribedEntry(
        ChatId chatId,
        long? startFrom,
        Moment minEndsAt,
        CancellationToken cancellationToken)
    {
        var idRange = await Chats
            .GetIdRange(Session, chatId, ChatEntryKind.Text, cancellationToken)
            .ConfigureAwait(false);
        if (startFrom != null)
            idRange = (startFrom.Value, idRange.End);
        var reader = Chats.NewEntryReader(Session, chatId, ChatEntryKind.Text);
        return await reader.GetLastWhile(idRange,
            x => x.HasAudioEntry || x.IsStreaming,
            x => GetEndsAt(x.ChatEntry) >= minEndsAt && x.SkippedCount < 100,
            cancellationToken);
    }

    private Moment GetEndsAt(ChatEntry lastEntry)
        => lastEntry.IsStreaming
            ? Clocks.SystemClock.Now
            : lastEntry.EndsAt ?? lastEntry.ContentEndsAt ?? lastEntry.BeginsAt;

    // Nested types

    [StructLayout(LayoutKind.Auto)]
    public readonly record struct RecordingState(ChatId ChatId, Language Language)
    {
        public static readonly RecordingState None = default;
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly record struct IdleAudioWatchOptions
    {
        public TimeSpan IdleInterval { get; init; }
        public TimeSpan CountdownInterval { get; init; }
        public TimeSpan CheckInterval { get; init; }

        public IdleAudioWatchOptions(
            TimeSpan idleInterval,
            TimeSpan countdownInterval,
            TimeSpan checkInterval)
        {
            if (idleInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(idleInterval));
            if (checkInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(checkInterval));
            if (countdownInterval > idleInterval)
                throw new ArgumentOutOfRangeException(
                    nameof(countdownInterval), countdownInterval,
                    $"{nameof(countdownInterval)} cannot be greater than {nameof(idleInterval)}");

            IdleInterval = idleInterval;
            CountdownInterval = countdownInterval;
            CheckInterval = checkInterval;
        }
    }
}
