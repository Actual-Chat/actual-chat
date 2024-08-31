using ActualChat.UI.Blazor.Services.Internal;

namespace ActualChat.UI.Blazor.Services;

public readonly record struct HistoryTransition(
    HistoryItem Item,
    HistoryItem BaseItem,
    LocationChangeKind LocationChangeKind)
{
    public bool IsUriChanged => !OrdinalEquals(Item.Url, BaseItem.Url);

    public override string ToString()
        => $"({LocationChangeKind}: {BaseItem} -> {Item})";
}
