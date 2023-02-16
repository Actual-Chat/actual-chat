using ActualChat.Concurrency;

namespace ActualChat.UI.Blazor.Services;

public partial class HistoryUI
{
    private readonly LocalValue<bool> _isSaveSuppressed;
    private readonly LockedRegionWithExitAction _saveRegion;

    public HistoryItem? Save()
    {
        using var _ = _saveRegion.Enter();
        if (_isSaveSuppressed.Value) {
            DebugLog?.LogDebug("Save: suppressed");
            return null;
        }
        DebugLog?.LogDebug("Save");

        var baseItem = _currentItem;
        var item = baseItem;
        foreach (var (stateKey, state) in baseItem.States) {
            var newState = state.Save();
            if (ReferenceEquals(newState, state) || Equals(newState, state))
                continue;

            item = item with { States = item.States.SetItem(stateKey, newState) };
        }
        return EndSave(item, baseItem);
    }

    public HistoryItem? Save<TState>() => Save(typeof(TState));
    public HistoryItem? Save(Type stateType)
    {
        using var _ = _saveRegion.Enter();
        if (_isSaveSuppressed.Value) {
            DebugLog?.LogDebug("Save {StateType}: suppressed", stateType.GetName(true));
            return null;
        }
        DebugLog?.LogDebug("Save {StateType}:", stateType.GetName(true));

        var baseItem = _currentItem;
        var state = baseItem.States[stateType];
        var newState = state.Save();
        if (ReferenceEquals(newState, state) || Equals(newState, state))
            return baseItem;

        var item = baseItem.With(newState);
        return EndSave(item, baseItem);
    }

    // Private methods

    private HistoryItem? EndSave(HistoryItem item, HistoryItem baseItem)
    {
        if (_locationChangeRegion.IsInside) {
            // Processing OnLocationChange
            ReplaceItem(ref item);
            DebugLog?.LogDebug("EndSave: exit (LocationChange)");
            return item;
        }

        var backStepDelta = item.CompareBackStepCount(baseItem);
        DebugLog?.LogDebug(
            "EndSave: back step count: {Old} -> {New} ({Delta})",
            baseItem.BackStepCount, item.BackStepCount, backStepDelta);

        switch (backStepDelta) {
        case 0:
            EndSave(ref item);
            break;
        case > 0:
            // Forward state
            item = item with {
                Id = NewItemId(), // Will be set by AddHistoryItem
                BackItemId = baseItem.Id,
            };
            AddItem(ref item);
            _saveRegion.ExitAction = () => AddNavigationHistoryEntry(item);
            DebugLog?.LogDebug("EndSave: +AddNavigationHistoryEntry");
            break;
        case < 0:
            // Backward state
            var backItem = GetItemByIdUnsafe(baseItem.BackItemId);
            if (backItem != null && backItem.IsIdenticalTo(item)) {
                item = _currentItem = backItem;
                _saveRegion.ExitAction = NavigateBack;
                DebugLog?.LogDebug("EndSave: +NavigateBack");
            }
            else
                EndSave(ref item);
            break;
        }
        return item;
    }

    private void EndSave(ref HistoryItem item)
    {
        if (ReplaceItem(ref item, out var _)) {
            var itemCopy = item;
            _saveRegion.ExitAction = () => AddNavigationHistoryEntry(itemCopy);
            DebugLog?.LogDebug("EndSave: +AddNavigationHistoryEntry");
        }
        else
            DebugLog?.LogDebug("EndSave: done");
    }
}
