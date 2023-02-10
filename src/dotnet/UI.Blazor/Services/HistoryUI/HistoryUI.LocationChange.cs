using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI
{
    private Action? _afterLocationChange;

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        DebugLog?.LogDebug("-> OnLocationChanged: {Intercepted}, '{Location}' #{State}",
            e.IsNavigationIntercepted ? "intercepted" : "internal",
            e.Location,
            e.HistoryEntryState);

        Monitor.Enter(Lock);
        try {
            if (_afterLocationChange != null)
                throw StandardError.Internal("OnLocationChanged is called recursively.");
            _afterLocationChange = NoAction;

            using (SuppressSave()) {
                try {
                    LocationChanging?.Invoke(this, e);
                }
                catch (Exception ex) {
                    Log.LogError(ex, "One of LocationChanging handlers failed");
                }
            }
            // Saving pre-transition state
            Save();

            var uri = _uri = Hub.Nav.GetLocalUrl().Value;
            var (prevItem, prevPosition) = (CurrentItemUnsafe, _position);
            var parsedPosition = Hub.PositionFormatter.Parse(e.HistoryEntryState);

            var locationChangeKind = LocationChangeKind.HistoryMove;
            if (parsedPosition is { } vPosition) {
                MovePosition(vPosition);
                var item = CurrentItemUnsafe;
                if (item.OnNavigate is { } onNavigate) {
                    DebugLog?.LogDebug("OnLocationChanged: got OnNavigate action");
                    item = item with { OnNavigate = null };
                    ReplaceHistoryItem(item);
                    _afterLocationChange = onNavigate;
                    return;
                }
                if (vPosition == prevPosition && OrdinalEquals(uri, prevItem.Uri)) {
                    DebugLog?.LogDebug("OnLocationChanged: nothing to do");
                    return;
                }
                if (!OrdinalEquals(uri, item.Uri)) {
                    Log.LogWarning(
                        "OnLocationChanged: Uri mismatch, expected: {Expected}, actual: {Actual} - fixed",
                        item.Uri, uri);
                    ReplaceHistoryItem(item.WithUri(uri));
                }
            }
            else {
                locationChangeKind = LocationChangeKind.NewUri;
                var item = NewItem(uri);
                AddHistoryItem(item);
                var position = _position;
                _afterLocationChange = () => ReplaceNavigationHistoryEntry(position);
            }

            { // The new scope is needed just to be able to use "item" here
                var (item, position) = (CurrentItemUnsafe, _position);
                var transition = new HistoryTransition(item, prevItem, locationChangeKind);
                DebugLog?.LogDebug(
                    "<- OnLocationChanged: transition to #{Position} <- #{PrevPosition}, {Transition}",
                    position, prevPosition, transition);
                Transition(transition);
            }

            try {
                LocationChanged?.Invoke(this, e);
            }
            catch (Exception ex) {
                Log.LogError(ex, "One of LocationChanged handlers failed");
            }
        }
        finally {
            var afterLocationChange = _afterLocationChange;
            _afterLocationChange = null;
            Monitor.Exit(Lock);

            try {
                afterLocationChange?.Invoke();
            }
            catch (Exception ex) {
                Log.LogError(ex, "AfterLocationChange action failed");
            }
            DebugLog?.LogDebug("<- OnLocationChanged");
        }
    }

    private void Transition(HistoryTransition transition)
    {
        var (item, prevItem, locationChangeKind) = transition;
        if (item.States.Count != _defaultItem.States.Count)
            throw StandardError.Internal(
                "Count of history states doesn't match to count of registered states.");

        // Applying changes
        var changes = item.GetChanges(prevItem).ToList();
        DebugLog?.LogDebug("Transition: applying {ChangeCount} change(s):", changes.Count);
        int n = 1;
        using (SuppressSave()) {
            foreach (var stateChange in changes) {
                var state = stateChange.State;
                DebugLog?.LogDebug(
                    "Change {Number}:\r\n- From: {PrevState}\r\n- To:   {State}",
                    n++, stateChange.PrevState, state);
                try {
                    state.Apply(transition);
                }
                catch (Exception e) {
                    Log.LogError(e, "Transition: Apply has failed for history state '{State}'", state);
                    // And we must still go on here
                }
            }
        }

        // Saving changes - Apply calls above should trigger Save, but we suppressed them
        try {
            Save();
            item = CurrentItemUnsafe; // That's where the changes are saved
        }
        catch (Exception e) {
            Log.LogError(e, "Transition: Save has failed");
            // And we must still go on here
        }

        // All the changes are applied at this moment, and the new state is saved
        if (locationChangeKind is LocationChangeKind.NewUri)
            return;

        // Special case: we might be at the very beginning of the history,
        // but current state requires us to have "Back" item.
        var backItem = BackItemUnsafe;
        var mustAddBackItem =
            backItem == null // There is no "back" item in history
            && item.States.Values.Any(static c => c.BackStepCount != 0); // And the state isn't "base"
        if (!mustAddBackItem)
            return;

        // Adding "Back" item
        DebugLog?.LogDebug("Transition: adding Back item");
        AddBackItem(item);
    }

    private void AddBackItem(HistoryItem item)
    {
        var nextPosition = _position + 1;
        var backItem = FindValidBackItem(item) with {
            OnNavigate = () => AddNavigationHistoryEntry(nextPosition),
        };
        item = item with {
            PrevId = backItem.Id,
        };
        ReplaceHistoryItem(backItem);
        AddHistoryItem(item); // Note that we "virtually" move to this
    }
}
