using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public enum HistoryChangeReason
{
    Update = 0,
    NavigateToHistoryItem = 1,
    NavigateToNewUri = 2,
}

public sealed record HistoryChange(
    HistoryHub Hub,
    HistoryItem Item,
    HistoryItem PrevItem,
    int? Position,
    int PrevPosition,
    HistoryChangeReason Reason
) {
    public bool IsUriChanged => !OrdinalEquals(Item.Uri, PrevItem.Uri);

    public override string ToString()
        => $"{GetType().Name}({Item} <- {PrevItem}, #{Position} <- #{PrevPosition}, Reason: {Reason})";

    public void Deconstruct(out HistoryItem item, out HistoryItem prevItem)
    {
        item = Item;
        prevItem = PrevItem;
    }

    public void Deconstruct(out HistoryItem item, out HistoryItem prevItem, out int? position, out int prevPosition)
    {
        item = Item;
        prevItem = PrevItem;
        position = Position;
        prevPosition = PrevPosition;
    }

    public IEnumerable<HistoryStateChange> Changes(bool exceptUriState = false)
    {
        foreach (var (stateType, state) in Item.States) {
            if (exceptUriState && stateType == typeof(UriState))
                continue;

            var prevState = PrevItem[stateType];
            var change = new HistoryStateChange(state, prevState!);
            if (change.HasChanges)
                yield return change;
        }
    }

    // "With" helpers

    public HistoryChange With<TState>(TState state)
        where TState : HistoryState
        => With(Item.With(state));

    public HistoryChange With(Type stateType, HistoryState state)
        => With(Item.With(stateType, state));

    public HistoryChange With(HistoryItem item)
    {
        if (Item.IsIdenticalTo(item))
            return this;
        return this with { Item = item };
    }

}
