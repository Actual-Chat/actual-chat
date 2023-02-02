namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatAudioUI
{
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

        var options = new IdleAudioMonitor.Options(AudioSettings.IdleRecordingTimeout,
            AudioSettings.IdleRecordingTimeoutBeforeCountdown,
            AudioSettings.IdleRecordingCheckInterval);

        var cChatId = await Computed.Capture(GetRecordingChatId).ConfigureAwait(false);
        var prevChatId = cChatId.Value;
        await foreach (var change in cChatId.Changes(cancellationToken).ConfigureAwait(false)) {
            var chatId = change.Value;
            if (!prevChatId.IsNone)
                await IdleRecordingMonitor.StopMonitoring(prevChatId).ConfigureAwait(false);
            if (!chatId.IsNone)
                IdleRecordingMonitor.StartMonitoring(chatId, OnIdleChanged, options, cancellationToken);
            prevChatId = chatId;
        }

        Task OnIdleChanged(ChatId _, IdleAudioMonitor.State state)
        {
            _stopRecordingAt.Value = state.WillBeIdleAt;
            return state.IsIdle ? UpdateRecorderState(true, default, cancellationToken) : Task.CompletedTask;
        }
    }

    private async Task StopListeningWhenIdle(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var options = new IdleAudioMonitor.Options(AudioSettings.IdleListeningTimeout,
            AudioSettings.IdleListeningTimeout - AudioSettings.IdleListeningCheckInterval + TimeSpan.FromSeconds(1),
            AudioSettings.IdleListeningCheckInterval);
        var cListeningChatIds = await Computed.Capture(GetListeningChatIds).ConfigureAwait(false);
        var prevListeningChatIds = cListeningChatIds.Value;
        await foreach (var change in cListeningChatIds.Changes(cancellationToken)) {
            var listeningChatIds = change.Value;
            var toStop = prevListeningChatIds.Except(listeningChatIds).ToList();
            var toStart = listeningChatIds.Except(prevListeningChatIds).ToList();
            await IdleListeningMonitor.StopMonitoring(toStop).ConfigureAwait(false);
            IdleListeningMonitor.StartMonitoring(toStart, OnIdleChanged, options, cancellationToken);
            prevListeningChatIds = listeningChatIds;
        }

        Task OnIdleChanged(ChatId chatId, IdleAudioMonitor.State state)
            => state.IsIdle ? SetListeningState(chatId, false).AsTask() : Task.CompletedTask;
    }

    private async Task SyncRecordingState(CancellationToken cancellationToken)
    {
        // Don't start till the moment ChatAudioUI gets enabled
        await WhenEnabled.WaitAsync(cancellationToken).ConfigureAwait(false);

        var cRecordingState = await Computed
            .Capture(() => GetRecordingState(cancellationToken))
            .ConfigureAwait(false);
        var prev = RecordingState.None;
        await foreach (var change in cRecordingState.Changes(cancellationToken).ConfigureAwait(false)) {
            var (recordingChatId, recorderChatId, language) = change.Value;
            var mustStop = (recorderChatId != recordingChatId || language != prev.Language) && !prev.RecorderChatId.IsNone;
            var mustSync = mustStop || recordingChatId != prev.RecordingChatId;
            if (mustSync) {
                await UpdateRecorderState(mustStop, recordingChatId, cancellationToken).ConfigureAwait(false);
                if (!recordingChatId.IsNone)
                    // Start recording = start realtime playback
                    await SetListeningState(recordingChatId, true);
            } else if (recorderChatId != prev.RecorderChatId)
                // Something stopped (or started?) the recorder
                await SetRecordingChatId(recordingChatId).ConfigureAwait(false);
            prev = change.Value;
        }
    }

    [ComputeMethod]
    protected virtual async Task<RecordingState> GetRecordingState(
        CancellationToken cancellationToken)
    {
        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        var recorderState = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var recorderChatId = recorderState?.ChatId ?? default;
        var language = await LanguageUI.GetChatLanguage(recorderChatId, cancellationToken).ConfigureAwait(false);
        return new(recordingChatId, recorderChatId, language);
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

    protected sealed record RecordingState(ChatId RecordingChatId, ChatId RecorderChatId, Language Language)
    {
        public static readonly RecordingState None = new (ChatId.None, ChatId.None, Language.None);
    }
}
