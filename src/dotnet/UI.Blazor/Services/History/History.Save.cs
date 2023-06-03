using ActualChat.Concurrency;

namespace ActualChat.UI.Blazor.Services;

public partial class History
{
    private readonly RegionalValue<bool> _isSaveSuppressed;
    private readonly NoRecursionRegion _saveRegion;

    public HistoryItem Save()
    {
        Dispatcher.AssertAccess();
        using var _ = _saveRegion.Enter();
        if (_isSaveSuppressed.Value) {
            DebugLog?.LogDebug("Save: suppressed");
            return HistoryItem.Null;
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

    public HistoryItem Save<TState>()
        => Save(typeof(TState));
    public HistoryItem Save(Type stateType)
    {
        Dispatcher.AssertAccess();
        using var _ = _saveRegion.Enter();
        if (_isSaveSuppressed.Value) {
            DebugLog?.LogDebug("Save {StateType}: suppressed", stateType.GetName(true));
            return HistoryItem.Null;
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

    private HistoryItem EndSave(HistoryItem item, HistoryItem baseItem)
    {
        DebugLog?.LogDebug(
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            "EndSave: changes:" + item.GetChanges(baseItem, false).Select(c => "\r\n- " + c).ToDelimitedString(""));
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
            _saveRegion.ExitAction = () => _ = AddHistoryEntry(item, true);
            DebugLog?.LogDebug("EndSave: +AddHistoryEntry({Item})", item.ToString());
            break;
        case < 0:
            // Backward state
            var backItem = GetItemById(baseItem.BackItemId);
            if (backItem != null && backItem.IsIdenticalTo(item)) {
                item = _currentItem = backItem;
                _saveRegion.ExitAction = () => _ = NavigateBack(true);
                DebugLog?.LogDebug("EndSave: +NavigateBack({Item})", item.ToString());
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
            _saveRegion.ExitAction = () => _ = AddHistoryEntry(itemCopy, true);
            DebugLog?.LogDebug("EndSave: +AddHistoryEntry({Item})", itemCopy.ToString());
        }
        else
            DebugLog?.LogDebug("EndSave: done");
    }
}
