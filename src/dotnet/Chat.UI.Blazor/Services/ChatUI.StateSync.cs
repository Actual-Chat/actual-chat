using ActualChat.Chat.UI.Blazor.Events;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatUI
{
    // All state sync logic should be here

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken).ConfigureAwait(false);
        var baseChains = new AsyncChain[] {
            AsyncChainExt.From(InvalidateSelectedChatDependencies),
            AsyncChainExt.From(NavigateToFixedSelectedChat),
            AsyncChainExt.From(ResetHighlightedEntry),
            AsyncChainExt.From(PushKeepAwakeState),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        await (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task InvalidateSelectedChatDependencies(CancellationToken cancellationToken)
    {
        var oldChatId = ChatId.None;
        var changes = SelectedChatId.Changes(cancellationToken);
        await foreach (var cSelectedContactId in changes.ConfigureAwait(false)) {
            var newChatId = cSelectedContactId.Value;
            if (newChatId == oldChatId)
                continue;

            DebugLog?.LogDebug("InvalidateSelectedChatDependencies: *");
            using (Computed.Invalidate()) {
                _ = IsSelected(oldChatId);
                _ = IsSelected(newChatId);
            }

            SelectionUI.Clear();
            _ = ChatEditorUI.RestoreRelatedEntry(newChatId).ConfigureAwait(false);
            _ = UIEventHub.Publish<SelectedChatChangedEvent>(CancellationToken.None);
            _ = UICommander.RunNothing();
            oldChatId = newChatId;
        }
    }

    [ComputeMethod]
    protected virtual async Task<ChatId> GetFixedSelectedChatId(CancellationToken cancellationToken)
    {
        var chatId = await SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        var fixedChatId = await FixChatId(chatId, cancellationToken).ConfigureAwait(false);
        var wasFixed = fixedChatId != chatId;
        return wasFixed ? fixedChatId : default;
    }

    private async Task NavigateToFixedSelectedChat(CancellationToken cancellationToken)
    {
        var cFixedSelectedChatId = await Computed
            .Capture(() => GetFixedSelectedChatId(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        cFixedSelectedChatId = await cFixedSelectedChatId
            .When(x => !x.IsNone, cancellationToken)
            .ConfigureAwait(false);

        var link = Links.Chat(cFixedSelectedChatId.Value);
        var autoNavigationUI = Services.GetRequiredService<AutoNavigationUI>();
        _ = autoNavigationUI.NavigateTo(link, AutoNavigationReason.FixedChatId);
    }

    [ComputeMethod]
    protected virtual async Task<bool> MustKeepAwake(CancellationToken cancellationToken)
    {
        var activeChats = await ActiveChatsUI.ActiveChats.Use(cancellationToken).ConfigureAwait(false);
        return activeChats.Any(c => c.IsListening || c.IsRecording);
    }

    private async Task PushKeepAwakeState(CancellationToken cancellationToken)
    {
        var lastMustKeepAwake = (bool?)null;
        var cMustKeepAwake0 = await Computed
            .Capture(() => MustKeepAwake(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cMustKeepAwake0.Changes(FixedDelayer.Get(1), cancellationToken);
        await foreach (var cMustKeepAwake in changes.ConfigureAwait(false)) {
            var mustKeepAwake = cMustKeepAwake.Value;
            if (mustKeepAwake != lastMustKeepAwake) {
                await KeepAwakeUI.SetKeepAwake(mustKeepAwake).ConfigureAwait(false);
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
