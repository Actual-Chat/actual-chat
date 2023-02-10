namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI
{
    private static readonly Action NoAction = () => {};

    private bool _isSaveSuppressed;
    private Action? _afterSave;

    public void Save()
    {
        DebugLog?.LogDebug("Save");
        using var _ = BeginSave();
        var oldItem = CurrentItemUnsafe;
        if (_isSaveSuppressed || oldItem.OnNavigate != null) {
            DebugLog?.LogDebug("Save: suppressed {Reason}", _isSaveSuppressed ? "directly" : "via OnNavigate");
            return;
        }

        var newItem = oldItem;
        foreach (var (stateKey, state) in oldItem.States) {
            var newState = state.Save();
            if (ReferenceEquals(newState, state) || Equals(newState, state))
                continue;

            newItem = newItem with { States = newItem.States.SetItem(stateKey, newState) };
        }
        EndSave(newItem, oldItem);
    }

    public void Save<TState>() => Save(typeof(TState));
    public void Save(Type stateType)
    {
        DebugLog?.LogDebug("Save: stateType: {StateType}", stateType.GetName(true));
        using var _ = BeginSave();
        var oldItem = CurrentItemUnsafe;
        if (_isSaveSuppressed || oldItem.OnNavigate != null) {
            DebugLog?.LogDebug("Save: suppressed {Reason}", _isSaveSuppressed ? "directly" : "via OnNavigate");
            return;
        }

        var state = oldItem.States[stateType];
        var newState = state.Save();
        if (ReferenceEquals(newState, state) || Equals(newState, state))
            return;

        var newItem = oldItem.With(newState);
        EndSave(newItem, oldItem);
    }

    // Private methods

    private ClosedDisposable<HistoryUI> BeginSave()
    {
        Monitor.Enter(Lock);
        try {
            ThrowIfSaving();
            _afterSave = NoAction;
        }
        catch (Exception) {
            Monitor.Exit(Lock);
            throw;
        }

        return new ClosedDisposable<HistoryUI>(this, static self => {
            var afterSave = self._afterSave;
            self._afterSave = null;
            Monitor.Exit(self.Lock);
            afterSave?.Invoke();
        });
    }

    private ClosedDisposable<(HistoryUI, bool)> SuppressSave(bool mustSuppress = true)
    {
        var wasSaveSuppressed = _isSaveSuppressed;
        _isSaveSuppressed = mustSuppress;
        return new ClosedDisposable<(HistoryUI, bool)>((this, wasSaveSuppressed), static arg => {
            var (self, wasSaveSuppressed1) = arg;
            self._isSaveSuppressed = wasSaveSuppressed1;
        });
    }

    private void EndSave(HistoryItem newItem, HistoryItem oldItem)
    {
        if (_afterLocationChange != null) {
            // Processing OnLocationChange
            ReplaceHistoryItem(newItem);
            DebugLog?.LogDebug("EndSave: done (OnLocationChange)");
            return;
        }

        var backStepDelta = newItem.CompareBackStepCount(oldItem);
        DebugLog?.LogDebug(
            "EndSave: back step count: {Old} -> {New} ({Delta})",
            oldItem.BackStepCount, newItem.BackStepCount, backStepDelta);

        switch (backStepDelta) {
        case 0:
            ReplaceOrAddBackItem(newItem);
            break;
        case > 0:
            // Forward state
            newItem = newItem with {
                Id = NextItemId(),
                PrevId = oldItem.Id,
            };
            AddHistoryItem(newItem);
            _afterSave = () => AddNavigationHistoryEntry(_position);
            DebugLog?.LogDebug("EndSave: +AddNavigationHistoryEntry");
            break;
        case < 0:
            // Backward state
            var backItem = BackItemUnsafe;
            if (backItem != null && backItem.IsIdenticalTo(newItem)) {
                _afterSave = NavigateBack;
                DebugLog?.LogDebug("EndSave: +NavigateBack");
            }
            ReplaceOrAddBackItem(newItem);
            break;
        }

        void ReplaceOrAddBackItem(HistoryItem item)
        {
            if (item.HasBackSteps && BackItemUnsafe == null) {
                var position = _position;
                AddBackItem(item);
                _afterSave = () => AddNavigationHistoryEntry(position);
                DebugLog?.LogDebug("EndSave: +AddNavigationHistoryEntry");
            }
            else {
                ReplaceHistoryItem(item);
                DebugLog?.LogDebug("EndSave: done");
            }
        }
    }

    private void ThrowIfSaving()
    {
        if (_afterSave != null)
            throw StandardError.Constraint("Save cannot be called recursively.");
    }
}
