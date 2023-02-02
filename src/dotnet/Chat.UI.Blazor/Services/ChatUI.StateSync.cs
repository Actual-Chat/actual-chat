namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatUI
{
    // All state sync logic should be here

    protected override Task RunInternal(CancellationToken cancellationToken)
    {
        var baseChains = new AsyncChain[] {
            new(nameof(InvalidateSelectedChatDependencies), InvalidateSelectedChatDependencies),
            new(nameof(ResetHighlightedEntry), ResetHighlightedEntry),
            new(nameof(PushKeepAwakeState), PushKeepAwakeState),
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
        var changes = SelectedChatId.Changes(cancellationToken);
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

    [ComputeMethod]
    protected virtual async Task<bool> MustKeepAwake()
    {
        var activeChats = await ActiveChatsUI.ActiveChats.Use().ConfigureAwait(false);
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
            .Changes(FixedDelayer.Get(0.1), cancellationToken)
            .Where(x => !x.Value.IsNone);
        CancellationTokenSource? cts = null;
        try {
            await foreach (var cHighlightedEntryId in changes.ConfigureAwait(false)) {
                cts.CancelAndDisposeSilently();
                var highlightedEntryId = cHighlightedEntryId.Value;
                if (highlightedEntryId.IsNone)
                    continue; // Nothing to reset

                cts = cancellationToken.CreateLinkedTokenSource();
                var ctsToken = cts.Token;
                _ = BackgroundTask.Run(async () => {
                    await Task.Delay(TimeSpan.FromSeconds(2), ctsToken).ConfigureAwait(false);
                    if (HighlightedEntryId.Value == highlightedEntryId)
                        HighlightEntry(ChatEntryId.None, false);
                }, CancellationToken.None);
            }
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }
}
