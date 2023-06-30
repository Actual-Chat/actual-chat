using ActualChat.Concurrency;
using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    private readonly NoRecursionRegion _locationChangeRegion;

    private void LocationChange(LocationChangedEventArgs eventArgs, bool mustReplace = false)
    {
        using var _1 = _locationChangeRegion.Enter();
        _whenReadySource.TrySetResult();
        var historyEntryState = eventArgs.HistoryEntryState;
        var parsedHistoryEntryState = ItemIdFormatter.Parse(historyEntryState);
        DebugLog?.LogDebug(
            "-> LocationChange: {Kind} '{Location}' #{ParsedState} / {UnparsedState}",
            eventArgs.IsNavigationIntercepted ? "intercepted" : "internal",
            eventArgs.Location, parsedHistoryEntryState?.Format() ?? "null", historyEntryState);

        Action? exitAction = null;
        try {
            // Saving the current state
            Save();

            var uri = _uri = Nav.GetLocalUrl().Value;
            var lastItem = _currentItem;
            if (lastItem.Id == 0)
                throw StandardError.Internal("Something is off: CurrentItem.Id == 0");

            HistoryItem currentItem;
            var locationChangeKind = LocationChangeKind.HistoryMove;
            var existingItemId = parsedHistoryEntryState.GetValueOrDefault();
            var hasValidHistoryEntryState = existingItemId > 0;
            var existingItem = hasValidHistoryEntryState && GetItemById(existingItemId) is { } item ? item : null;
            if (existingItem != null) {
                if (OrdinalEquals(uri, existingItem.Uri)) {
                    currentItem = _currentItem = existingItem;
                    if (currentItem.OnNavigation is {IsNone: false} onNavigation) {
                        DebugLog?.LogDebug("LocationChange: OnNavigation action: {OnNavigation}", onNavigation);
                        currentItem = currentItem with {OnNavigation = default};
                        ReplaceItem(ref currentItem);
                        _locationChangeRegion.ExitAction = onNavigation.Action;
                        return;
                    }
                }
                else {
                    // Navigation with keeping state but changing Uri happened
                    currentItem = _currentItem = existingItem.WithUri(uri);
                    ReplaceItem(ref currentItem, false);
                    locationChangeKind = LocationChangeKind.NewUri;
                }
            }
            else {
                locationChangeKind = LocationChangeKind.NewUri;
                currentItem = hasValidHistoryEntryState ? NewItem(existingItemId) : NewItem();
                if (mustReplace) {
                    ReplaceItem(ref currentItem, false);
                    if (!hasValidHistoryEntryState)
                        exitAction = () => _ = ReplaceHistoryEntry(currentItem, true);
                }
                else {
                    AddItem(ref currentItem, false);
                    if (!hasValidHistoryEntryState)
                        exitAction = () => _ = AddHistoryEntry(currentItem, true);
                }
            }

            var transition = new HistoryTransition(currentItem, lastItem, locationChangeKind);
            DebugLog?.LogDebug(
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                $"LocationChange: transition #{lastItem.Id} -> #{CurrentItem.Id}, {transition}");
            Transition(transition);
        }
        finally {
            try {
                LocationChanged?.Invoke(this, eventArgs);
            }
            catch (Exception e) {
                Log.LogError(e, "LocationChange: One of LocationChanged handlers failed");
            }
            try {
                exitAction?.Invoke();
            }
            catch (Exception e) {
                Log.LogError(e, "LocationChange: exit action failed");
            }
            if (parsedHistoryEntryState is { } expectedItemId) {
                DebugLog?.LogDebug("<- LocationChange: completing #{ExpectedItemId}", expectedItemId);
                NavigationQueue.TryComplete(expectedItemId);
            }
            else
                DebugLog?.LogDebug("<- LocationChange");
        }
    }

    private void Transition(HistoryTransition transition)
    {
        var (item, baseItem, locationChangeKind) = transition;
        if (item.States.Count != _defaultItem.States.Count)
            throw StandardError.Internal(
                "Count of history states doesn't match to count of registered states.");

        // Applying changes
        var changes = item.GetChanges(baseItem, true).ToList();
        DebugLog?.LogDebug("Transition: applying {ChangeCount} change(s):", changes.Count);
        using (_isSaveSuppressed.Change(true)) {
            foreach (var change in changes) {
                var state = change.State;
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                DebugLog?.LogDebug("- " + change);
                try {
                    state.Apply(transition);
                }
                catch (Exception e) {
                    Log.LogError(e, "Transition: Apply has failed for history state {State}", state);
                    // And we must still go on here
                }
            }
        }

        //  state.Apply(...) calls above were running in SuppressSave() block, so we save everything here
        try {
            Save();
            item = _currentItem; // That's where the changes are saved
        }
        catch (Exception e) {
            Log.LogError(e, "Transition: Save has failed");
            // And we must still go on here
        }

        // All the changes are applied at this moment, and the new state is saved
        if (locationChangeKind is LocationChangeKind.NewUri)
            return;

        if (_locationChangeRegion.ExitAction != Delegates.Noop) {
            Log.LogWarning("Transition: check for Back item is skipped (ExitAction != Noop)");
            return;
        }

        // Special case: we might be at the very beginning of the history,
        // but current state requires us to have "Back" item.
        var backItem = GetItemById(item.BackItemId);
        var mustAddBackItem =
            backItem == null // There is no "back" item in history
            && item.HasBackSteps; // And the state isn't "base"
        if (!mustAddBackItem)
            return;

        // Adding "Back" item
        if (ReplaceItem(ref item, out backItem)) {
            DebugLog?.LogDebug("Transition: adding Back item");
            _locationChangeRegion.ExitAction = () => _ = AddHistoryEntry(item);
        }
        else
            Log.LogWarning("Transition: Back item couldn't be added");
    }
}
