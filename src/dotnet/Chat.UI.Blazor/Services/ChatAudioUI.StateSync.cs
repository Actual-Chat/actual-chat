namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatAudioUI
{
    private static readonly TimeSpan Epsilon = TimeSpan.FromMilliseconds(50);

    protected override Task RunInternal(CancellationToken cancellationToken)
    {
        var baseChains = new AsyncChain[] {
            new(nameof(InvalidateActiveChatDependencies), InvalidateActiveChatDependencies),
            new(nameof(InvalidateHistoricalPlaybackDependencies), InvalidateHistoricalPlaybackDependencies),
            new(nameof(PushRecordingState), PushRecordingState),
            new(nameof(PushRealtimePlaybackState), PushRealtimePlaybackState),
            new(nameof(StopListeningWhenIdle), StopListeningWhenIdle),
        };
        var retryDelays = new RetryDelaySeq(100, 1000);
        return (
            from chain in baseChains
            select chain
                .RetryForever(retryDelays, Log)
                .LogBoundary(LogLevel.Debug, Log)
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

            Log.LogDebug("InvalidateActiveChatDependencies: *");
            var added = newListeningChats.Except(oldListeningChats);
            var removed = oldListeningChats.Except(newListeningChats);
            var changed = added.Concat(removed).ToList();
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
            }

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

            Log.LogDebug("InvalidateHistoricalPlaybackDependencies: *");
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
        while (true) {
            var cRecordingState = await cRecordingStateBase.When(x => !x.ChatId.IsNone, cancellationToken).ConfigureAwait(false);
            var worker = FuncWorker.New(ct => RecordInChat(cRecordingState, ct), cancellationToken);
            await worker.Run().ConfigureAwait(false);
        }
    }

    private async Task RecordInChat(Computed<RecordingState> cRecordingState, CancellationToken cancellationToken)
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

        Task whenIdle = Stl.Async.TaskExt.NeverEndingTask;
        try {
            if (!cRecordingState.IsConsistent())
                return;

            await AudioRecorder.StartRecording(chatId, cancellationToken).ConfigureAwait(false);

            var whenChanged = ForegroundTask.Run(async () => {
                await Task.Yield();
                return await cRecordingState
                    .When(x => x.ChatId != chatId || x.Language != language, cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken);
            whenIdle = ForegroundTask.Run(async () => {
                await Task.Yield();
                var options = new IdleAudioWatchOptions(AudioSettings.IdleRecordingTimeout,
                    AudioSettings.IdleRecordingTimeoutBeforeCountdown,
                    AudioSettings.IdleRecordingCheckInterval);
                await foreach (var willStopAt in WatchIdleAudioBoundaries(chatId, options, cancellationToken).ConfigureAwait(false))
                    _stopRecordingAt.Value = willStopAt;
            }, cancellationToken);
            await Task.WhenAny(whenChanged, whenIdle).ConfigureAwait(false);
        }
        finally {
            if (whenIdle.IsCompleted)
                await SetRecordingChatId(ChatId.None).ConfigureAwait(false);

            // Stopping the recording
            while (!await AudioRecorder.StopRecording(CancellationToken.None).ConfigureAwait(false))
                await Clocks.CpuClock.Delay(1000, CancellationToken.None).ConfigureAwait(false);
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

        while (!cancellationToken.IsCancellationRequested) {
            var expectedPlaybackState = cExpectedPlaybackState.Value;
            var actualPlaybackState = cActualPlaybackState.Value;
            if (actualPlaybackState is null or RealtimePlaybackState) {
                if (!ReferenceEquals(actualPlaybackState, expectedPlaybackState)) {
                    if (expectedPlaybackState == null) {
                        Log.LogDebug(nameof(PushRealtimePlaybackState) + ": stopping playback");
                        ChatPlayers.StopPlayback();
                    }
                    else {
                        if (actualPlaybackState == null && !InteractiveUI.IsInteractive.Value) {
                            var isConfirmed = false;
                            var chats = await expectedPlaybackState.ChatIds
                                .Select(id => Chats.Get(Session, id, cancellationToken))
                                .Collect()
                                .ConfigureAwait(false);
                            var chatTitles = chats
                                .SkipNullItems()
                                .Select(c => $"\"{c.Title}\"")
                                .ToCommaPhrase();
                            if (!chatTitles.IsNullOrEmpty()) { // It's empty if there are no chats
                                var operation = "listening in " + chatTitles;
                                isConfirmed = await InteractiveUI.Demand(operation, cancellationToken).ConfigureAwait(false);
                            }
                            if (!isConfirmed) {
                                await ClearListeningState().ConfigureAwait(false);
                                // An extra pause to make sure we don't apply changes too frequently
                                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                        }
                        Log.LogDebug(nameof(PushRealtimePlaybackState) + ": starting playback");
                        ChatPlayers.StartRealtimePlayback(expectedPlaybackState);
                    }

                    // An extra pause to make sure we don't apply changes too frequently
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }

            Log.LogDebug("PushRealtimePlaybackState: waiting for changes");
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
        try {
            await foreach (var _ in WatchIdleAudioBoundaries(chatId, options, cancellationToken).ConfigureAwait(false)) { }
        }
        catch (Exception exc) when (!cancellationToken.IsCancellationRequested) {
            Log.LogError(exc, "StopIdleListening failed");
            throw;
        }
        finally {
            await SetListeningState(chatId, false).ConfigureAwait(false);
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
        await Task.Delay(options.CountdownInterval, cancellationToken)
            .ConfigureAwait(false);

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
                await Task.Delay(TimeSpanExt.Min(timeToStop, options.CheckInterval),
                        cancellationToken)
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
