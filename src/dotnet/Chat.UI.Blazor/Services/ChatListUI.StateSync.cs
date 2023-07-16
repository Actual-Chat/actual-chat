namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatListUI
{
    // All state sync logic should be here

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new AsyncChain[] {
            new(nameof(InvalidateIsSelectedChatUnlisted), InvalidateIsSelectedChatUnlisted),
            new($"{nameof(PushItems)}({ChatListKind.Active})", ct => PushItems(ChatListKind.Active, ct)),
            new($"{nameof(PushItems)}({ChatListKind.All})", ct => PushItems(ChatListKind.All, ct)),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, DebugLog)
                .RetryForever(retryDelays, DebugLog)
            ).RunIsolated(cancellationToken);
    }

    private async Task InvalidateIsSelectedChatUnlisted(CancellationToken cancellationToken)
    {
        var cValueBase = await Computed
            .Capture(() => IsSelectedChatUnlistedInternal(cancellationToken))
            .ConfigureAwait(false);
        var changes = cValueBase.Changes(cancellationToken);
        await foreach (var cValue in changes.ConfigureAwait(false)) {
            if (_isSelectedChatUnlisted.Value == cValue.Value)
                continue;

            DebugLog?.LogDebug("InvalidateIsSelectedChatUnlisted: push");
            _isSelectedChatUnlisted.Value = cValue.Value;
        }
    }

    private async Task PushItems(ChatListKind listKind, CancellationToken cancellationToken)
    {
        var cListBase = await Computed
            .Capture(() => List(listKind, cancellationToken))
            .ConfigureAwait(false);
        var changes = cListBase.Changes(cancellationToken);
        await foreach (var cList in changes.ConfigureAwait(false)) {
            DebugLog?.LogDebug("PushItems({ListKind}): push", listKind);
            var newItems = cList.Value.Select(c => c.Id).ToList();
            PushItems(listKind, newItems);
        }
    }

    private void PushItems(ChatListKind listKind, List<ChatId> newItems)
    {
        bool isCountChanged;
        var changedIndexes = new List<int>();
        var oldItems = GetItems(listKind);
        lock (oldItems) {
            isCountChanged = oldItems.Count != newItems.Count;
            var commonLength = Math.Min(oldItems.Count, newItems.Count);
            for (int i = 0; i < commonLength; i++)
                if (oldItems[i] != newItems[i])
                    changedIndexes.Add(i);

            var maxLength = Math.Max(oldItems.Count, newItems.Count);
            for (int i = commonLength; i < maxLength; i++)
                changedIndexes.Add(i);

            oldItems.Clear();
            oldItems.AddRange(newItems);
        }

        if (changedIndexes.Count == 0 && !isCountChanged)
            return;

        using (Computed.Invalidate()) {
            DebugLog?.LogDebug("PushItems({ListKind}): invalidating {Count} indexes: {Indexes}",
                listKind, changedIndexes.Count, changedIndexes.ToDelimitedString());
            foreach (var i in changedIndexes)
                _ = GetItem(listKind, i);

            DebugLog?.LogDebug("PushItems({ListKind}): invalidating GetCount", listKind);
            if (isCountChanged)
                _ = GetCount(listKind);
        }
    }
}
