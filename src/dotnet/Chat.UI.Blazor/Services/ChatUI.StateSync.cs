namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatUI
{
    private Language? _lastRecordingLanguage;
    private ChatId _lastRecordingChatId;
    private ChatId _lastRecorderChatId;

    // All state sync logic should be here

    protected override Task RunInternal(CancellationToken cancellationToken)
    {
        var baseChains = new AsyncChain[] {
            new(nameof(InvalidateSelectedChatDependencies), InvalidateSelectedChatDependencies),
            new(nameof(InvalidateActiveChatDependencies), InvalidateActiveChatDependencies),
            new(nameof(InvalidateHistoricalPlaybackDependencies), InvalidateHistoricalPlaybackDependencies),
            new(nameof(PushRealtimePlaybackState), PushRealtimePlaybackState),
            new(nameof(SyncRecordingState), SyncRecordingState),
            new(nameof(PushKeepAwakeState), PushKeepAwakeState),
            new(nameof(ResetHighlightedEntry), ResetHighlightedEntry),
            new(nameof(StopRecordingWhenIdle), StopRecordingWhenIdle),
        };
        var retryDelays = new RetryDelaySeq(100, 1000);
        return (
            from chain in baseChains
            select chain
                .RetryForever(retryDelays, Log)
                // .LogBoundary(LogLevel.Debug, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task InvalidateSelectedChatDependencies(CancellationToken cancellationToken)
    {
        var oldChatId = SelectedChatId.Value;
        var changes = SelectedChatId.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
        await foreach (var cSelectedContactId in changes.ConfigureAwait(false)) {
            var newChatId = cSelectedContactId.Value;
            if (newChatId == oldChatId)
                continue;

            Log.LogDebug("InvalidateSelectedChatDependencies: *");
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

            Log.LogDebug("InvalidateActiveChatDependencies: *");
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

            Log.LogDebug("InvalidateHistoricalPlaybackDependencies: *");
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

        var cExpectedPlaybackState = await Computed
            .Capture(GetExpectedRealtimePlaybackState)
            .ConfigureAwait(false);
        var playbackState = ChatPlayers.PlaybackState;
        var cActualPlaybackState = playbackState.Computed;

        while (!cancellationToken.IsCancellationRequested) {
            var expectedPlaybackState = cExpectedPlaybackState.Value;
            var actualPlaybackState = cActualPlaybackState.Value;
            if (actualPlaybackState is null or RealtimePlaybackState) {
                if (!ReferenceEquals(actualPlaybackState, expectedPlaybackState)) {
                    if (actualPlaybackState is null && !InteractiveUI.IsInteractive.Value)
                        await InteractiveUI.Demand("audio playback").ConfigureAwait(false);

                    Log.LogDebug("PushRealtimePlaybackState: applying changes");
                    playbackState.Value = expectedPlaybackState;
                    continue;
                }
            }

            Log.LogDebug("PushRealtimePlaybackState: waiting for changes");
            await Task.WhenAny(
                cActualPlaybackState.WhenInvalidated(cancellationToken),
                cExpectedPlaybackState.WhenInvalidated(cancellationToken),
                cancellationTask
                ).ConfigureAwait(false);
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);

            cExpectedPlaybackState = await cExpectedPlaybackState.Update(cancellationToken).ConfigureAwait(false);
            cActualPlaybackState = playbackState.Computed;
        }
        // ReSharper disable once FunctionNeverReturns
    }

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
                Log.LogDebug("PushKeepAwakeState: *");
                await KeepAwakeUI.SetKeepAwake(mustKeepAwake);
                lastMustKeepAwake = mustKeepAwake;
            }
        }
    }

    private async Task ResetHighlightedEntry(CancellationToken cancellationToken)
    {
        var changes = HighlightedEntryId
            .Changes(FixedDelayer.ZeroUnsafe, cancellationToken)
            .Where(x => !x.Value.IsNone);
        CancellationTokenSource? cts = null;
        try {
            await foreach (var cHighlightedEntryId in changes.ConfigureAwait(false)) {
                cts.CancelAndDisposeSilently();
                cts = cancellationToken.CreateLinkedTokenSource();
                var ctsToken = cts.Token;
                var highlightedEntryId = cHighlightedEntryId.Value;
                _ = BackgroundTask.Run(async () => {
                    await Clocks.UIClock.Delay(TimeSpan.FromSeconds(2), ctsToken).ConfigureAwait(false);
                    var isReset = false;
                    lock (_lock) {
                        if (_highlightedEntryId.Value == highlightedEntryId) {
                            _highlightedEntryId.Value = default;
                            isReset = true;
                        }
                    }
                    if (isReset)
                        _ = UICommander.RunNothing();
                }, CancellationToken.None);
            }
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }
}
