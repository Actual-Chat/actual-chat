using ActualChat.Hosting;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatListUI
{
    // All state sync logic should be here

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(InvalidateIsSelectedChatUnlisted),
            new($"{nameof(PushItems)}({ChatListKind.Active})", ct => PushItems(ChatListKind.Active, ct)),
            new($"{nameof(PushItems)}({ChatListKind.All})", ct => PushItems(ChatListKind.All, ct)),
            AsyncChain.From(ResetPushIfStuck),
            AsyncChain.From(PlayTuneOnNewMessages),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task InvalidateIsSelectedChatUnlisted(CancellationToken cancellationToken)
    {
        var cValueBase = await Computed
            .Capture(() => IsSelectedChatUnlistedInternal(cancellationToken), cancellationToken)
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
        var cList = await Computed
            .Capture(() => List(listKind, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var changes = cList.Changes(cancellationToken);
        await foreach (var (list, error) in changes.ConfigureAwait(false)) {
            if (error != null)
                continue;

            DebugLog?.LogDebug("PushItems({ListKind}): push", listKind);
            var chatIds = new ChatId[list.Count];
            for (var i = 0; i < list.Count; i++)
                chatIds[i] = list[i].Id;
            PushItems(listKind, chatIds);
        }
    }

    private void PushItems(ChatListKind listKind, ChatId[] newItems)
    {
        bool isCountChanged;
        var changedIndexes = new List<int>();
        var oldItems = GetItems(listKind);
        int oldCount;
        int newCount;

        lock (oldItems) {
            oldCount = oldItems.Count;
            newCount = newItems.Length;
            isCountChanged = oldItems.Count != newItems.Length;
            var commonLength = Math.Min(oldItems.Count, newItems.Length);
            for (int i = 0; i < commonLength; i++)
                if (oldItems[i] != newItems[i])
                    changedIndexes.Add(i);

            // If list is shrinking, do not invalidate items with index greater than new list length.
            // It will prevent recalculating correspondent ChatListItem`s. They will anyway will
            // be removed on ChatList re-render.
            // If list is expanding, do invalidate items with index greater than old list length.
            if (newItems.Length > oldItems.Count) {
                for (int i = commonLength; i < newItems.Length; i++)
                    changedIndexes.Add(i);
            }

            oldItems.Clear();
            oldItems.AddRange(newItems);
        }

        if (changedIndexes.Count == 0 && !isCountChanged)
            return;

        using (Computed.Invalidate()) {
            if (isCountChanged) {
                DebugLog?.LogDebug("PushItems({ListKind}, {OldCount}->{NewCount}): invalidating GetCount",
                    listKind, oldCount, newCount);
                _ = GetCount(listKind);
            }

            DebugLog?.LogDebug("PushItems({ListKind}): invalidating {Count} indexes: {Indexes}",
                listKind, changedIndexes.Count, changedIndexes.ToDelimitedString());
            foreach (var i in changedIndexes)
                _ = GetItem(listKind, i);
        }
    }

    private async Task ResetPushIfStuck(CancellationToken cancellationToken)
    {
        var cItem0 = await Computed
            .Capture(() => GetItem(ChatListKind.All, 0), cancellationToken)
            .ConfigureAwait(false);
        var delaySeq = RetryDelaySeq.Exp(5, 120, 0, 2);
        while (true) {
            if (!cItem0.ValueOrDefault.Item2.IsNone) {
                // We're fine
                await cItem0.WhenInvalidated(cancellationToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken).ConfigureAwait(false);
                cItem0 = await cItem0.Update(cancellationToken).ConfigureAwait(false);
                continue;
            }

            // We're not fine: GetItem(ChatListKind.All, 0) == None
            var tryIndex = 1;
            while (true) {
                await Task.Delay(delaySeq[tryIndex], cancellationToken).ConfigureAwait(false);
                cItem0 = await cItem0.Update(cancellationToken).ConfigureAwait(false);
                if (!cItem0.ValueOrDefault.Item2.IsNone)
                    break; // We're fine

                // And it's still None after delay
                Log.LogWarning($"{nameof(ResetPushIfStuck)}: chat list stuck in the loading state, invalidating...");
                tryIndex++;
                using (Computed.Invalidate())
                    _ = PseudoListAllUnorderedRawDependency();
            }
        }
    }

    private async Task PlayTuneOnNewMessages(CancellationToken cancellationToken)
    {
        if (Hub.HostInfo().AppKind.IsMobile())
            return; // skip tune notifications for mobile MAUI

        var cChatInfoMap = await Computed
            .Capture(() => ListOverallUnorderedRaw(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var previous = await cChatInfoMap.Use(cancellationToken).ConfigureAwait(false);
        var lastPlayedAt = CpuNow; // Skip tune after loading
        await foreach (var change in cChatInfoMap.Changes(cancellationToken).ConfigureAwait(false))
            await OnChange(change.Value).ConfigureAwait(false);
        return;

        async Task OnChange(IReadOnlyDictionary<ChatId, ChatInfo> chatInfoMap)
        {
            var selectedChatId = await ChatUI.SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
            var otherChatInfoItems = chatInfoMap.Where(x => x.Key != selectedChatId);
            if (lastPlayedAt + MinNotificationInterval <= CpuNow)
                foreach (var (chatId, chatInfo) in otherChatInfoItems) {
                    var isAlreadyExists = previous.TryGetValue(chatId, out var prevChatInfo);
                    if (!isAlreadyExists) {
                        // notify on new chat
                        _ = TuneUI.Play(Tune.NotifyOnNewMessageInApp);
                        lastPlayedAt = CpuNow;
                        break;
                    }

                    var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
                    var hasNewUnreadMessages = prevChatInfo!.UnmutedUnreadCount < chatInfo.UnmutedUnreadCount;
                    var isLastMessageOwn = chatInfo.LastTextEntry?.AuthorId == ownAuthor?.Id;
                    if (!hasNewUnreadMessages || isLastMessageOwn)
                        continue;

                    // notify on new message from other authors
                    _ = TuneUI.Play(Tune.NotifyOnNewMessageInApp);
                    lastPlayedAt = CpuNow;
                    break;
                }
            previous = chatInfoMap;
        }
    }
}
