namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatUI
{
    private LanguageId? _lastRecordingLanguage;
    private Symbol _lastRecordingChatId;
    private Symbol _lastRecorderChatId;

    // All state sync logic should be here

    protected override Task RunInternal(CancellationToken cancellationToken)
        => Task.WhenAll(
            InvalidateSelectedChatDependencies(cancellationToken),
            InvalidateActiveChatDependencies(cancellationToken),
            InvalidateHistoricalPlaybackDependencies(cancellationToken),
            PushRealtimePlaybackState(cancellationToken),
            SyncRecordingState(cancellationToken),
            PushKeepAwakeState(cancellationToken),
            ResetHighlightedChatEntry(cancellationToken),
            StopRecordingWhenInactive(cancellationToken),
            Task.CompletedTask); // Just to add more items w/o need to worry about comma :)

    private async Task InvalidateSelectedChatDependencies(CancellationToken cancellationToken)
    {
        var oldChatId = SelectedChatId.Value;
        var changes = SelectedChatId.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cSelectedContactId in changes.ConfigureAwait(false)) {
            var newChatId = cSelectedContactId.Value;
            if (newChatId == oldChatId)
                continue;

            using (Computed.Invalidate()) {
                _ = IsSelected(oldChatId);
                _ = IsSelected(newChatId);
            }

            oldChatId = newChatId;
        }
    }

    private async Task InvalidateActiveChatDependencies(CancellationToken cancellationToken)
    {
        var oldRecordingChat = default(ActiveChat);
        var oldListeningChats = new HashSet<ActiveChat>();
        var changes = ActiveChats.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cActiveContacts in changes.ConfigureAwait(false)) {
            var activeChats = cActiveContacts.Value;
            var newRecordingChat = activeChats.FirstOrDefault(c => c.IsRecording);
            var newListeningChats = activeChats.Where(c => c.IsListening).ToHashSet();

            var added = newListeningChats.Except(oldListeningChats);
            var removed = oldListeningChats.Except(newListeningChats);
            var changed = added.Concat(removed).ToList();
            using (Computed.Invalidate()) {
                if (newRecordingChat != oldRecordingChat) {
                    _ = GetRecordingChatId();
                    _ = GetMediaState(oldRecordingChat.ChatId);
                    _ = GetMediaState(newRecordingChat.ChatId);
                }
                if (changed.Count > 0) {
                    _ = GetListeningChatIds();
                    foreach (var c in changed)
                        _ = GetMediaState(c.ChatId);
                }
            }

            oldRecordingChat = newRecordingChat;
            oldListeningChats = newListeningChats;
        }
    }

    private async Task InvalidateHistoricalPlaybackDependencies(CancellationToken cancellationToken)
    {
        var oldChatId = ChatId.None;
        var changes = ChatPlayers.PlaybackState.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cPlaybackState in changes.ConfigureAwait(false)) {
            var newChatId = (cPlaybackState.Value as HistoricalPlaybackState)?.ChatId ?? default;
            if (newChatId == oldChatId)
                continue;

            using (Computed.Invalidate()) {
                _ = GetMediaState(oldChatId);
                _ = GetMediaState(newChatId);
            }

            oldChatId = newChatId;
        }
    }


    private async Task PushRealtimePlaybackState(CancellationToken cancellationToken)
    {
        using var dCancellationTask = cancellationToken.ToTask();
        var cancellationTask = dCancellationTask.Resource;

        var cExpectedRealtimePlaybackStateBase = await Computed
            .Capture(GetExpectedRealtimePlaybackState)
            .ConfigureAwait(false);
        var playbackState = ChatPlayers.PlaybackState;

        var changes = cExpectedRealtimePlaybackStateBase.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cExpectedPlaybackState in changes.ConfigureAwait(false)) {
            var expectedPlaybackState = cExpectedPlaybackState.ValueOrDefault;
            while (cExpectedPlaybackState.IsConsistent()) {
                var playbackStateValue = playbackState.Value;
                if (playbackStateValue is null or RealtimePlaybackState) {
                    if (!ReferenceEquals(playbackStateValue, expectedPlaybackState)) {
                        if (playbackStateValue is null && !InteractiveUI.IsInteractive.Value)
                            await InteractiveUI.Demand("audio playback").ConfigureAwait(false);
                        playbackState.Value = expectedPlaybackState;
                    }
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                await Task.WhenAny(
                        playbackState.Computed.WhenInvalidated(CancellationToken.None),
                        cExpectedPlaybackState.WhenInvalidated(CancellationToken.None),
                        cancellationTask)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    private async Task SyncRecordingState(CancellationToken cancellationToken)
    {
        var cRecordingChatId = await Computed
            .Capture(() => SyncRecordingStateImpl(cancellationToken))
            .ConfigureAwait(false);
        // Let's update it continuously -- solely for the side effects of GetRecordingChatId runs
        await cRecordingChatId.When(_ => false, FixedDelayer.ZeroUnsafe, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    protected virtual async Task<Symbol> SyncRecordingStateImpl(CancellationToken cancellationToken)
    {
        // This compute method creates dependencies & gets recomputed on changes by SyncRecordingState.
        // The result it returns doesn't have any value - it runs solely for its own side effects.

        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        var recordingChatIdChanged = recordingChatId != _lastRecordingChatId;
        _lastRecordingChatId = recordingChatId;

        var recorderState = await AudioRecorder.State.Use(cancellationToken).ConfigureAwait(false);
        var recorderChatId = recorderState?.ChatId ?? default;
        var recorderChatIdChanged = recorderChatId != _lastRecorderChatId;
        _lastRecorderChatId = recorderChatId;

        if (recordingChatId == recorderChatId) {
            // The state is in sync
            if (recordingChatId.IsNone)
                return default;

            if (await IsRecordingLanguageChanged().ConfigureAwait(false))
                SyncRecorderState(); // We need to toggle the recording in this case
        } else if (recordingChatIdChanged) {
            // The recording was activated or deactivated
            SyncRecorderState();
            if (recordingChatId.IsNone)
                return default;

            // Update _lastLanguageId
            await IsRecordingLanguageChanged().ConfigureAwait(false);
            // Start recording = start realtime playback
            await SetListeningState(recordingChatId, true).ConfigureAwait(false);
        } else if (recorderChatIdChanged) {
            // Something stopped (or started?) the recorder
            await SetRecordingChatId(recorderChatId).ConfigureAwait(false);
        }
        return default;

        async ValueTask<bool> IsRecordingLanguageChanged()
        {
            var language = await LanguageUI.GetChatLanguage(recorderChatId, cancellationToken).ConfigureAwait(false);
            var isLanguageChanged = _lastRecordingLanguage.HasValue && language != _lastRecordingLanguage;
            _lastRecordingLanguage = language;
            return isLanguageChanged;
        }

        void SyncRecorderState()
            => UpdateRecorderState(recorderState != null && recorderChatId != recordingChatId, recordingChatId, cancellationToken);
    }

    private Task UpdateRecorderState(
        bool mustStop,
        ChatId chatIdToStartRecording,
        CancellationToken cancellationToken)
        => BackgroundTask.Run(async () => {
                if (mustStop) {
                    // Recording is running - let's top it first;
                    await AudioRecorder.StopRecording(cancellationToken).ConfigureAwait(false);
                }
                if (!chatIdToStartRecording.IsNone) {
                    // And start the recording if we must
                    if (!InteractiveUI.IsInteractive.Value)
                        await InteractiveUI.Demand("audio recording").ConfigureAwait(false);
                    await AudioRecorder.StartRecording(chatIdToStartRecording, cancellationToken).ConfigureAwait(false);
                }
            },
            Log,
            "Failed to apply new recording state.",
            CancellationToken.None);

    [ComputeMethod]
    protected virtual async Task<bool> MustKeepAwake()
    {
        var activeChats = await ActiveChats.Use().ConfigureAwait(false);
        return activeChats.Any(c => c.IsListening || c.IsRecording);
    }

    private async Task PushKeepAwakeState(CancellationToken cancellationToken)
    {
        var lastMustKeepAwake = false;
        var cMustKeepAwake0 = await Computed
            .Capture(MustKeepAwake)
            .ConfigureAwait(false);

        var changes = cMustKeepAwake0.Changes(FixedDelayer.Get(1), cancellationToken);
        await foreach (var cMustKeepAwake in changes.ConfigureAwait(false)) {
            var mustKeepAwake = cMustKeepAwake.Value;
            if (mustKeepAwake != lastMustKeepAwake) {
                // TODO(AY): Send this update to JS
                await KeepAwakeUI.SetKeepAwake(mustKeepAwake);
                lastMustKeepAwake = mustKeepAwake;
            }
        }
    }

    /// <summary>
    /// Monitors for inactivity for amount of time defined in ChatSettings.TurnOffRecordingAfterIdleTimeout.
    /// If no speech was transcribed from recording during this period the recording stops automatically.
    /// </summary>
    private async Task StopRecordingWhenInactive(CancellationToken cancellationToken)
    {
        var cLastChatEntry = await Computed
            .Capture(() => GetLastRecordingChatEntryInfo(cancellationToken))
            .ConfigureAwait(false);
        var lastChatEntry = (Symbol.Empty, 0L);

        while (!cancellationToken.IsCancellationRequested) {
            // Wait for recording started
            cLastChatEntry = await cLastChatEntry.When(x => !x.ChatId.IsEmpty, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = new CancellationTokenSource(AudioSettings.IdleRecordingTimeout);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try {
                var toCompare = lastChatEntry;
                cLastChatEntry = await cLastChatEntry.When(x => toCompare != x, cts.Token).ConfigureAwait(false);
                lastChatEntry = cLastChatEntry.Value;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
                await UpdateRecorderState(true, default, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    [ComputeMethod]
    protected virtual async Task<(Symbol ChatId, long LastEntryId)> GetLastRecordingChatEntryInfo(
        CancellationToken cancellationToken)
    {
        var recordingChatId = await GetRecordingChatId().ConfigureAwait(false);
        if (recordingChatId.IsNone)
            return (recordingChatId, 0);

        var (_, end) = await Chats
            .GetIdRange(Session, recordingChatId, ChatEntryKind.Text, cancellationToken)
            .ConfigureAwait(false);
        return (recordingChatId, end);
    }

    private async Task ResetHighlightedChatEntry(CancellationToken cancellationToken)
    {
        var changes = HighlightedChatEntryId
            .Changes(FixedDelayer.ZeroUnsafe, cancellationToken)
            .Where(x => x.Value != 0);
        CancellationTokenSource? cts = null;
        try {
            await foreach (var cHighlightedChatEntryId in changes.ConfigureAwait(false)) {
                cts.CancelAndDisposeSilently();
                cts = cancellationToken.CreateLinkedTokenSource();
                var ctsToken = cts.Token;
                var highlightedChatEntryId = cHighlightedChatEntryId.Value;
                _ = BackgroundTask.Run(async () => {
                    await Clocks.UIClock.Delay(TimeSpan.FromSeconds(2), ctsToken).ConfigureAwait(false);
                    HighlightedChatEntryId.Set(
                        highlightedChatEntryId,
                        (expected, result) => result.IsValue(out var v) && v == expected ? default : result);
                }, CancellationToken.None);
            }
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }
}
