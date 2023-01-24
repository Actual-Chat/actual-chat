using ActualChat.Chat.UI.Blazor.Events;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatUI
{
    private const int MinRestartDelayMs = 100;
    private const int MaxRestartDelayMs = 2_000;

    private Language? _lastRecordingLanguage;
    private ChatId _lastRecordingChatId;
    private ChatId _lastRecorderChatId;

    // All state sync logic should be here

    protected override Task RunInternal(CancellationToken cancellationToken)
        => Task.WhenAll(
            InvalidateActiveChatDependencies(cancellationToken),
            InvalidateHistoricalPlaybackDependencies(cancellationToken),
            PushRealtimePlaybackState(cancellationToken),
            SyncRecordingState(cancellationToken),
            PushKeepAwakeState(cancellationToken),
            ResetHighlightedEntry(cancellationToken),
            StopRecordingWhenIdle(cancellationToken),
            Task.CompletedTask); // Just to add more items w/o need to worry about comma :)

    private async Task InvalidateActiveChatDependencies(CancellationToken cancellationToken)
    {
        while (true) {
            try {
                var oldRecordingChat = default(ActiveChat);
                var oldListeningChats = new HashSet<ActiveChat>();
                var changes = ChatListUI.ActiveChats.Changes(FixedDelayer.ZeroUnsafe, cancellationToken);
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
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, $"{nameof(InvalidateActiveChatDependencies)} failed");
            }
            var random = Random.Shared.Next(MinRestartDelayMs, MaxRestartDelayMs);
            await Clocks.CoarseCpuClock.Delay(random, cancellationToken);
        }
    }

    private async Task InvalidateHistoricalPlaybackDependencies(CancellationToken cancellationToken)
    {
        while (true) {
            try {
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
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, $"{nameof(InvalidateHistoricalPlaybackDependencies)} failed");
            }
            var random = Random.Shared.Next(MinRestartDelayMs, MaxRestartDelayMs);
            await Clocks.CoarseCpuClock.Delay(random, cancellationToken);
        }
    }


    private async Task PushRealtimePlaybackState(CancellationToken cancellationToken)
    {
        while (true) {
            try {
                using var dCancellationTask = cancellationToken.ToTask();
                var cancellationTask = dCancellationTask.Resource;

                var cExpectedPlaybackStateBase = await Computed
                    .Capture(GetExpectedRealtimePlaybackState)
                    .ConfigureAwait(false);
                var playbackState = ChatPlayers.PlaybackState;

                while (true) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var cExpectedPlaybackState = await cExpectedPlaybackStateBase.Update(cancellationToken).ConfigureAwait(false);
                    var cActualPlaybackState = playbackState.Computed;
                    var expectedPlaybackState = cExpectedPlaybackStateBase.Value;
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
                            cancellationTask)
                        .ConfigureAwait(false);
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                // ReSharper disable once FunctionNeverReturns
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, $"{nameof(PushRealtimePlaybackState)} failed");
            }
            var random = Random.Shared.Next(MinRestartDelayMs, MaxRestartDelayMs);
            await Clocks.CoarseCpuClock.Delay(random, cancellationToken);
        }
    }

    [ComputeMethod]
    protected virtual async Task<bool> MustKeepAwake()
    {
        var activeChats = await ChatListUI.ActiveChats.Use().ConfigureAwait(false);
        return activeChats.Any(c => c.IsListening || c.IsRecording);
    }

    private async Task PushKeepAwakeState(CancellationToken cancellationToken)
    {
        while (true) {
            try {
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
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, $"{nameof(PushKeepAwakeState)} failed");
            }
            var random = Random.Shared.Next(MinRestartDelayMs, MaxRestartDelayMs);
            await Clocks.CoarseCpuClock.Delay(random, cancellationToken);
        }
    }


    private async Task ResetHighlightedEntry(CancellationToken cancellationToken)
    {
        while (true) {
            try {
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
            catch (Exception e) when (e is not OperationCanceledException) {
                Log.LogError(e, $"{nameof(ResetHighlightedEntry)} failed");
            }
            var random = Random.Shared.Next(MinRestartDelayMs, MaxRestartDelayMs);
            await Clocks.CoarseCpuClock.Delay(random, cancellationToken);
        }
    }
}
