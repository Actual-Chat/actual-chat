using System.Diagnostics.CodeAnalysis;
using ActualChat.Audio.UI.Blazor.Components;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatAudioUI
{
    private static readonly TimeSpan Eps = TimeSpan.FromMilliseconds(50);

    protected override Task RunInternal(CancellationToken cancellationToken)
    {
        var baseChains = new AsyncChain[] {
            new(nameof(InvalidateActiveChatDependencies), InvalidateActiveChatDependencies),
            new(nameof(InvalidateHistoricalPlaybackDependencies), InvalidateHistoricalPlaybackDependencies),
            new(nameof(PushRealtimePlaybackState), PushRealtimePlaybackState),
            new(nameof(SyncRecordingState), SyncRecordingState),
            new(nameof(StopRecordingWhenIdle), StopRecordingWhenIdle),
            new(nameof(StopListeningWhenIdle), StopListeningWhenIdle),
        };
        var retryDelays = new RetryDelaySeq(100, 1000);
        return (
            from chain in baseChains
            select chain
                .RetryForever(retryDelays, Log)
            // .LogBoundary(LogLevel.Debug, Log)
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

    private async Task StopRecordingWhenIdle(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var options = new IdleMonitoringOptions(AudioSettings.IdleRecordingTimeout,
            AudioSettings.IdleRecordingTimeoutBeforeCountdown,
            AudioSettings.IdleRecordingCheckInterval);

        var cChatId = await Computed.Capture(GetRecordingChatId).ConfigureAwait(false);
        Operation? monitoring = null;
        await foreach (var change in cChatId.Changes(cancellationToken).ConfigureAwait(false)) {
            var chatId = change.Value;
            if (monitoring != null)
                await monitoring.Stop().ConfigureAwait(false);
            if (!chatId.IsNone) {
                monitoring = new Operation(ct => MonitorIdleRecording(chatId, ct), cancellationToken);
                monitoring.Start();
            }
        }

        async Task MonitorIdleRecording(ChatId chatId, CancellationToken cancellationToken1)
        {
            try {
                await foreach (var willStopAt in MonitorIdleAudio(chatId, options, cancellationToken1)
                                   .ConfigureAwait(false))
                    _stopRecordingAt.Value = willStopAt;
                await SetRecordingChatId(default).ConfigureAwait(false);
            }
            finally {
                _stopRecordingAt.Value = null;
            }
        }
    }

    private async Task StopListeningWhenIdle(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var options = new IdleMonitoringOptions(AudioSettings.IdleListeningTimeout,
            AudioSettings.IdleListeningTimeout - AudioSettings.IdleListeningCheckInterval + TimeSpan.FromSeconds(1),
            AudioSettings.IdleListeningCheckInterval);
        var cListeningChatIds = await Computed.Capture(GetListeningChatIds).ConfigureAwait(false);
        var running = new Dictionary<ChatId, Operation>();
        await foreach (var change in cListeningChatIds.Changes(cancellationToken).ConfigureAwait(false)) {
            var listeningChatIds = change.Value;
            var toStop = running.Keys.Except(listeningChatIds).ToList();
            var toStart = listeningChatIds.Except(running.Keys).ToList();

            var stoppedTasks = new List<Task>();
            foreach (var chatId in toStop) {
                running.Remove(chatId, out var monitoring);
                stoppedTasks.Add(monitoring!.Stop());
            }
            await stoppedTasks.Collect().ConfigureAwait(false);

            foreach (var chatId in toStart) {
                var monitoring = new Operation(ct => MonitorIdleListening(chatId, ct), cancellationToken);
                running.Add(chatId, monitoring);
                monitoring.Start();
            }
        }

        async Task MonitorIdleListening(ChatId chatId, CancellationToken cancellationToken1)
        {
            await foreach (var _ in MonitorIdleAudio(chatId, options, cancellationToken1).ConfigureAwait(false))
            { }
            await SetListeningState(chatId, false).ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<Moment?> MonitorIdleAudio(
        ChatId chatId,
        IdleMonitoringOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        var clock = Clocks.SystemClock;
        var lastEntryAt = clock.Now; // recording just started
        yield return null;
        // no need to check last entry since monitoring has just started
        await Task.Delay(options.IdleTimeoutBeforeCountdown, cancellationToken)
            .ConfigureAwait(false);

        ChatEntry? prevLastEntry = null;
        while (!cancellationToken.IsCancellationRequested) {
            var lastEntry = await GetLastTranscribedEntry(chatId,
                    prevLastEntry?.LocalId,
                    lastEntryAt,
                    cancellationToken)
                .ConfigureAwait(false);
            if (lastEntry != null)
                // when entry is finalized and EndsAt is lower than we expect we keep previous lastEntryAt
                lastEntryAt = Moment.Max(lastEntryAt, GetEndsAt(lastEntry));
            var willBeIdleAt = lastEntryAt + options.IdleTimeout;
            var timeBeforeStop = (willBeIdleAt - clock.Now).Positive();
            var timeBeforeCountdown =
                (lastEntryAt + options.IdleTimeoutBeforeCountdown - clock.Now).Positive();
            if (timeBeforeStop <= Eps) {
                // notify is idle and stop counting down
                yield return null;
                yield break;
            }
            if (timeBeforeCountdown <= Eps) {
                // continue counting down
                yield return willBeIdleAt;
                await Task.Delay(TimeSpanExt.Min(timeBeforeStop, options.CheckInterval),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else {
                // reset countdown since there were new messages
                yield return null;
                await Task.Delay(timeBeforeCountdown, cancellationToken).ConfigureAwait(false);
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

    private async Task SyncRecordingState(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var cRecordingState = await Computed
            .Capture(() => GetRecordingState(cancellationToken))
            .ConfigureAwait(false);
        var prev = RecordingState.None;
        var current = RecordingState.None;
        await foreach (var change in cRecordingState.Changes(cancellationToken).ConfigureAwait(false))
            try {
                current = change.Value;
                Log.LogDebug(nameof(SyncRecordingState) + " change");
                var (recordingChatId, recorderChatId, recorderError, language) = current;
                var isRecordingFailed = recorderError != null;
                var mustStop =
                    isRecordingFailed
                    || ((recorderChatId != recordingChatId || language != prev.Language) && !prev.RecorderChatId.IsNone);
                var mustSync = mustStop || recordingChatId != prev.RecordingChatId;
                if (isRecordingFailed)
                    ShowRecorderError(recorderError!.Value);
                if (mustSync) {
                    Log.LogDebug("Needs sync recorder state: prev={Prev}, current={Current}", prev, current);
                    if (isRecordingFailed)
                        recordingChatId = ChatId.None;
                    await UpdateRecorderState(mustStop, recordingChatId, cancellationToken).ConfigureAwait(false);
                    if (!recordingChatId.IsNone && !mustStop)
                        // Start recording = start realtime playback
                        await SetListeningState(recordingChatId, true).ConfigureAwait(false);
                }
                else if (recorderChatId != prev.RecorderChatId)
                    // Something stopped (or started?) the recorder
                    await SetRecordingChatId(recorderChatId).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                if (!current.RecordingChatId.IsNone)
                    await SetRecordingChatId(ChatId.None).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    throw;
            }
            catch (Exception e) {
                Log.LogError(e, "Failed to sync recording state");

                // mark recording stopped in case of timeout
                if (!current.RecordingChatId.IsNone) {
                    await SetRecordingChatId(ChatId.None).ConfigureAwait(false);
                    ErrorUI.ShowError("Recording failed.");
                }
            }
            finally {
                prev = change.Value;
            }
    }

    [ComputeMethod]
    protected virtual async Task<RecordingState> GetRecordingState(CancellationToken cancellationToken)
    {
        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        var recorderState = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var recorderChatId = recorderState?.ChatId ?? default;
        var recorderError = recorderState?.Error;
        var language = await LanguageUI.GetChatLanguage(recorderChatId, cancellationToken).ConfigureAwait(false);
        return new(recordingChatId, recorderChatId, recorderError, language);
    }

    private Task UpdateRecorderState(
        bool mustStop,
        ChatId recordingChatId,
        CancellationToken cancellationToken)
        => BackgroundTask.Run(async () => {
            if (mustStop) {
                // Recording is running - let's top it first;
                await AudioRecorder.StopRecording(cancellationToken).ConfigureAwait(false);
            }
            if (!recordingChatId.IsNone) {
                // And start the recording if we must
                if (!InteractiveUI.IsInteractive.Value) {
                    var isConfirmed = false;
                    var chat = await Chats.Get(Session, recordingChatId, cancellationToken).ConfigureAwait(false);
                    if (chat != null) {
                        var operation = $"recording in \"{chat.Title}\"";
                        isConfirmed = await InteractiveUI.Demand(operation, cancellationToken).ConfigureAwait(false);
                    }
                    if (!isConfirmed) {
                        await SetRecordingChatId(ChatId.None).ConfigureAwait(false);
                        // An extra pause to make sure we don't apply changes too frequently
                        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                }
                await AudioRecorder.StartRecording(recordingChatId, cancellationToken).ConfigureAwait(false);
            }
        }, Log, "Failed to apply new recording state.", CancellationToken.None);

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
                        Log.LogDebug("PushRealtimePlaybackState: stopping playback");
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
                        Log.LogDebug("PushRealtimePlaybackState: starting playback");
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

    private void ShowRecorderError(AudioRecorderError recorderError)
    {
        var message = recorderError switch {
            AudioRecorderError.Microphone => "Microphone is not ready.",
            AudioRecorderError.Timeout => "Unable to start recording in time.",
            _ => "Voice recording failed.",
        };
        ErrorUI.ShowError(message);
    }

    protected sealed record RecordingState(ChatId RecordingChatId, ChatId RecorderChatId, AudioRecorderError? RecorderError, Language Language)
    {
        public static readonly RecordingState None = new (ChatId.None, ChatId.None, null, Language.None);
    }

    public record IdleMonitoringOptions(TimeSpan IdleTimeout,
        TimeSpan IdleTimeoutBeforeCountdown,
        TimeSpan CheckInterval)
    {
        public void Validate()
        {
            if (IdleTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(IdleTimeout));
            if (CheckInterval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(CheckInterval));
            if (IdleTimeoutBeforeCountdown > IdleTimeout)
                throw new ArgumentOutOfRangeException(nameof(IdleTimeoutBeforeCountdown), IdleTimeoutBeforeCountdown, $"{nameof(IdleTimeoutBeforeCountdown)} cannot be greater than {nameof(IdleTimeout)}");
        }
    }

    private sealed class Operation : WorkerBase
    {
        private Func<CancellationToken, Task> TaskFactory { get; }

        public Operation(Func<CancellationToken, Task> taskFactory, CancellationToken cancellationToken)
            : base(cancellationToken.CreateLinkedTokenSource())
            => TaskFactory = taskFactory;

        protected override async Task RunInternal(CancellationToken cancellationToken)
        {
            await Task.Yield();
            await TaskFactory(cancellationToken).ConfigureAwait(false);
        }
    }
}
