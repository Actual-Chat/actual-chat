using ActualChat.Concurrency;
using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    private readonly NoRecursionRegionWithExitAction _locationChangeRegion;

    private void LocationChange(
        LocationChangedEventArgs eventArgs,
        HistoryItem? newItem = null,
        bool mustReplace = false)
    {
        using var _ = _locationChangeRegion.Enter();
        if (DebugLog != null) {
            var intercepted = eventArgs.IsNavigationIntercepted ? "intercepted" : "internal";
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            DebugLog.LogDebug($"-> LocationChange: {intercepted} '{{Location}}' #{{State}}",
                eventArgs.Location,
                eventArgs.HistoryEntryState);
        }

        try {
            // Saving current state
            Save();

            var uri = _uri = Nav.GetLocalUrl().Value;
            var lastItem = _currentItem;
            if (lastItem.Id == 0)
                throw StandardError.Internal("Something is off: CurrentItem.Id == 0");

            HistoryItem currentItem;
            var locationChangeKind = LocationChangeKind.HistoryMove;
            if (newItem == null
                && ItemIdFormatter.Parse(eventArgs.HistoryEntryState) is { } itemId
                && GetItemByIdUnsafe(itemId) is { } existingItem) {
                currentItem = _currentItem = existingItem;
                if (!OrdinalEquals(uri, currentItem.Uri)) {
                    Log.LogWarning(
                        "LocationChange: Uri mismatch, expected: {Expected}, actual: {Actual} - fixed",
                        currentItem.Uri,
                        uri);
                    currentItem = currentItem.WithUri(uri);
                    ReplaceItem(ref currentItem);
                }
                if (currentItem.OnNavigation is { IsNone: false } onNavigation) {
                    DebugLog?.LogDebug("LocationChange: OnNavigation action: {OnNavigation}", onNavigation);
                    currentItem = currentItem with { OnNavigation = default };
                    ReplaceItem(ref currentItem);
                    _locationChangeRegion.ExitAction = onNavigation.Action;
                    return;
                }
            }
            else {
                locationChangeKind = LocationChangeKind.NewUri;
                currentItem = newItem ?? NewItemUnsafe();
                if (mustReplace)
                    ReplaceItem(ref currentItem);
                else
                    AddItem(ref currentItem);
                _locationChangeRegion.ExitAction = () => {
                    if (newItem != null && !mustReplace)
                        AddNavigationHistoryEntry(currentItem);
                    else
                        ReplaceNavigationHistoryEntry(currentItem);
                };
            }

            /*
            if (currentItem.IsIdenticalTo(lastItem)) {
                DebugLog?.LogDebug("LocationChange: same state as now, nothing to do");
                return;
            }
            */

            var transition = new HistoryTransition(currentItem, lastItem, locationChangeKind);
            DebugLog?.LogDebug(
                // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
                $"<- LocationChange: transition #{lastItem.Id} -> #{CurrentItem.Id}, {transition}");
            Transition(transition);
        }
        finally {
            try {
                LocationChanged?.Invoke(this, eventArgs);
            }
            catch (Exception ex) {
                Log.LogError(ex, "LocationChange: One of LocationChanged handlers failed");
            }
            var exitAction = _locationChangeRegion.ExitAction!;
            _locationChangeRegion.ExitAction = exitAction == Delegates.Noop
                ? _processNextNavigationActionUnsafeCached
                : () => {
                    try {
                        exitAction.Invoke();
                    }
                    finally {
                        ProcessNextNavigationUnsafe();
                    }
                };
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
        var backItem = GetItemByIdUnsafe(item.BackItemId);
        var mustAddBackItem =
            backItem == null // There is no "back" item in history
            && item.HasBackSteps; // And the state isn't "base"
        if (!mustAddBackItem)
            return;

        // Adding "Back" item
        if (ReplaceItem(ref item, out backItem)) {
            DebugLog?.LogDebug("Transition: adding Back item");
            _locationChangeRegion.ExitAction = () => AddNavigationHistoryEntry(item);
        }
        else
            Log.LogWarning("Transition: Back item couldn't be added");
    }
}
