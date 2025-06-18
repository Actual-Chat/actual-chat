using ActualChat.UI.Blazor.App.Events;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatUI
{
    // All state sync logic should be here

    protected override async Task OnRun(CancellationToken cancellationToken)
    {
        // All logic here can be delayed to let other code run
        await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken).ConfigureAwait(false);
        var baseChains = new[] {
            AsyncChain.From(InvalidateSelectedChatDependencies),
            AsyncChain.From(NavigateToFixedSelectedChat),
            AsyncChain.From(ResetHighlightedEntry),
            AsyncChain.From(PushKeepAwakeState),
            AsyncChain.From(SynchronizeSelectedChatIdAndActivePlaceId),
            AsyncChain.From(PrefetchChatTails),
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
        var oldChatId = (ChatId?)null;
        var changes = SelectedChatId.Computed.ChangesUntyped(cancellationToken);
        await foreach (var c in changes.ConfigureAwait(false)) {
            var cSelectedContactId = (Computed<ChatId>)c;
            var newChatId = cSelectedContactId.Value;
            if (newChatId == oldChatId)
                continue;

            DebugLog?.LogDebug("InvalidateSelectedChatDependencies: *");
            using (Invalidation.Begin()) {
                if (oldChatId is not null)
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
    protected virtual async Task<ChatId?> GetFixedSelectedChatId(CancellationToken cancellationToken)
    {
        var chatId = await SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
        var fixedChatId = await FixChatId(chatId, cancellationToken).ConfigureAwait(false);
        var wasFixed = fixedChatId != chatId;
        return wasFixed ? fixedChatId : null;
    }

    private async Task NavigateToFixedSelectedChat(CancellationToken cancellationToken)
    {
        var cFixedSelectedChatId = await Computed
            .Capture(() => GetFixedSelectedChatId(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        cFixedSelectedChatId = await cFixedSelectedChatId
            .When(x => x is not null, cancellationToken)
            .ConfigureAwait(false);

        var link = Links.Chat(cFixedSelectedChatId.Value!);
        _ = AutoNavigationUI.DispatchNavigateTo(link, AutoNavigationReason.FixedChatId);
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
        CancellationTokenSource? cts = null;
        try {
            // ReSharper disable once PossiblyMistakenUseOfCancellationToken
            var changes = HighlightedEntryId.Computed.ChangesUntyped(FixedDelayer.Get(0.1), cancellationToken);
            await foreach (var c in changes.ConfigureAwait(false)) {
                var cHighlightedEntryId = (Computed<TextEntryId?>)c;
                cts.CancelAndDisposeSilently();
                var highlightedEntryId = cHighlightedEntryId.Value;
                if (highlightedEntryId is null)
                    continue; // Nothing to reset

                cts = cancellationToken.CreateLinkedTokenSource();
                var ctsToken = cts.Token;
                _ = BackgroundTask.Run(async () => {
                    await Task.Delay(TimeSpan.FromSeconds(2), ctsToken).ConfigureAwait(false);
                    if (HighlightedEntryId.Value == highlightedEntryId)
                        HighlightEntry(null, false);
                }, CancellationToken.None);
            }
        }
        finally {
            cts.CancelAndDisposeSilently();
        }
    }

    private async Task SynchronizeSelectedChatIdAndActivePlaceId(CancellationToken cancellationToken)
    {
        await WhenReady.ConfigureAwait(false);
        _ = RestoreSelectedNavbarGroup(cancellationToken);
        _ = SynchronizeSelectedChatIds();
    }

    private async Task SynchronizeSelectedChatIds()
    {
        await _selectedChatIds.WhenRead.ConfigureAwait(false);
        lock (Lock) {
            var selectedChatIds = _selectedChatIds.Value;
            if (_pendingSelectedChatIds is { Count: > 0 }) {
                // Selected chat ids are not loaded yet.
                foreach (var chatId in _pendingSelectedChatIds)
                    selectedChatIds = selectedChatIds.SetItem((chatId as PlaceChatId)?.PlaceId.Value ?? "", chatId);
                _selectedChatIds.Value = selectedChatIds;
            }
            _pendingSelectedChatIds = null;
        }
    }

    private async Task RestoreSelectedNavbarGroup(CancellationToken cancellationToken)
    {
        try {
            var selectedChatId = await SelectedChatId.Use(cancellationToken).ConfigureAwait(false);
            if (selectedChatId is null)
                return;

            var chat = await Chats.Get(Session, selectedChatId, cancellationToken).ConfigureAwait(false);
            if (chat == null)
                return;

            var isChatsSelected = NavbarUI.IsGroupSelected(NavbarGroupIds.Chats);
            var isPlaceSelected = NavbarUI.IsPlaceSelected(out var selectedPlaceId);
            var isPeerChat = selectedChatId.Kind == ChatKind.Peer;
            var selectedAsPlaceChatId = selectedChatId as PlaceChatId;
            var isChatPlaceSelected = selectedAsPlaceChatId is not null
                && isPlaceSelected
                && Equals(selectedPlaceId, selectedAsPlaceChatId.PlaceId);
            if (!isChatsSelected && !(isPeerChat && isPlaceSelected) && !isChatPlaceSelected) {
                var navbarSettings = await NavbarSettings.Use(cancellationToken).ConfigureAwait(false);
                if (navbarSettings.PinnedChats.Contains(selectedChatId)) {
                    await Hub.Dispatcher.InvokeAsync(() => {
                            Hub.NavbarUI.SelectGroup(selectedChatId.GetNavbarGroupId(), false);
                        })
                        .ConfigureAwait(false);
                    return;
                }
            }

            if (selectedAsPlaceChatId is null)
                return;

            var place = await Hub.Places.Get(Session, selectedAsPlaceChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            if (place == null)
                return;

            await Hub.Dispatcher.InvokeAsync(() => {
                    Hub.NavbarUI.SelectGroup(place.Id.GetNavbarGroupId(), false);
                })
                .ConfigureAwait(false);
        }
        catch (Exception ex) {
            Log.LogError(ex, "RestoreSelectedPlace failed");
        }
        finally {
            _whenActivePlaceRestored.TrySetResult();
            await Hub.Dispatcher
                .InvokeAsync(() => ChatListUI.GetPlaceChatListSettings(SelectedPlaceId.Value))
                .ConfigureAwait(false);
        }
    }

    private async Task PrefetchChatTails(CancellationToken cancellationToken)
    {
        // NOTE(AY): We must rewrite this logic as follow:
        // - We don't care about visible chats - we periodically prefetch the tails of all chats
        // - Periodically = prefech next 100 tails every 10 min (w/ 1 min startup delay)
        // - We should iterate through every chat in user's contact list, even though
        //   we may also end up prefetching only top 100 chats by recency in every list
        // - Ultimately, our goal is to make sure nearly every chat is available offline.

        // NOTE(AK): This is a temporary solution to prefetch chat tails
        // - We start prefetching tails of visible chats with delay
        // - We prefetch tails of changed chats only - ChatInfo is updated when new entries are added
        await Clocks.CoarseSystemClock.Delay(TimeSpan.FromSeconds(20), cancellationToken).ConfigureAwait(false);

        var visibleChatsChanges = ChatListUI.VisibleChats.Computed.Changes(cancellationToken);
        var changeTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await foreach (var cVisibleChats in visibleChatsChanges.ConfigureAwait(false)) {
            if (cVisibleChats.Value.Count == 0)
                continue;

            changeTokenSource.CancelAndDisposeSilently();
            changeTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var changeToken = changeTokenSource.Token;
            var visibleChatIds = cVisibleChats.Value;
            foreach (var chatId in visibleChatIds)
                _ = BackgroundTask.Run(async () => {
                    var cGet = await Computed.Capture(() => Get(chatId, changeToken), changeToken).ConfigureAwait(false);
                    var chatInfoChanges = cGet.Changes(changeToken);
                    await foreach (var cChatInfo in chatInfoChanges.ConfigureAwait(false)) {
                        var chatInfo = cChatInfo.Value;
                        if (chatInfo == null)
                            continue;

                        var lastReadEntryLid = chatInfo.ReadEntryLid;
                        var prefetchNearTo = lastReadEntryLid != 0
                            ? lastReadEntryLid
                            : chatInfo.News?.TextEntryIdRange.End ?? 0;

                            var secondLayer = IdTileStack.LastLayer;
                            var idTile = secondLayer.GetTile(prefetchNearTo).Range;
                            var chatDataQuery = new ChatDataQuery(idTile, -HalfLoadLimit, LoadLimit);
                            _ = GetChatItems(chatId, chatDataQuery, lastReadEntryLid, changeToken);
                    }
                }, changeToken);
        }
    }
}
