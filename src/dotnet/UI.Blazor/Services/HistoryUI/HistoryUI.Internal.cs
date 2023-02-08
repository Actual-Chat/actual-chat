using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI
{
    private HistoryItem _defaultItem;
    private readonly List<int> _history = new();
    private readonly LruCache<int, HistoryItem> _itemById = new(MaxItemCount);
    private int _position = -1;
    private int _nextItemId;

    private HistoryItem CurrentItemUnsafe => GetItemById(_history[_position]);

    private HistoryItem NewItem()
        => new(NextItemId(), CurrentItemUnsafe.Id, GetItemById(_history[_position]).States);

    private HistoryItem GetItemByPosition(int position)
        =>  position < 0 || position >= _history.Count
            ? HistoryItem.None
            : GetItemById(_history[position]);

    private HistoryItem GetItemById(int id)
        =>_itemById.GetValueOrDefault(id);

    // Event handlers

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        DebugLog?.LogDebug("-> OnLocationChanged: {Intercepted}, '{Location}' #{State}",
            e.IsNavigationIntercepted ? "intercepted" : "internal",
            e.Location,
            e.HistoryEntryState);
        lock (Lock) {
            // We cancel active transaction, coz the change we're
            // going to apply is the one we create here.
            // Note that HistoryUI transactions are typically auto-retried,
            // so cancellation here shouldn't change anything, it will simply
            // restart once this change is applied.
            if (_transaction is { } tx)
                tx.Cancel();

            var uriState = new UriState(Hub.Nav);
            var (prevItem, prevPosition) = (CurrentItemUnsafe, _position);
            var parsedPosition = Hub.PositionFormatter.Parse(e.HistoryEntryState);

            HistoryChangeReason changeReason;
            if (parsedPosition is { } vPosition) {
                changeReason = HistoryChangeReason.NavigateToHistoryItem;
                MovePosition(vPosition);
                var oldItem = CurrentItemUnsafe;
                if (oldItem.FollowUpAction is { } followUpAction) {
                    DebugLog?.LogDebug("<- OnLocationChanged: handling follow-up action");
                    oldItem = oldItem with { FollowUpAction = null };
                    AddOrReplaceHistoryItem(oldItem, true);
                    followUpAction.Invoke();
                    return;
                }
                if (vPosition == prevPosition && uriState == prevItem.GetState<UriState>()) {
                    DebugLog?.LogDebug("<- OnLocationChanged: state push completed");
                    return;
                }
                var oldUriState = oldItem.GetState<UriState>();
                if (uriState != oldUriState) {
                    Log.LogWarning(
                        "OnLocationChanged: UriState mismatch, expected: {Expected}, actual: {Actual}",
                        oldUriState, uriState);
                    AddOrReplaceHistoryItem(oldItem.With(uriState), true);
                }
            }
            else {
                changeReason = HistoryChangeReason.NavigateToNewUri;
                var newItem = NewItem().With(uriState);
                AddOrReplaceHistoryItem(newItem, false);
            }

            var (item, position) = (CurrentItemUnsafe, _position);
            DebugLog?.LogDebug(
                "<- OnLocationChanged: {ChangeReason}, #{PrevPosition} -> #{Position}",
                changeReason, prevPosition, position);

            var change = new HistoryChange(Hub, item, prevItem, position, prevPosition, changeReason);
            Apply(change);
        }
    }

    // Private methods

    private void Apply(HistoryChange change)
    {
        var (item, prevItem) = change;
        if (item.States.Count != _defaultItem.States.Count)
            throw StandardError.Internal(
                "Something is off: count of history states doesn't match to count of registered states.");

        // Fix rounds
        for (var i = 0; i < MaxFixCount + 1; i++) {
            if (i == MaxFixCount)
                throw StandardError.Internal("Too many fixes.");

            HistoryState? fixedState = null;
            foreach (var (_, state) in item.States) {
                fixedState = state.Fix(change);
                if (!ReferenceEquals(fixedState, state) && fixedState != state)
                    break;

                fixedState = null;
            }
            if (fixedState == null) // No fixes were made
                break;

            item = item.With(fixedState);
            change = change.With(item);
        }
        if (item.IsIdenticalTo(prevItem)) // No changes
            return;

        // Extracting changes to apply & ordering them by priority
        var changesToApply = change
            .Changes(exceptUriState: true)
            .OrderBy(static c => c.State.Priority)
            .ToList();

        // Applying changes
        DebugLog?.LogDebug(
            "Apply: applying {ChangeCount} change(s) from {Change}:",
            changesToApply.Count, change);
        int n = 1;
        foreach (var stateChange in changesToApply) {
            var state = stateChange.State;
            DebugLog?.LogDebug(
                "Change {Number}:\r\n- From: {PrevState}\r\n- To:   {State}",
                n++, stateChange.PrevState, state);
            try {
                item = item.With(state.Apply(change));
                change = change.With(item);
            }
            catch (Exception e) {
                Log.LogError(e, "Apply has failed for history state '{State}'", state);
                // And we must still go on here
            }
        }

        // Applying UriState change at the very end
        var uriState = change.Item.GetState<UriState>();
        try {
            var result = GetApplyResult(change);
            DebugLog?.LogDebug("Apply result: {Result}", result);
            ProcessApplyResult(result, change.Item);
        }
        catch (Exception e) {
            Log.LogError(e, "Apply has failed for history state '{State}'", uriState);
            // And we must still go on here
        }
    }

    // Private methods

    private HistoryChangeApplyResult? GetApplyResult(HistoryChange change)
    {
        var item = change.Item;
        var backItem = GetItemByPosition(_position - 1);

        if (change.Reason is HistoryChangeReason.NavigateToNewUri)
            return new MustNavigateResult(true); // Straightforward: just persist the state

        // Special case: we might need "Back" item, but we're at the beginning of the history
        var isValidItem =
            !backItem.IsNone // There is "back" item in history
            || item.States.Values.All(static c => c.BackCount == 0); // Alternatively, all of the states are "base"
        if (!isValidItem) {
            var firstItem = FindFirstItemReplacement(change.Item);
            var secondItem = item with { PrevId = firstItem.Id };
            return new MustFixFirstItemResult(firstItem, secondItem);
        }

        var changes = change.Changes(exceptUriState: true).ToList();
        if (!change.IsUriChanged && changes.Count == 0)
            return null;

        // Check for possible transition back in history
        var mustMoveBack =
            !backItem.IsNone // There is possibility to move back
            && changes.Any(static c => c.State.BackCount > 0) // Any of changed states allows to go back
            && backItem.IsIdenticalTo(item); // And the whole "back" state is identical to the expected one
        if (mustMoveBack)
            return new MustGoBackResult();

        // HistoryChangeReason.NavigateToNewUri is processed earlier, so we know for sure we already
        // have history position tag in browser's state.
        // Thus we need to either just save the changes, or push the new "forward" state.
        // And we push the "forward" state, when:
        // - Uri has changed
        // - Or one of changes implies you can go back from it.
        var mustMoveForward =
            change.IsUriChanged
            || changes.Any(c => c.State.BackCount > c.PrevState.BackCount);
        if (mustMoveForward)
            return new MustNavigateResult(false);

        return new MustUpdateItemResult();
    }

    private void ProcessApplyResult(HistoryChangeApplyResult? result, HistoryItem item)
    {
        if (result == null)
            return;

        switch (result) {
        case MustUpdateItemResult:
            AddOrReplaceHistoryItem(item, true);
            break;
        case MustGoBackResult:
            // We don't need to save the new item here, coz it matches
            // the item we're going to go back to
            Hub.JS.EvalVoid("window.history.back()");
            break;
        case MustNavigateResult mustNavigateResult:
            AddOrReplaceHistoryItem(item, mustNavigateResult.MustReplace);
            Hub.Nav.NavigateTo(Uri, new NavigationOptions() {
                ForceLoad = false,
                ReplaceHistoryEntry = mustNavigateResult.MustReplace,
                HistoryEntryState = Hub.PositionFormatter.Format(_position),
            });
            break;
        case MustFixFirstItemResult mustFixFirstItemResult:
            var (item1, item2) = mustFixFirstItemResult;
            var (p1, p2) = (_position, _position + 1);
            var s1 = Hub.PositionFormatter.Format(p1);
            var s2 = Hub.PositionFormatter.Format(p2);
            item1 = item1 with { FollowUpAction = () => {
                Hub.Nav.NavigateTo(Hub.Nav.Uri, new NavigationOptions() {
                    ForceLoad = false,
                    ReplaceHistoryEntry = false,
                    HistoryEntryState = s2,
                });
            }};
            AddOrReplaceHistoryItem(item1, true);
            AddOrReplaceHistoryItem(item2, false);
            Hub.Nav.NavigateTo(Hub.Nav.Uri, new NavigationOptions() {
                ForceLoad = false,
                ReplaceHistoryEntry = true,
                HistoryEntryState = s1,
            });
            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(result));
        }
    }

    private HistoryItem FindFirstItemReplacement(HistoryItem item)
    {
        var baseItem = item;
        var seenIds = new HashSet<int>();
        while (true) {
            item = GetItemById(item.PrevId);
            var id = item.Id;
            if (seenIds.Contains(id)) {
                Log.LogError("Cycle in history item dependency graph, repeating item: {Item}", item);
                return Default();
            }

            seenIds.Add(id);
            if (item.IsNone || !OrdinalEquals(baseItem.Uri, item.Uri))
                return Default();
            if (item.States.Any(kv => kv.Value.BackCount < (baseItem[kv.Key]?.BackCount ?? 0)))
                return item;
        }

        HistoryItem Default()
            => (_defaultItem with { Id = NextItemId() }).With(new UriState(baseItem.Uri));
    }

    // History manipulation

    private void AddOrReplaceHistoryItem(HistoryItem item, bool mustReplace)
    {
        if (mustReplace)
            _history[_position] = item.Id;
        else {
            ExpandHistory(++_position);
            _history[_position] = item.Id;
            TrimHistory(); // When item is added, future history is wiped out
        }
        _itemById[item.Id] = item;
        DebugLog?.LogDebug(
            "AddOrReplaceHistoryItem({MustReplace}): History = [{Items}]",
            mustReplace, _history.ToDelimitedString(", "));
    }

    private void MovePosition(int position)
    {
        ExpandHistory(position);
        _position = position;
    }

    private void TrimHistory()
        => TrimHistory(_position);

    private void TrimHistory(int position)
    {
        var lastPosition = _history.Count - 1;
        if (lastPosition == 0)
            return; // Nothing to trim

        position.Clamp(0, lastPosition);
        if (position < lastPosition)
            _history.RemoveRange(position + 1, lastPosition - position);
    }

    private void ExpandHistory(int position)
    {
        var lastItem = _history.Count > 0 ? _history[^1] : default;
        while (position >= _history.Count)
            _history.Add(lastItem);
    }

    private int NextItemId()
        => Interlocked.Increment(ref _nextItemId);
}
